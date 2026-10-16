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
    /// 在 Stream Deck+ 觸控條（每顆旋鈕 200x100）上玩的打磚塊。
    /// 旋鈕轉動 = 移動球拍，旋鈕按下 / 觸控 = 開始、發球、暫停，觸控長按 = 重新開始。
    /// 畫面用 GDI+ 畫在 200x100 的 Bitmap 上，轉成 base64 丟給 layout 裡唯一的 pixmap。
    /// </summary>
    [PluginActionId("com.odiestudio.minigame.breakout")]
    public class BreakoutAction : KeyAndEncoderBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    paddleStep = "8",
                    ballSpeed = "normal",
                };
            }

            [JsonProperty(PropertyName = "paddleStep")]
            public string paddleStep { get; set; }

            [JsonProperty(PropertyName = "ballSpeed")]
            public string ballSpeed { get; set; }
        }

        private enum GameState { Idle, Ready, Playing, Paused, GameOver }

        #region 常數

        private const int W = 200;
        private const int H = 100;
        private const int HUD_H = 10;                 // 最上面一條留給分數 / 關卡 / 命
        private const int BRICK_ROWS = 4;
        private const int BRICK_COLS = 10;
        private const int BRICK_W = 19;
        private const int BRICK_H = 6;
        private const int BRICK_GAP = 1;
        private const int BRICK_TOP = HUD_H + 1;
        private const int PADDLE_W = 32;
        private const int PADDLE_H = 3;
        private const int PADDLE_Y = H - 5;
        private const int BALL_R = 2;
        private const int START_LIVES = 3;
        private const int FRAME_MS = 40;              // 25 fps
        private const double LEVEL_SPEEDUP = 1.15;    // 每過一關球速乘上這個

        private static readonly Color[] RowColors =
        {
            Color.FromArgb(0xFF, 0x4C, 0x4C),
            Color.FromArgb(0xFF, 0xA5, 0x2C),
            Color.FromArgb(0xFF, 0xE1, 0x3A),
            Color.FromArgb(0x5A, 0xD6, 0x5A),
        };

        #endregion

        #region 遊戲狀態

        private readonly object sync = new object();
        private readonly PluginSettings settings;
        private readonly bool[,] bricks = new bool[BRICK_ROWS, BRICK_COLS];
        private readonly Random rng = new Random();

        private GameState state = GameState.Idle;
        private double paddleX = (W - PADDLE_W) / 2.0;
        private double ballX, ballY, ballVx, ballVy;
        private double speed;
        private int bricksLeft;
        private int score;
        private int lives;
        private int level = 1;

        private Timer frameTimer;
        private int inFrame;          // 防止 timer callback 重入
        private bool disposed;

        #endregion

        public BreakoutAction(ISDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "breakout, BreakoutAction");

            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings));
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
            }

            speed = BaseSpeed();
            ResetBricks();   // Idle 畫面先把磚塊排好當預覽
            frameTimer = new Timer(OnFrame, null, Timeout.Infinite, Timeout.Infinite);
            Render();
        }

        public override void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                frameTimer?.Dispose();
                frameTimer = null;
            }
            Logger.Instance.LogMessage(TracingLevel.INFO, "breakout, Destructor called");
        }

        #region Stream Deck 事件

        public override void DialRotate(DialRotatePayload payload)
        {
            bool needRender;
            lock (sync)
            {
                if (state == GameState.GameOver)
                    return;

                paddleX += payload.Ticks * PaddleStep();
                paddleX = Math.Max(0, Math.Min(W - PADDLE_W, paddleX));

                // 球還黏在球拍上就跟著走
                if (state == GameState.Ready)
                    ballX = paddleX + PADDLE_W / 2.0;

                // Playing 時由 timer 負責畫
                needRender = state != GameState.Playing;
            }

            if (needRender)
                Render();
        }

        public override void DialDown(DialPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "breakout, DialDown");
            Pressed();
        }

        public override void DialUp(DialPayload payload) { }

        public override void TouchPress(TouchpadPressPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"breakout, TouchPress longPress={payload.IsLongPress}");
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
            Logger.Instance.LogMessage(TracingLevel.INFO, "breakout, ReceivedSettings");
            Tools.AutoPopulateSettings(settings, payload.Settings);
            Connection.SetSettingsAsync(JObject.FromObject(settings));

            lock (sync)
            {
                // 進行中不改速度，避免球突然變快；其他狀態直接套用
                if (state != GameState.Playing)
                    speed = BaseSpeed() * Math.Pow(LEVEL_SPEEDUP, level - 1);
            }
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        #endregion

        #region 遊戲流程

        private double PaddleStep()
        {
            return double.TryParse(settings.paddleStep, out double v) && v > 0 ? v : 8;
        }

        private double BaseSpeed()
        {
            switch (settings.ballSpeed)
            {
                case "slow": return 1.5;
                case "fast": return 2.6;
                default: return 2.0;
            }
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
                    case GameState.Ready:
                        Launch();
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

        private void NewGame()
        {
            StopTimer();
            score = 0;
            lives = START_LIVES;
            level = 1;
            speed = BaseSpeed();
            paddleX = (W - PADDLE_W) / 2.0;
            ResetBricks();
            AttachBall();
            state = GameState.Ready;
        }

        private void ResetBricks()
        {
            for (int r = 0; r < BRICK_ROWS; r++)
                for (int c = 0; c < BRICK_COLS; c++)
                    bricks[r, c] = true;
            bricksLeft = BRICK_ROWS * BRICK_COLS;
        }

        private void AttachBall()
        {
            ballX = paddleX + PADDLE_W / 2.0;
            ballY = PADDLE_Y - BALL_R - 1;
            ballVx = 0;
            ballVy = 0;
        }

        private void Launch()
        {
            // 往上隨機偏個角度，±35 度以內
            double angle = (rng.NextDouble() - 0.5) * 1.2;
            ballVx = speed * Math.Sin(angle);
            ballVy = -speed * Math.Cos(angle);
            state = GameState.Playing;
            StartTimer();
        }

        private void LoseLife()
        {
            lives--;
            if (lives <= 0)
            {
                state = GameState.GameOver;
                Logger.Instance.LogMessage(TracingLevel.INFO, $"breakout, game over score={score} level={level}");
            }
            else
            {
                AttachBall();
                state = GameState.Ready;
            }
            StopTimer();
        }

        private void NextLevel()
        {
            level++;
            speed *= LEVEL_SPEEDUP;
            ResetBricks();
            AttachBall();
            state = GameState.Ready;
            StopTimer();
        }

        private void StartTimer()
        {
            if (!disposed)
                frameTimer?.Change(0, FRAME_MS);
        }

        private void StopTimer()
        {
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
                    if (state == GameState.Playing)
                        Step();
                }
                Render();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"breakout, OnFrame error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref inFrame, 0);
            }
        }

        // 一幀的物理：牆、球拍、磚塊、掉出底部
        private void Step()
        {
            double nx = ballX + ballVx;
            double ny = ballY + ballVy;

            // 左右牆
            if (nx - BALL_R < 0) { nx = BALL_R; ballVx = -ballVx; }
            else if (nx + BALL_R > W) { nx = W - BALL_R; ballVx = -ballVx; }

            // 上緣（HUD 下面）
            if (ny - BALL_R < HUD_H) { ny = HUD_H + BALL_R; ballVy = -ballVy; }

            // 球拍：只在往下掉、且這一幀剛好穿過球拍上緣時反彈
            if (ballVy > 0
                && ny + BALL_R >= PADDLE_Y
                && ballY + BALL_R <= PADDLE_Y + PADDLE_H
                && nx >= paddleX - BALL_R
                && nx <= paddleX + PADDLE_W + BALL_R)
            {
                ny = PADDLE_Y - BALL_R;
                // 打到球拍哪個位置決定反彈角度，邊緣最多約 60 度
                double offset = (nx - (paddleX + PADDLE_W / 2.0)) / (PADDLE_W / 2.0);
                offset = Math.Max(-1, Math.Min(1, offset));
                // 正中接球也給一點角度，不然球會垂直上下穿同一條縫
                if (Math.Abs(offset) < 0.12)
                    offset = (ballVx >= 0 ? 1 : -1) * 0.12;
                double angle = offset * 1.05;
                ballVx = speed * Math.Sin(angle);
                ballVy = -speed * Math.Cos(angle);
            }

            // 掉出底部
            if (ny - BALL_R > H)
            {
                LoseLife();
                return;
            }

            // 磚塊：找球框框碰到的第一顆
            int rowMin = RowAt(ny - BALL_R), rowMax = RowAt(ny + BALL_R);
            int colMin = ColAt(nx - BALL_R), colMax = ColAt(nx + BALL_R);
            bool hit = false;
            for (int r = Math.Max(0, rowMin); r <= Math.Min(BRICK_ROWS - 1, rowMax) && !hit; r++)
            {
                for (int c = Math.Max(0, colMin); c <= Math.Min(BRICK_COLS - 1, colMax) && !hit; c++)
                {
                    if (!bricks[r, c]) continue;

                    int bx = c * (BRICK_W + BRICK_GAP);
                    int by = BRICK_TOP + r * (BRICK_H + BRICK_GAP);
                    if (nx + BALL_R <= bx || nx - BALL_R >= bx + BRICK_W ||
                        ny + BALL_R <= by || ny - BALL_R >= by + BRICK_H)
                        continue;

                    bricks[r, c] = false;
                    bricksLeft--;
                    score += (BRICK_ROWS - r) * 10;
                    hit = true;

                    // 上一幀的 x 已經在磚塊範圍內 → 從上下打到；否則是從側面
                    if (ballX >= bx && ballX <= bx + BRICK_W)
                        ballVy = -ballVy;
                    else
                        ballVx = -ballVx;
                }
            }

            ballX = nx;
            ballY = ny;

            if (bricksLeft <= 0)
                NextLevel();
        }

        private static int RowAt(double y)
        {
            return (int)Math.Floor((y - BRICK_TOP) / (BRICK_H + BRICK_GAP));
        }

        private static int ColAt(double x)
        {
            return (int)Math.Floor(x / (BRICK_W + BRICK_GAP));
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
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"breakout, Render error: {ex.Message}");
            }
        }

        private Bitmap Draw()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var hudFont = new Font("Arial", 8, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var bigFont = new Font("Arial", 16, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = new Font("Arial", 10, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var topCenter = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.Clear(Color.Black);

                // 磚塊
                for (int r = 0; r < BRICK_ROWS; r++)
                {
                    using (var brush = new SolidBrush(RowColors[r % RowColors.Length]))
                    {
                        for (int c = 0; c < BRICK_COLS; c++)
                        {
                            if (!bricks[r, c]) continue;
                            g.FillRectangle(brush,
                                c * (BRICK_W + BRICK_GAP),
                                BRICK_TOP + r * (BRICK_H + BRICK_GAP),
                                BRICK_W, BRICK_H);
                        }
                    }
                }

                // 球拍
                g.FillRectangle(Brushes.White, (int)Math.Round(paddleX), PADDLE_Y, PADDLE_W, PADDLE_H);

                // 球
                if (state != GameState.Idle && state != GameState.GameOver)
                {
                    g.FillRectangle(Brushes.White,
                        (int)Math.Round(ballX) - BALL_R, (int)Math.Round(ballY) - BALL_R,
                        BALL_R * 2, BALL_R * 2);
                }

                // HUD：分數、關卡、命
                if (state != GameState.Idle)
                {
                    g.DrawString(score.ToString(), hudFont, Brushes.White, 1, 0);
                    g.DrawString($"L{level}", hudFont, Brushes.Gray, new RectangleF(0, 0, W, HUD_H), topCenter);
                    for (int i = 0; i < lives; i++)
                        g.FillRectangle(Brushes.White, W - 5 - i * 6, 3, 4, 4);
                }

                // 狀態文字蓋在玩區中間
                float playTop = BRICK_TOP + BRICK_ROWS * (BRICK_H + BRICK_GAP);
                var playArea = new RectangleF(0, playTop, W, PADDLE_Y - playTop);
                var upper = new RectangleF(0, playArea.Top, W, playArea.Height * 0.6f);
                var lower = new RectangleF(0, playArea.Top + playArea.Height * 0.55f, W, playArea.Height * 0.45f);
                switch (state)
                {
                    case GameState.Idle:
                        g.DrawString("BREAKOUT", bigFont, Brushes.White, upper, center);
                        g.DrawString("press dial to start", smallFont, Brushes.Gray, lower, center);
                        break;
                    case GameState.Ready:
                        g.DrawString("press to launch", smallFont, Brushes.Gray, playArea, center);
                        break;
                    case GameState.Paused:
                        g.DrawString("PAUSED", bigFont, Brushes.White, playArea, center);
                        break;
                    case GameState.GameOver:
                        g.DrawString("GAME OVER", bigFont, Brushes.OrangeRed, upper, center);
                        g.DrawString($"score {score}   press to retry", smallFont, Brushes.Gray, lower, center);
                        break;
                }
            }
            return bmp;
        }

        #endregion
    }
}
