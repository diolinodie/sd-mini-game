using BarRaider.SdTools;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;

namespace MiniGame
{
    /// <summary>
    /// 手足球機（桌上足球）的比賽狀態。同一台 Stream Deck+ 上的 4 個 FoosballAction 共用一個實例，
    /// 整條觸控條 800x100 是球場，每個 action 只顯示自己那一格 200x100。
    ///
    /// 桿子順序跟真實足球機一樣（左到右）：
    ///   紅 守門員、紅 後衛、藍 前鋒、紅 中場、藍 中場、紅 前鋒、藍 後衛、藍 守門員
    /// 旋鈕控制：
    ///   旋鈕 1（紅）：紅 守門員 + 紅 後衛
    ///   旋鈕 2（藍）：藍 前鋒 + 藍 中場
    ///   旋鈕 3（紅）：紅 中場 + 紅 前鋒
    ///   旋鈕 4（藍）：藍 後衛 + 藍 守門員
    /// 紅隊守左邊球門往右攻，藍隊守右邊球門往左攻。
    /// 球員正反兩面都會擋球（自己人也是），按住旋鈕可以抬腳讓球過去；
    /// 腳前的球往前踢，背後的球往後撥（比較弱）。
    /// </summary>
    public sealed class FoosballGame
    {
        #region 共用實例（每台裝置一個）

        private static readonly Dictionary<string, FoosballGame> games = new Dictionary<string, FoosballGame>();
        private static readonly object gamesLock = new object();

        public static FoosballGame Acquire(string deviceId)
        {
            deviceId = deviceId ?? "";
            lock (gamesLock)
            {
                if (!games.TryGetValue(deviceId, out FoosballGame game))
                {
                    game = new FoosballGame(deviceId);
                    games[deviceId] = game;
                }
                game.refCount++;
                return game;
            }
        }

        public void Release()
        {
            lock (gamesLock)
            {
                refCount--;
                if (refCount > 0)
                    return;
                games.Remove(DeviceId);
            }

            lock (sync)
            {
                disposed = true;
                frameTimer?.Dispose();
                frameTimer = null;
            }
            Logger.Instance.LogMessage(TracingLevel.INFO, $"foosball, game released device={DeviceId}");
        }

        #endregion

        #region 常數

        public const int SLOTS = 4;
        public const int SLOT_W = 200;
        private const int W = SLOT_W * SLOTS;          // 800
        private const int H = 100;
        private const int FIELD_TOP = 10;              // 上面 10px 是記分列
        private const int FIELD_H = H - FIELD_TOP;     // 90
        private const int GOAL_H = 30;
        private const int GOAL_TOP = FIELD_TOP + (FIELD_H - GOAL_H) / 2;
        private const int GOAL_BOTTOM = GOAL_TOP + GOAL_H;

        private const int PW = 6;                      // 球員寬
        private const int PH = 10;                     // 球員高
        private const double BALL_R = 3;

        private const int FRAME_MS = 40;               // 25 fps
        private const int KICK_FRAMES = 6;             // 踢球動畫總幀數
        private const int KICK_ACTIVE = 3;             // 前幾幀碰到球才算踢到
        private const double KICK_REACH = 16;          // 桿子左右多遠內的球踢得到
        private const double KICK_SPEED = 11;
        private const double BACK_KICK_SPEED = 5;      // 背後的球往後撥的速度
        private const double BOUNCE_JITTER = 0.4;      // 碰撞時加一點上下亂數，免得球永遠在同一條線上來回彈
        private const double MAX_VX = 12;
        private const double MAX_VY = 5;
        private const double MIN_VX = 0.8;             // 球不會完全停下來，避免卡在兩支桿子中間
        private const double FRICTION = 0.992;
        private const int GOAL_PAUSE_FRAMES = 40;      // 進球後停 1.6 秒

        public const int TEAM_RED = 0;
        public const int TEAM_BLUE = 1;

        private static readonly Color RedColor = Color.FromArgb(0xFF, 0x4C, 0x4C);
        private static readonly Color BlueColor = Color.FromArgb(0x4C, 0x8B, 0xFF);
        private static readonly Color FieldColor = Color.FromArgb(0x1F, 0x6B, 0x32);
        private static readonly int[] KickReach = { 6, 12, 16, 12, 8, 3 };   // 踢球每一幀腳伸出去的長度

        #endregion

        #region 狀態

        private enum GameState { Idle, Playing, Goal, Paused, GameOver }

        private sealed class Rod
        {
            public int Team;
            public int Column;
            public int X;
            public int Players;
            public double Spacing;
            public double MaxOffset;
            public double Offset;
            public int Kick;          // >0 表示踢球動畫進行中，倒數
            public bool KickHit;      // 這一腳已經踢到球
            public bool Held;         // 旋鈕還按著

            // 踢完還按著旋鈕 = 桿子轉起來、球員抬腳，球可以從底下過去
            public bool Lifted => Held && Kick == 0;

            public int Dir => Team == TEAM_RED ? 1 : -1;

            public double PlayerY(int i)
            {
                double baseY = Players == 1 ? FIELD_H / 2.0 : Spacing * (i + 0.5);
                return FIELD_TOP + baseY + Offset;
            }
        }

        private static Rod MakeRod(int team, int column, int x, int players)
        {
            double spacing = FIELD_H / (double)players;
            return new Rod
            {
                Team = team,
                Column = column,
                X = x,
                Players = players,
                Spacing = spacing,
                // 守門員只顧球門前；其他桿子剛好讓相鄰球員的範圍接起來、最外側的球員碰到邊線
                MaxOffset = players == 1 ? GOAL_H / 2.0 + 5 : spacing / 2 - PH / 2.0,
            };
        }

        public string DeviceId { get; }

        private readonly object sync = new object();
        private readonly Random rng = new Random();
        private readonly List<Rod> rods;
        private readonly Action<string>[] slots = new Action<string>[SLOTS];
        private readonly string[] lastSent = new string[SLOTS];
        private readonly int[] score = new int[2];

        private int refCount;
        private GameState state = GameState.Idle;
        private double bx, by, vx, vy;
        private int goalPause;
        private int lastScorer;
        private int goalsToWin = 5;

        private Timer frameTimer;
        private bool timerRunning;
        private int inFrame;
        private bool disposed;

        #endregion

        private FoosballGame(string deviceId)
        {
            DeviceId = deviceId;
            rods = new List<Rod>
            {
                MakeRod(TEAM_RED,  0,  25, 1),   // 紅 守門員
                MakeRod(TEAM_RED,  0, 125, 2),   // 紅 後衛
                MakeRod(TEAM_BLUE, 1, 250, 3),   // 藍 前鋒
                MakeRod(TEAM_RED,  2, 350, 2),   // 紅 中場
                MakeRod(TEAM_BLUE, 1, 450, 2),   // 藍 中場
                MakeRod(TEAM_RED,  2, 550, 3),   // 紅 前鋒
                MakeRod(TEAM_BLUE, 3, 675, 2),   // 藍 後衛
                MakeRod(TEAM_BLUE, 3, 775, 1),   // 藍 守門員
            };
            frameTimer = new Timer(OnFrame, null, Timeout.Infinite, Timeout.Infinite);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"foosball, game created device={deviceId}");
        }

        #region 給 action 呼叫的介面

        public static int TeamOfColumn(int column)
        {
            return column % 2 == 0 ? TEAM_RED : TEAM_BLUE;
        }

        public void Attach(int column, Action<string> sink)
        {
            lock (sync)
            {
                slots[column] = sink;
                lastSent[column] = null;
                // 其他格的提示文字（n/4）也要更新
                for (int i = 0; i < SLOTS; i++) lastSent[i] = null;
            }
            RenderAndSend();
        }

        public void Detach(int column, Action<string> sink)
        {
            lock (sync)
            {
                if (slots[column] != sink)
                    return;
                slots[column] = null;
                lastSent[column] = null;
                // 換頁時收不到 DialUp，不清掉的話桿子會一直抬著
                foreach (Rod rod in rods)
                    if (rod.Column == column) rod.Held = false;
            }
            RenderAndSend();
        }

        public void SetGoalsToWin(int n)
        {
            lock (sync)
            {
                goalsToWin = Math.Max(1, n);
            }
            RenderIfIdle();
        }

        // 旋鈕轉動：這一格的桿子上下移動（順時針往下）
        public void Rotate(int column, int ticks, double step)
        {
            lock (sync)
            {
                if (state == GameState.GameOver)
                    return;
                foreach (Rod rod in rods)
                {
                    if (rod.Column != column) continue;
                    rod.Offset = Math.Max(-rod.MaxOffset, Math.Min(rod.MaxOffset, rod.Offset + ticks * step));
                }
            }
            RenderIfIdle();
        }

        // 旋鈕按下：比賽中是踢球（按住不放就維持抬腳），其他狀態是開始 / 繼續
        public void Press(int column)
        {
            lock (sync)
            {
                switch (state)
                {
                    case GameState.Idle:
                    case GameState.GameOver:
                        StartMatch();
                        break;
                    case GameState.Paused:
                        state = GameState.Playing;
                        StartTimer();
                        break;
                    case GameState.Playing:
                        foreach (Rod rod in rods)
                        {
                            if (rod.Column != column) continue;
                            rod.Held = true;
                            if (rod.Kick > 0) continue;
                            rod.Kick = KICK_FRAMES;
                            rod.KickHit = false;
                        }
                        break;
                }
            }
            RenderIfIdle();
        }

        // 旋鈕放開：抬起的球員放下來
        public void DialReleased(int column)
        {
            lock (sync)
            {
                foreach (Rod rod in rods)
                    if (rod.Column == column) rod.Held = false;
            }
            RenderIfIdle();
        }

        // 觸控條點一下：暫停 / 繼續
        public void TogglePause()
        {
            lock (sync)
            {
                if (state == GameState.Playing)
                {
                    state = GameState.Paused;
                    StopTimer();
                }
                else if (state == GameState.Paused)
                {
                    state = GameState.Playing;
                    StartTimer();
                }
                else if (state == GameState.Idle || state == GameState.GameOver)
                {
                    StartMatch();
                }
            }
            RenderIfIdle();
        }

        // 觸控條長按：回到開始畫面
        public void Reset()
        {
            lock (sync)
            {
                StopTimer();
                state = GameState.Idle;
                score[0] = score[1] = 0;
                foreach (Rod rod in rods) { rod.Offset = 0; rod.Kick = 0; rod.Held = false; }
            }
            RenderIfIdle();
        }

        /// <summary>給放在 1~4 以外位置的 action 用的提示畫面</summary>
        public static string RenderMessage(string line1, string line2)
        {
            using (var bmp = new Bitmap(SLOT_W, H, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var big = new Font("Arial", 14, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.Clear(Color.Black);
                g.DrawString(line1, big, Brushes.White, new RectangleF(0, 20, SLOT_W, 30), center);
                g.DrawString(line2, small, Brushes.Gray, new RectangleF(0, 50, SLOT_W, 30), center);
                return Tools.ImageToBase64(bmp, true);
            }
        }

        #endregion

        #region 比賽流程

        private void StartMatch()
        {
            score[0] = score[1] = 0;
            foreach (Rod rod in rods) { rod.Offset = 0; rod.Kick = 0; rod.Held = false; }
            Serve();
            StartTimer();
            Logger.Instance.LogMessage(TracingLevel.INFO, $"foosball, match start device={DeviceId} goalsToWin={goalsToWin}");
        }

        // 從中線發球，方向隨機、慢慢滾向其中一隊的中場
        private void Serve()
        {
            bx = W / 2.0;
            by = FIELD_TOP + FIELD_H / 2.0;
            vx = (rng.Next(2) == 0 ? -1 : 1) * 1.8;
            vy = (rng.NextDouble() - 0.5) * 1.6;
            state = GameState.Playing;
        }

        private void ScoreGoal(int team)
        {
            score[team]++;
            lastScorer = team;
            state = GameState.Goal;
            goalPause = GOAL_PAUSE_FRAMES;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"foosball, goal team={team} score={score[0]}:{score[1]}");
        }

        private void StartTimer()
        {
            if (disposed || frameTimer == null) return;
            timerRunning = true;
            frameTimer.Change(0, FRAME_MS);
        }

        private void StopTimer()
        {
            timerRunning = false;
            frameTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnFrame(object _)
        {
            if (Interlocked.Exchange(ref inFrame, 1) == 1)
                return;

            try
            {
                lock (sync)
                {
                    if (state == GameState.Playing || state == GameState.Goal)
                        Step();
                }
                RenderAndSend();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"foosball, OnFrame error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref inFrame, 0);
            }
        }

        // 一幀：踢球動畫、進球暫停、球的移動與碰撞
        private void Step()
        {
            foreach (Rod rod in rods)
            {
                if (rod.Kick <= 0) continue;
                if (state == GameState.Playing && !rod.KickHit && rod.Kick > KICK_FRAMES - KICK_ACTIVE)
                    TryKick(rod);
                rod.Kick--;
            }

            if (state == GameState.Goal)
            {
                if (--goalPause <= 0)
                {
                    if (score[lastScorer] >= goalsToWin)
                    {
                        state = GameState.GameOver;
                        StopTimer();
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"foosball, match over winner={lastScorer} score={score[0]}:{score[1]}");
                    }
                    else
                    {
                        Serve();
                    }
                }
                return;
            }

            // 分幾小步走，快的球才不會直接穿過 6px 寬的球員
            double speed = Math.Max(Math.Abs(vx), Math.Abs(vy));
            int n = Math.Max(1, (int)Math.Ceiling(speed / 2));
            for (int i = 0; i < n; i++)
            {
                double px = bx, py = by;
                bx += vx / n;
                by += vy / n;
                if (Walls())
                    return;
                Players(px);
            }

            vx *= FRICTION;
            vy *= FRICTION;
            if (Math.Abs(vx) < MIN_VX)
                vx = (vx == 0 ? (rng.Next(2) == 0 ? -1 : 1) : Math.Sign(vx)) * MIN_VX;
            vx = Math.Max(-MAX_VX, Math.Min(MAX_VX, vx));
            vy = Math.Max(-MAX_VY, Math.Min(MAX_VY, vy));
        }

        private void TryKick(Rod rod)
        {
            // ahead > 0 是腳前，< 0 是背後；貼著身體的球算腳前
            double ahead = (bx - rod.X) * rod.Dir;
            if (Math.Abs(ahead) > KICK_REACH)
                return;
            bool forward = ahead >= -(PW / 2.0 + BALL_R);

            for (int i = 0; i < rod.Players; i++)
            {
                double d = by - rod.PlayerY(i);
                if (Math.Abs(d) > PH / 2.0 + BALL_R + 1)
                    continue;

                // 踢到球員上緣球往上偏、下緣往下偏，加一點亂數
                double spread = d / (PH / 2.0 + BALL_R);
                if (forward)
                {
                    vx = rod.Dir * KICK_SPEED;
                    vy = spread * 3.2 + (rng.NextDouble() - 0.5) * 0.6;
                    bx = rod.X + rod.Dir * (PW / 2.0 + BALL_R + 1);
                }
                else
                {
                    // 背後的球：桿子往後轉把球撥回去，力道比較弱
                    vx = -rod.Dir * BACK_KICK_SPEED;
                    vy = spread * 2.0 + (rng.NextDouble() - 0.5) * 0.6;
                    bx = rod.X - rod.Dir * (PW / 2.0 + BALL_R + 1);
                }
                rod.KickHit = true;
                return;
            }
        }

        // 回傳 true 代表進球
        private bool Walls()
        {
            if (by - BALL_R < FIELD_TOP) { by = FIELD_TOP + BALL_R; vy = Math.Abs(vy); }
            else if (by + BALL_R > H) { by = H - BALL_R; vy = -Math.Abs(vy); }

            bool inMouth = by >= GOAL_TOP && by <= GOAL_BOTTOM;

            if (bx - BALL_R < 0)
            {
                if (inMouth)
                {
                    if (bx < 0) { ScoreGoal(TEAM_BLUE); return true; }
                }
                else
                {
                    bx = BALL_R;
                    vx = Math.Abs(vx) * 0.9;
                    vy += (rng.NextDouble() - 0.5) * BOUNCE_JITTER;
                }
            }
            else if (bx + BALL_R > W)
            {
                if (inMouth)
                {
                    if (bx > W) { ScoreGoal(TEAM_RED); return true; }
                }
                else
                {
                    bx = W - BALL_R;
                    vx = -Math.Abs(vx) * 0.9;
                    vy += (rng.NextDouble() - 0.5) * BOUNCE_JITTER;
                }
            }
            return false;
        }

        // 球員碰撞。正面背面都會擋，自己人也一樣；只有抬腳中的桿子讓球過去
        private void Players(double px)
        {
            foreach (Rod rod in rods)
            {
                if (rod.Lifted) continue;

                double left = rod.X - PW / 2.0 - BALL_R;
                double right = rod.X + PW / 2.0 + BALL_R;
                if (bx < left || bx > right) continue;

                for (int i = 0; i < rod.Players; i++)
                {
                    double cy = rod.PlayerY(i);
                    double top = cy - PH / 2.0 - BALL_R;
                    double bottom = cy + PH / 2.0 + BALL_R;
                    if (by < top || by > bottom) continue;

                    if (px < left || px > right)
                    {
                        // 從左或右撞到：往來的方向彈回去
                        int side = px < left ? -1 : 1;
                        vx = side * Math.Max(Math.Abs(vx) * 0.7, MIN_VX);
                        bx = side > 0 ? right + 0.1 : left - 0.1;
                        vy += (rng.NextDouble() - 0.5) * BOUNCE_JITTER;
                    }
                    else
                    {
                        // 上下撞到，或是球員移過來壓到球
                        double ny = by < cy ? top - 0.1 : bottom + 0.1;
                        if (ny - BALL_R >= FIELD_TOP && ny + BALL_R <= H)
                        {
                            by = ny;
                            vy = by < cy ? -Math.Abs(vy) : Math.Abs(vy);
                        }
                        else
                        {
                            // 球被壓在邊線上、上下沒空間：從比較近的那一側擠出去
                            int side = bx >= rod.X ? 1 : -1;
                            bx = side > 0 ? right + 0.1 : left - 0.1;
                            vx = side * Math.Max(Math.Abs(vx), MIN_VX);
                        }
                    }
                    return;
                }
            }
        }

        #endregion

        #region 繪圖

        private void RenderIfIdle()
        {
            // 計時器在跑的時候由它負責畫
            if (!timerRunning)
                RenderAndSend();
        }

        public void RenderAndSend()
        {
            var outgoing = new List<KeyValuePair<Action<string>, string>>();
            try
            {
                lock (sync)
                {
                    if (disposed) return;
                    using (Bitmap bmp = Draw())
                    {
                        for (int col = 0; col < SLOTS; col++)
                        {
                            if (slots[col] == null) continue;
                            using (Bitmap crop = bmp.Clone(new Rectangle(col * SLOT_W, 0, SLOT_W, H), bmp.PixelFormat))
                            {
                                string b64 = Tools.ImageToBase64(crop, true);
                                // 沒變的格子就不送，球只在一兩格裡跑，可以省掉大半流量
                                if (b64 == lastSent[col]) continue;
                                lastSent[col] = b64;
                                outgoing.Add(new KeyValuePair<Action<string>, string>(slots[col], b64));
                            }
                        }
                    }
                }

                foreach (var kv in outgoing)
                    kv.Key(kv.Value);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"foosball, Render error: {ex.Message}");
            }
        }

        private int AttachedCount()
        {
            int n = 0;
            foreach (var s in slots) if (s != null) n++;
            return n;
        }

        private Bitmap Draw()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var hudFont = new Font("Arial", 8, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bigFont = new Font("Arial", 18, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var topCenter = new StringFormat { Alignment = StringAlignment.Center })
            using (var topRight = new StringFormat { Alignment = StringAlignment.Far })
            using (var fieldBrush = new SolidBrush(FieldColor))
            using (var linePen = new Pen(Color.FromArgb(110, 255, 255, 255)))
            using (var rodPen = new Pen(Color.FromArgb(150, 170, 170, 170)))
            using (var redBrush = new SolidBrush(RedColor))
            using (var blueBrush = new SolidBrush(BlueColor))
            using (var redFoot = new SolidBrush(Color.FromArgb(0xFF, 0xA0, 0xA0)))
            using (var blueFoot = new SolidBrush(Color.FromArgb(0xA0, 0xC4, 0xFF)))
            using (var headBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            using (var dim = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.Clear(Color.Black);

                // 球場
                g.FillRectangle(fieldBrush, 0, FIELD_TOP, W, FIELD_H);
                int midY = FIELD_TOP + FIELD_H / 2;
                g.DrawLine(linePen, W / 2, FIELD_TOP, W / 2, H);
                g.DrawEllipse(linePen, W / 2 - 14, midY - 14, 28, 28);
                g.DrawRectangle(linePen, 0, GOAL_TOP - 8, 22, GOAL_H + 16);
                g.DrawRectangle(linePen, W - 23, GOAL_TOP - 8, 22, GOAL_H + 16);
                g.FillRectangle(Brushes.White, 0, GOAL_TOP, 3, GOAL_H);
                g.FillRectangle(Brushes.White, W - 3, GOAL_TOP, 3, GOAL_H);

                // 桿子與球員
                foreach (Rod rod in rods)
                {
                    g.DrawLine(rodPen, rod.X, FIELD_TOP, rod.X, H);
                    Brush body = rod.Team == TEAM_RED ? redBrush : blueBrush;
                    Brush foot = rod.Team == TEAM_RED ? redFoot : blueFoot;

                    for (int i = 0; i < rod.Players; i++)
                    {
                        int cy = (int)Math.Round(rod.PlayerY(i));
                        if (rod.Lifted)
                        {
                            // 抬起來：從上面看球員躺平、腳朝前，顏色淡一點表示離地
                            int len = 14;
                            int lx = rod.Dir > 0 ? rod.X - PW / 2 : rod.X + PW / 2 - len;
                            g.FillRectangle(foot, lx, cy - 3, len, 6);
                            int hx = rod.Dir > 0 ? rod.X - PW / 2 : rod.X + PW / 2 - 4;
                            g.FillRectangle(headBrush, hx, cy - 2, 4, 4);
                            continue;
                        }
                        if (rod.Kick > 0)
                        {
                            int reach = KickReach[KICK_FRAMES - rod.Kick];
                            int fx = rod.Dir > 0 ? rod.X + PW / 2 : rod.X - PW / 2 - reach;
                            g.FillRectangle(foot, fx, cy - 2, reach, 4);
                        }
                        g.FillRectangle(body, rod.X - PW / 2, cy - PH / 2, PW, PH);
                        g.FillRectangle(headBrush, rod.X - 2, cy - 2, 4, 4);
                    }
                }

                // 球
                if (state == GameState.Playing || state == GameState.Paused)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(Brushes.White, (float)(bx - BALL_R), (float)(by - BALL_R), (float)(BALL_R * 2), (float)(BALL_R * 2));
                    g.SmoothingMode = SmoothingMode.None;
                }

                // 記分列
                g.DrawString($"RED {score[TEAM_RED]}", hudFont, redBrush, 2, 0);
                g.DrawString($"{score[TEAM_BLUE]} BLUE", hudFont, blueBrush, new RectangleF(0, 0, W - 2, FIELD_TOP), topRight);
                g.DrawString($"FIRST TO {goalsToWin}", hudFont, Brushes.Gray, new RectangleF(0, 0, W, FIELD_TOP), topCenter);
                // 標示每格下面那顆旋鈕是哪一隊的（第 1、4 格旁邊已經有比分，不用標）
                for (int col = 0; col < SLOTS; col++)
                {
                    if (col == 0 || col == SLOTS - 1) continue;
                    Brush b = TeamOfColumn(col) == TEAM_RED ? redBrush : blueBrush;
                    g.FillRectangle(b, col * SLOT_W + SLOT_W / 2 - 15, 3, 30, 2);
                }

                // 中間的訊息框橫跨第 2、3 格
                var box = new RectangleF(W / 2 - 170, 22, 340, 66);
                var line1 = new RectangleF(box.X, box.Y + 2, box.Width, 26);
                var line2 = new RectangleF(box.X, box.Y + 28, box.Width, 16);
                var line3 = new RectangleF(box.X, box.Y + 46, box.Width, 16);
                switch (state)
                {
                    case GameState.Idle:
                        g.FillRectangle(dim, box);
                        g.DrawString("FOOSBALL", bigFont, Brushes.White, line1, center);
                        int n = AttachedCount();
                        g.DrawString(n < SLOTS ? $"put Foosball on all 4 dials ({n}/4)" : "press any dial to start",
                            smallFont, n < SLOTS ? Brushes.Orange : Brushes.LightGray, line2, center);
                        g.DrawString("RED: dial 1 + 3     BLUE: dial 2 + 4", smallFont, Brushes.Gray, line3, center);
                        break;
                    case GameState.Paused:
                        g.FillRectangle(dim, box);
                        g.DrawString("PAUSED", bigFont, Brushes.White, line1, center);
                        g.DrawString("tap the strip to resume", smallFont, Brushes.Gray, line2, center);
                        break;
                    case GameState.Goal:
                        g.FillRectangle(dim, box);
                        g.DrawString("GOAL!", bigFont, lastScorer == TEAM_RED ? redBrush : blueBrush, line1, center);
                        g.DrawString($"{score[TEAM_RED]} : {score[TEAM_BLUE]}", smallFont, Brushes.White, line2, center);
                        break;
                    case GameState.GameOver:
                        g.FillRectangle(dim, box);
                        g.DrawString(lastScorer == TEAM_RED ? "RED WINS" : "BLUE WINS", bigFont,
                            lastScorer == TEAM_RED ? redBrush : blueBrush, line1, center);
                        g.DrawString($"{score[TEAM_RED]} : {score[TEAM_BLUE]}", smallFont, Brushes.White, line2, center);
                        g.DrawString("press any dial for a rematch", smallFont, Brushes.Gray, line3, center);
                        break;
                }
            }
            return bmp;
        }

        #endregion
    }
}
