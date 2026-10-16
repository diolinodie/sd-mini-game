using BarRaider.SdTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace MiniGame
{
    /// <summary>
    /// 單人版復古街機風格賽車，畫在一格觸控條 200x100 上。
    /// 油門自動踩，轉旋鈕 = 方向盤，按住旋鈕 = 煞車。
    /// 五關各有不同景色，每關結尾的檢查點加時間，時間用完就結束，跑完第五關到終點。
    /// 車子物理在 RacerDriver，畫面在 RacerRenderer。
    /// </summary>
    public sealed class RacerGame : IRacerSource
    {
        private enum GameState { Idle, Countdown, Playing, Paused, TimeUp, Goal, GameOver, Finished }

        private const int STAGES = 5;
        private const int STAGE_SEGS = 2100;
        private const int TRACK_SEED = 1986;
        private const int FRAME_MS = 40;                   // 25 fps

        private readonly object sync = new object();
        private readonly Action<string> sink;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly RacerDriver driver = new RacerDriver();
        private readonly RacerRenderer renderer = new RacerRenderer();
        private readonly int column;
        private readonly Color carColor;

        private RacerTrack track;
        private GameState state = GameState.Idle;
        private GameState pausedFrom;

        private double timeLeft;
        private double countdown;
        private double stateTimer;
        private long score;
        private long hiScore;
        private long timeBonus;
        private string difficulty = "normal";

        private Timer frameTimer;
        private bool timerRunning;
        private int inFrame;
        private long lastFrameTicks;
        private bool disposed;

        public Action<long> HiScoreChanged;

        public string DeviceId { get; }

        public RacerGame(string deviceId, int column, Action<string> sink)
        {
            DeviceId = deviceId ?? "";
            this.column = column;
            this.sink = sink;
            carColor = RacerConst.PlayerColors[0];
            BuildTrack();
            ResetRun();
            frameTimer = new Timer(OnFrame, null, Timeout.Infinite, Timeout.Infinite);
            RacerMap.Register(this);
        }

        public void Dispose()
        {
            RacerMap.Unregister(this);
            lock (sync)
            {
                disposed = true;
                frameTimer?.Dispose();
                frameTimer = null;
                renderer.Dispose();
            }
        }

        #region 地圖

        public int MapPriority => 0;

        public bool MapActive
        {
            get { lock (sync) { return state != GameState.Idle; } }
        }

        RacerTrack IRacerSource.CollectMarkers(List<RacerMarker> markers, out string status)
        {
            lock (sync)
            {
                status = null;
                int slot = column >= 0 && column < RacerConst.PlayerColors.Length ? column : 0;
                markers.Add(new RacerMarker
                {
                    Color = RacerConst.PlayerColors[slot],
                    Z = driver.Z,
                    Label = (column + 1).ToString(),
                    Finished = state == GameState.Goal || state == GameState.Finished,
                });
                return track;
            }
        }

        #endregion

        #region 對外操作

        public void Configure(string sensitivity, string steerMode, string diff, long best)
        {
            lock (sync)
            {
                driver.SteerSens = RacerConst.SteerSensitivity(sensitivity);
                driver.AutoCenter = steerMode != "hold";
                if (best > hiScore) hiScore = best;

                diff = diff ?? "normal";
                if (diff != difficulty)
                {
                    difficulty = diff;
                    // 比賽中不換賽道，下一場生效
                    if (state == GameState.Idle)
                    {
                        BuildTrack();
                        ResetRun();
                    }
                }
            }
        }

        public void Rotate(int ticks)
        {
            bool render;
            lock (sync)
            {
                if (state == GameState.Idle || state == GameState.GameOver || state == GameState.Finished || state == GameState.Paused)
                    return;
                driver.Rotate(ticks, Now());
                render = !timerRunning;
            }
            if (render) Render();
        }

        /// <summary>旋鈕按下：開始 / 繼續 / 煞車</summary>
        public void Press()
        {
            lock (sync)
            {
                switch (state)
                {
                    case GameState.Idle:
                    case GameState.GameOver:
                    case GameState.Finished:
                        NewGame();
                        break;
                    case GameState.Paused:
                        Resume();
                        break;
                    default:
                        driver.Braking = true;
                        break;
                }
            }
            Render();
        }

        public void ReleasePress()
        {
            lock (sync) { driver.Braking = false; }
        }

        /// <summary>觸控條點一下：暫停 / 繼續 / 開始</summary>
        public void Tap()
        {
            lock (sync)
            {
                switch (state)
                {
                    case GameState.Idle:
                    case GameState.GameOver:
                    case GameState.Finished:
                        NewGame();
                        break;
                    case GameState.Paused:
                        Resume();
                        break;
                    default:
                        pausedFrom = state;
                        state = GameState.Paused;
                        driver.Braking = false;
                        StopTimer();
                        break;
                }
            }
            Render();
        }

        /// <summary>觸控條長按：回到標題畫面</summary>
        public void Reset()
        {
            lock (sync)
            {
                StopTimer();
                BuildTrack();
                ResetRun();
                state = GameState.Idle;
            }
            Render();
        }

        #endregion

        #region 遊戲流程

        private void Difficulty(out double startTime, out double extend, out int cars)
        {
            switch (difficulty)
            {
                case "easy": startTime = 75; extend = 62; cars = 25; break;
                case "hard": startTime = 52; extend = 46; cars = 60; break;
                default: startTime = 62; extend = 53; cars = 40; break;
            }
        }

        private void BuildTrack()
        {
            Difficulty(out _, out _, out int cars);
            track = RacerTrack.Build(TRACK_SEED, STAGES, STAGE_SEGS, cars, RacerConst.MAX_SPEED);
        }

        private void ResetRun()
        {
            driver.Reset();
            score = 0;
            timeBonus = 0;
            Difficulty(out timeLeft, out _, out _);
        }

        private void NewGame()
        {
            StopTimer();
            // 路上的車開過一場就亂掉了，重建一次
            BuildTrack();
            ResetRun();
            state = GameState.Countdown;
            countdown = 3.0;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer, new game difficulty={difficulty}");
            StartTimer();
        }

        private void Resume()
        {
            state = pausedFrom;
            StartTimer();
        }

        private void StartTimer()
        {
            if (disposed || frameTimer == null) return;
            timerRunning = true;
            lastFrameTicks = clock.ElapsedTicks;
            frameTimer.Change(0, FRAME_MS);
        }

        private void StopTimer()
        {
            timerRunning = false;
            frameTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private double Now() => clock.Elapsed.TotalSeconds;

        private void OnFrame(object _)
        {
            if (Interlocked.Exchange(ref inFrame, 1) == 1)
                return;

            try
            {
                lock (sync)
                {
                    if (!timerRunning) return;
                    long nowTicks = clock.ElapsedTicks;
                    double dt = (nowTicks - lastFrameTicks) / (double)Stopwatch.Frequency;
                    lastFrameTicks = nowTicks;
                    Update(Math.Max(0, Math.Min(0.1, dt)));
                }
                Render();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"racer, OnFrame error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref inFrame, 0);
            }
        }

        private void Update(double dt)
        {
            driver.Tick(dt, Now());

            if (state == GameState.Countdown)
            {
                countdown -= dt;
                if (countdown <= 0)
                {
                    state = GameState.Playing;
                    driver.ShowMessage("GO!", null, 0.8);
                }
                return;
            }

            track.UpdateCars(dt);
            driver.Drive(track, dt, state == GameState.Playing, state == GameState.Goal ? 0.35 : 0.25);

            if (state == GameState.Playing)
            {
                score += (long)(driver.Kmh * dt * 40);
                timeLeft -= dt;
                if (timeLeft <= 0)
                {
                    timeLeft = 0;
                    state = GameState.TimeUp;
                    stateTimer = 0;
                    driver.Braking = false;
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"racer, time up score={score} stage={driver.Stage + 1}");
                }
            }

            if (state == GameState.Playing && driver.ReachedCheckpoint(track))
                PassCheckpoint();

            // 時間到 / 到終點之後滑行到停下來
            if (state == GameState.TimeUp || state == GameState.Goal)
            {
                if (driver.Speed <= 0)
                    stateTimer += dt;
                if (stateTimer > 1.2)
                {
                    state = state == GameState.Goal ? GameState.Finished : GameState.GameOver;
                    UpdateHiScore();
                    StopTimer();
                }
            }
        }

        private void PassCheckpoint()
        {
            if (driver.Stage == STAGES - 1)
            {
                timeBonus = (long)(timeLeft * 10) * 10000;
                score += timeBonus;
                state = GameState.Goal;
                stateTimer = 0;
                driver.Braking = false;
                driver.ShowMessage("GOAL!", $"TIME BONUS {timeBonus}", 99);
                Logger.Instance.LogMessage(TracingLevel.INFO, $"racer, goal score={score} timeLeft={timeLeft:F1}");
                return;
            }

            Difficulty(out _, out double extend, out _);
            timeLeft += Math.Max(30, extend - driver.Stage * 2);
            driver.NextStage();
            driver.ShowMessage("CHECKPOINT!", "TIME EXTENDED", 2.5);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer, checkpoint stage={driver.Stage + 1} timeLeft={timeLeft:F1}");
        }

        private void UpdateHiScore()
        {
            if (score <= hiScore) return;
            hiScore = score;
            HiScoreChanged?.Invoke(hiScore);
        }

        #endregion

        #region 繪圖

        public void Render()
        {
            try
            {
                string b64;
                lock (sync)
                {
                    if (disposed) return;
                    using (Bitmap bmp = renderer.Render(track, driver, carColor, null, DrawHud))
                    {
                        b64 = Tools.ImageToBase64(bmp, true);
                    }
                }
                sink(b64);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"racer, Render error: {ex.Message}");
            }
        }

        private void DrawHud(RacerRenderer r)
        {
            const int W = RacerConst.W;
            if (state != GameState.Idle)
            {
                r.Text("SCORE", r.Small, RacerRenderer.HudBlue, 2, 0);
                r.Text(score.ToString(), r.Small, Color.White, 2, 8);

                r.Text("TIME", r.Small, RacerRenderer.HudYellow, W / 2f, 0, StringAlignment.Center);
                int secs = (int)Math.Ceiling(timeLeft);
                bool warn = secs <= 10 && state == GameState.Playing;
                Color tc = warn && (int)(Now() * 4) % 2 == 0 ? Color.FromArgb(0xFF, 0x40, 0x30) : RacerRenderer.HudYellow;
                r.Text(secs.ToString(), r.Big, tc, W / 2f, 7, StringAlignment.Center);

                r.StageBar($"STAGE {Math.Min(driver.Stage, STAGES - 1) + 1}", driver.StageProgress(track));
                r.Speedo(driver.Kmh);
            }

            switch (state)
            {
                case GameState.Idle:
                    r.Band(24, 44);
                    r.TextIn("SUNSET RACER", r.Title, Color.FromArgb(0xFF, 0x8C, 0x20), 24, 26);
                    r.TextIn("press dial to start", r.Mid, Color.FromArgb(0xD0, 0xD0, 0xD0), 48, 18);
                    if (hiScore > 0)
                        r.Text($"HI {hiScore}", r.Small, RacerRenderer.HudYellow, W / 2f, 2, StringAlignment.Center);
                    return;

                case GameState.Countdown:
                    r.TextIn(((int)Math.Ceiling(countdown)).ToString(), r.Huge, RacerRenderer.HudYellow, 22, 32);
                    r.TextIn($"STAGE 1  {RacerTheme.All[0].Name}", r.Small, Color.White, 52, 12);
                    return;

                case GameState.Paused:
                    r.Band(30, 30);
                    r.TextIn("PAUSED", r.Big, Color.White, 30, 18);
                    r.TextIn("tap strip to resume", r.Small, RacerRenderer.Grey, 46, 12);
                    return;

                case GameState.TimeUp:
                    r.TextIn("TIME UP", r.Title, Color.FromArgb(0xFF, 0x50, 0x30), 28, 26);
                    return;

                case GameState.GameOver:
                    r.Band(24, 44);
                    r.TextIn("GAME OVER", r.Title, Color.FromArgb(0xFF, 0x50, 0x30), 24, 24);
                    r.TextIn($"score {score}   hi {hiScore}", r.Small, Color.White, 46, 11);
                    r.TextIn("press dial to retry", r.Small, RacerRenderer.Grey, 56, 11);
                    return;

                case GameState.Finished:
                    r.Band(22, 48);
                    r.TextIn("CONGRATULATIONS!", r.Big, RacerRenderer.HudYellow, 22, 18);
                    r.TextIn($"time bonus {timeBonus}", r.Small, Color.White, 39, 11);
                    r.TextIn($"score {score}   hi {hiScore}", r.Small, Color.White, 49, 11);
                    r.TextIn("press dial to play again", r.Small, RacerRenderer.Grey, 59, 11);
                    return;
            }

            r.Message(driver, Now());
        }

        #endregion
    }
}
