using BarRaider.SdTools;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// 在 Stream Deck+ 觸控條（每顆旋鈕 200x100）上玩的貪食蛇。
    /// 旋鈕順時針一格 = 右轉 90 度，逆時針一格 = 左轉 90 度（相對蛇頭方向）。
    /// 旋鈕按下 / 觸控 = 開始、暫停，觸控長按 = 重新開始。
    /// </summary>
    [PluginActionId("com.odiestudio.minigame.snake")]
    public class SnakeAction : KeyAndEncoderBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    speed = "normal",
                    wrap = "false",
                    best = "0",
                };
            }

            [JsonProperty(PropertyName = "speed")]
            public string speed { get; set; }

            // "true" = 穿牆從另一邊出來，"false" = 撞牆就死
            [JsonProperty(PropertyName = "wrap")]
            public string wrap { get; set; }

            // 最高分，存在按鍵設定裡，重開 Stream Deck 也還在
            [JsonProperty(PropertyName = "best")]
            public string best { get; set; }
        }

        private enum GameState { Idle, Playing, Paused, GameOver }

        #region 常數

        private const int W = 200;
        private const int H = 100;
        private const int HUD_H = 10;
        private const int CELL = 5;
        private const int COLS = W / CELL;                 // 40
        private const int ROWS = (H - HUD_H) / CELL;       // 18
        private const int START_LEN = 4;
        private const int MIN_STEP_MS = 60;
        private const int STEP_SPEEDUP_MS = 3;             // 每吃一個縮短的毫秒數
        private const int MAX_PENDING_TURNS = 3;           // 轉太快時最多先記住幾次轉向

        private static readonly Color HeadColor = Color.FromArgb(0xB8, 0xFF, 0x7A);
        private static readonly Color BodyColor = Color.FromArgb(0x5A, 0xD6, 0x5A);
        private static readonly Color FoodColor = Color.FromArgb(0xFF, 0x4C, 0x4C);
        private static readonly Color LineColor = Color.FromArgb(0x30, 0x30, 0x30);

        #endregion

        #region 遊戲狀態

        private readonly object sync = new object();
        private readonly PluginSettings settings;
        private readonly Random rng = new Random();

        // 蛇身：First 是頭，Last 是尾；occupied 用來 O(1) 判斷撞到自己
        private readonly LinkedList<Point> snake = new LinkedList<Point>();
        private readonly HashSet<Point> occupied = new HashSet<Point>();
        private readonly Queue<int> pendingTurns = new Queue<int>();

        private GameState state = GameState.Idle;
        private int dx = 1, dy = 0;
        private Point food;
        private int score;
        private int best;
        private int stepMs;
        private bool won;

        private Timer stepTimer;
        private int inStep;           // 防止 timer callback 重入
        private bool disposed;

        #endregion

        public SnakeAction(ISDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "snake, SnakeAction");

            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
            }

            int.TryParse(settings.best, out best);
            ResetSnake();   // Idle 畫面先擺一條蛇當預覽
            stepTimer = new Timer(OnStep, null, Timeout.Infinite, Timeout.Infinite);
            Render();
        }

        public override void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                stepTimer?.Dispose();
                stepTimer = null;
            }
            Logger.Instance.LogMessage(TracingLevel.INFO, "snake, Destructor called");
        }

        #region Stream Deck 事件

        public override void DialRotate(DialRotatePayload payload)
        {
            lock (sync)
            {
                if (state != GameState.Playing)
                    return;

                // 一格轉一次；一次轉很多格就排隊，每一步只吃一個轉向，避免直接 180 度咬到自己
                int turn = payload.Ticks > 0 ? 1 : -1;
                for (int i = 0; i < Math.Abs(payload.Ticks) && pendingTurns.Count < MAX_PENDING_TURNS; i++)
                    pendingTurns.Enqueue(turn);
            }
        }

        public override void DialDown(DialPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "snake, DialDown");
            Pressed();
        }

        public override void DialUp(DialPayload payload) { }

        public override void TouchPress(TouchpadPressPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"snake, TouchPress longPress={payload.IsLongPress}");
            if (payload.IsLongPress)
            {
                lock (sync) { NewGame(); }
                Render();
            }
            else
            {
                Pressed();
            }
        }

        public override void KeyPressed(KeyPayload payload)
        {
            Pressed();
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override void OnTick() { }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "snake, ReceivedSettings");
            lock (sync)
            {
                // PI 送來的設定不含最高分，保留 plugin 這邊的
                Tools.AutoPopulateSettings(settings, payload.Settings);
                int.TryParse(settings.best, out int fromPi);
                best = Math.Max(best, fromPi);
                settings.best = best.ToString();
            }
            Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        #endregion

        #region 遊戲流程

        private int BaseStepMs()
        {
            switch (settings.speed)
            {
                case "slow": return 200;
                case "fast": return 100;
                default: return 150;
            }
        }

        private bool Wrap()
        {
            return string.Equals(settings.wrap, "true", StringComparison.OrdinalIgnoreCase);
        }

        // 旋鈕按下 / 觸控點一下
        private void Pressed()
        {
            lock (sync)
            {
                switch (state)
                {
                    case GameState.Idle:
                    case GameState.GameOver:
                        NewGame();
                        break;
                    case GameState.Playing:
                        state = GameState.Paused;
                        StopTimer();
                        break;
                    case GameState.Paused:
                        state = GameState.Playing;
                        StartTimer();
                        break;
                }
            }
            Render();
        }

        private void ResetSnake()
        {
            snake.Clear();
            occupied.Clear();
            pendingTurns.Clear();
            dx = 1;
            dy = 0;

            int y = ROWS / 2;
            int headX = COLS / 2;
            for (int i = 0; i < START_LEN; i++)
            {
                var p = new Point(headX - i, y);
                snake.AddLast(p);
                occupied.Add(p);
            }
            PlaceFood();
        }

        private void NewGame()
        {
            StopTimer();
            ResetSnake();
            score = 0;
            won = false;
            stepMs = BaseStepMs();
            state = GameState.Playing;
            StartTimer();
        }

        // 回傳 false 代表格子滿了（贏了）
        private bool PlaceFood()
        {
            int free = COLS * ROWS - occupied.Count;
            if (free <= 0)
                return false;

            // 隨機挑第 n 個空格，避免蛇很長時一直亂數撞到身體
            int n = rng.Next(free);
            for (int y = 0; y < ROWS; y++)
            {
                for (int x = 0; x < COLS; x++)
                {
                    var p = new Point(x, y);
                    if (occupied.Contains(p)) continue;
                    if (n-- == 0)
                    {
                        food = p;
                        return true;
                    }
                }
            }
            return false;
        }

        private void StartTimer()
        {
            if (!disposed)
                stepTimer?.Change(stepMs, stepMs);
        }

        private void StopTimer()
        {
            stepTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnStep(object _)
        {
            if (Interlocked.Exchange(ref inStep, 1) == 1)
                return;

            try
            {
                lock (sync)
                {
                    if (state == GameState.Playing)
                        Step();
                }
                Render();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"snake, OnStep error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref inStep, 0);
            }
        }

        // 前進一格：轉向、撞牆、撞自己、吃東西
        private void Step()
        {
            if (pendingTurns.Count > 0)
            {
                int turn = pendingTurns.Dequeue();
                int ndx, ndy;
                if (turn > 0) { ndx = -dy; ndy = dx; }   // 螢幕座標 y 朝下：順時針 = 右轉
                else { ndx = dy; ndy = -dx; }           // 逆時針 = 左轉
                dx = ndx;
                dy = ndy;
            }

            Point head = snake.First.Value;
            int nx = head.X + dx;
            int ny = head.Y + dy;

            if (Wrap())
            {
                nx = (nx + COLS) % COLS;
                ny = (ny + ROWS) % ROWS;
            }
            else if (nx < 0 || nx >= COLS || ny < 0 || ny >= ROWS)
            {
                GameOver();
                return;
            }

            var next = new Point(nx, ny);
            bool grow = next == food;

            // 沒吃到東西時尾巴會先移走，所以頭可以走進原本尾巴的位置
            if (!grow)
            {
                occupied.Remove(snake.Last.Value);
                snake.RemoveLast();
            }

            if (occupied.Contains(next))
            {
                GameOver();
                return;
            }

            snake.AddFirst(next);
            occupied.Add(next);

            if (grow)
            {
                score++;
                if (!PlaceFood())
                {
                    won = true;
                    GameOver();
                    return;
                }

                int newStep = Math.Max(MIN_STEP_MS, stepMs - STEP_SPEEDUP_MS);
                if (newStep != stepMs)
                {
                    stepMs = newStep;
                    StartTimer();
                }
            }
        }

        private void GameOver()
        {
            state = GameState.GameOver;
            StopTimer();
            Logger.Instance.LogMessage(TracingLevel.INFO, $"snake, game over score={score} len={snake.Count} won={won}");

            if (score > best)
            {
                best = score;
                settings.best = best.ToString();
                Connection?.SetSettingsAsync(JObject.FromObject(settings));
            }
        }

        #endregion

        #region 繪圖

        private void Render()
        {
            try
            {
                string b64;
                lock (sync)
                {
                    if (disposed) return;
                    using (Bitmap bmp = Draw())
                    {
                        b64 = Tools.ImageToBase64(bmp, true);
                    }
                }

                Connection.SetFeedbackAsync(new Dictionary<string, string> { ["canvas"] = b64 });
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"snake, Render error: {ex.Message}");
            }
        }

        private static Rectangle CellRect(Point p)
        {
            // 每格 5x5，畫 4x4 留 1px 縫，看得出一節一節
            return new Rectangle(p.X * CELL, HUD_H + p.Y * CELL, CELL - 1, CELL - 1);
        }

        private Bitmap Draw()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var hudFont = new Font("Arial", 8, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bigFont = new Font("Arial", 16, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var right = new StringFormat { Alignment = StringAlignment.Far })
            using (var headBrush = new SolidBrush(HeadColor))
            using (var bodyBrush = new SolidBrush(BodyColor))
            using (var foodBrush = new SolidBrush(FoodColor))
            using (var linePen = new Pen(LineColor))
            using (var dim = new SolidBrush(Color.FromArgb(0xB0, 0, 0, 0)))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.Clear(Color.Black);

                // HUD 跟遊戲區的分隔線；穿牆模式用虛線提示上緣可以穿
                if (Wrap())
                    linePen.DashPattern = new float[] { 2, 2 };
                g.DrawLine(linePen, 0, HUD_H - 1, W, HUD_H - 1);

                // 食物
                if (state != GameState.Idle)
                    g.FillRectangle(foodBrush, CellRect(food));

                // 蛇（Idle 時不畫，免得壓到標題文字）
                if (state != GameState.Idle)
                {
                    bool first = true;
                    foreach (Point p in snake)
                    {
                        g.FillRectangle(first ? headBrush : bodyBrush, CellRect(p));
                        first = false;
                    }
                }

                // HUD：分數、最高分
                g.DrawString(score.ToString(), hudFont, Brushes.White, 1, 0);
                g.DrawString($"BEST {best}", hudFont, Brushes.Gray, new RectangleF(0, 0, W - 1, HUD_H), right);

                // 狀態文字：先壓暗背景再寫字，蛇在底下也看得清楚
                var area = new RectangleF(0, HUD_H, W, H - HUD_H);
                var upper = new RectangleF(0, HUD_H + 15, W, 30);
                var lower = new RectangleF(0, HUD_H + 45, W, 20);
                switch (state)
                {
                    case GameState.Idle:
                        g.FillRectangle(dim, area);
                        g.DrawString("SNAKE", bigFont, Brushes.White, upper, center);
                        g.DrawString("press dial to start", smallFont, Brushes.Gray, lower, center);
                        break;
                    case GameState.Paused:
                        g.FillRectangle(dim, area);
                        g.DrawString("PAUSED", bigFont, Brushes.White, area, center);
                        break;
                    case GameState.GameOver:
                        g.FillRectangle(dim, area);
                        g.DrawString(won ? "YOU WIN" : "GAME OVER", bigFont, won ? Brushes.Gold : Brushes.OrangeRed, upper, center);
                        g.DrawString($"score {score}   press to retry", smallFont, Brushes.Gray, lower, center);
                        break;
                }
            }
            return bmp;
        }

        #endregion
    }
}
