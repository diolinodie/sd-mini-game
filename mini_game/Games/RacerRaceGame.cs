using BarRaider.SdTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace MiniGame
{
    /// <summary>
    /// 4 人同場賽車。同一台 Stream Deck+ 上的 4 個 RacerRaceAction 共用一個實例，
    /// 每顆旋鈕一位玩家，觸控條每一格是那位玩家自己的車後視角（4 分割畫面），彼此看得到對方的車。
    /// 開始畫面按旋鈕加入，第一個人加入後倒數 ENTRY_TIME 秒開跑（全員加入或點觸控條就馬上開跑）。
    /// 沒有時間限制，比名次；第一名到終點後其他人還有 FINISH_WAIT 秒，沒到的依位置排名。
    /// </summary>
    public sealed class RacerRaceGame : IRacerSource
    {
        #region 共用實例（每台裝置一個）

        private static readonly Dictionary<string, RacerRaceGame> games = new Dictionary<string, RacerRaceGame>();
        private static readonly object gamesLock = new object();

        public static RacerRaceGame Acquire(string deviceId)
        {
            deviceId = deviceId ?? "";
            lock (gamesLock)
            {
                if (!games.TryGetValue(deviceId, out RacerRaceGame game))
                {
                    game = new RacerRaceGame(deviceId);
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

            RacerMap.Unregister(this);
            lock (sync)
            {
                disposed = true;
                frameTimer?.Dispose();
                frameTimer = null;
                renderer.Dispose();
            }
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer4p, game released device={DeviceId}");
        }

        #endregion

        public const int SLOTS = 4;

        private const int STAGE_SEGS = 1600;
        private const int TRACK_SEED = 2024;
        private const int FRAME_MS = 40;                   // 25 fps，4 個畫面連 PNG 一幀約 6 ms
        private const double ENTRY_TIME = 10;
        private const double FINISH_WAIT = 20;
        private const double GRID_GAP = 600;               // 前後排距離

        private enum GameState { Lobby, Countdown, Racing, Paused, Results }

        private sealed class Player
        {
            public readonly RacerDriver D = new RacerDriver();
            public bool Joined;
            public bool Finished;
            public double FinishTime;
            public int Place;
            public Action<string> Sink;
            public string LastSent;
        }

        public string DeviceId { get; }

        private readonly object sync = new object();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly RacerRenderer renderer = new RacerRenderer();
        private readonly Player[] players = new Player[SLOTS];

        private int refCount;
        private RacerTrack track;
        private GameState state = GameState.Lobby;
        private GameState pausedFrom;
        private double countdown;
        private double entryTimer = -1;
        private double raceTime;
        private double finishWait = -1;
        private int finishCount;
        private int stages = 3;
        private string traffic = "normal";

        private Timer frameTimer;
        private bool timerRunning;
        private int inFrame;
        private long lastFrameTicks;
        private bool disposed;

        private RacerRaceGame(string deviceId)
        {
            DeviceId = deviceId;
            for (int i = 0; i < SLOTS; i++) players[i] = new Player();
            BuildTrack();
            PlaceGrid();
            frameTimer = new Timer(OnFrame, null, Timeout.Infinite, Timeout.Infinite);
            RacerMap.Register(this);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer4p, game created device={deviceId}");
        }

        #region 給 action 呼叫的介面

        public void Attach(int column, Action<string> sink)
        {
            lock (sync)
            {
                players[column].Sink = sink;
                players[column].LastSent = null;
            }
            RenderIfIdle();
        }

        public void Detach(int column, Action<string> sink)
        {
            lock (sync)
            {
                Player p = players[column];
                if (p.Sink != sink) return;
                p.Sink = null;
                p.LastSent = null;
                if (state == GameState.Lobby) p.Joined = false;
            }
        }

        public void Configure(int column, string sensitivity, string steerMode)
        {
            lock (sync)
            {
                players[column].D.SteerSens = RacerConst.SteerSensitivity(sensitivity);
                players[column].D.AutoCenter = steerMode != "hold";
            }
        }

        /// <summary>比賽長度、路上車量，四個旋鈕共用，最後改的生效；比賽中改的下一場生效</summary>
        public void SetRace(string length, string trafficLevel)
        {
            lock (sync)
            {
                int n = length == "short" ? 2 : length == "long" ? 5 : 3;
                string t = trafficLevel ?? "normal";
                if (n == stages && t == traffic) return;
                stages = n;
                traffic = t;
                if (state == GameState.Lobby)
                {
                    BuildTrack();
                    PlaceGrid();
                }
            }
            RenderIfIdle();
        }

        public void Rotate(int column, int ticks)
        {
            lock (sync)
            {
                Player p = players[column];
                if (!p.Joined || p.Finished) return;
                if (state != GameState.Countdown && state != GameState.Racing) return;
                p.D.Rotate(ticks, Now());
            }
        }

        /// <summary>旋鈕按下：加入 / 繼續 / 加速</summary>
        public void Press(int column)
        {
            lock (sync)
            {
                switch (state)
                {
                    case GameState.Lobby:
                        Join(column);
                        break;
                    case GameState.Results:
                        BackToLobby();
                        Join(column);
                        break;
                    case GameState.Paused:
                        Resume();
                        break;
                    case GameState.Racing:
                        Player p = players[column];
                        if (p.Joined && !p.Finished && p.D.Boost())
                            p.D.ShowMessage("BOOST!", null, 0.8);
                        break;
                }
            }
            RenderIfIdle();
        }

        /// <summary>觸控條點一下：開始 / 暫停 / 繼續</summary>
        public void Tap()
        {
            lock (sync)
            {
                switch (state)
                {
                    case GameState.Lobby:
                        if (players.Any(p => p.Joined))
                            StartCountdown();
                        break;
                    case GameState.Results:
                        BackToLobby();
                        break;
                    case GameState.Paused:
                        Resume();
                        break;
                    default:
                        pausedFrom = state;
                        state = GameState.Paused;
                        StopTimer();
                        break;
                }
            }
            RenderIfIdle();
        }

        /// <summary>觸控條長按：回到開始畫面</summary>
        public void Reset()
        {
            lock (sync) { BackToLobby(); }
            RenderIfIdle();
        }

        #endregion

        #region 地圖

        public int MapPriority => 1;

        public bool MapActive
        {
            get { lock (sync) { return state != GameState.Lobby || players.Any(p => p.Joined); } }
        }

        RacerTrack IRacerSource.CollectMarkers(List<RacerMarker> markers, out string status)
        {
            lock (sync)
            {
                for (int i = 0; i < SLOTS; i++)
                {
                    Player p = players[i];
                    if (!p.Joined) continue;
                    markers.Add(new RacerMarker
                    {
                        Color = RacerConst.PlayerColors[i],
                        Z = p.D.Z,
                        Label = $"P{i + 1}",
                        Place = state == GameState.Lobby ? 0 : p.Place,
                        Finished = p.Finished,
                    });
                }
                switch (state)
                {
                    case GameState.Lobby: status = entryTimer >= 0 ? $"ENTRY {Math.Ceiling(entryTimer)}" : "ENTRY"; break;
                    case GameState.Countdown: status = "READY"; break;
                    case GameState.Results: status = "RESULT"; break;
                    case GameState.Paused: status = "PAUSED"; break;
                    default: status = FormatTime(raceTime); break;
                }
                return track;
            }
        }

        #endregion

        #region 比賽流程

        private void BuildTrack()
        {
            int cars = traffic == "none" ? 0 : traffic == "heavy" ? 55 : 30;
            track = RacerTrack.Build(TRACK_SEED, stages, STAGE_SEGS, cars, RacerConst.MAX_SPEED);
        }

        // 起跑位置：旋鈕 1、2 第一排，3、4 第二排
        private void PlaceGrid()
        {
            for (int i = 0; i < SLOTS; i++)
            {
                double x = i % 2 == 0 ? -0.5 : 0.5;
                double pos = (i < 2 ? GRID_GAP : 0);
                players[i].D.Reset(pos, x);
                players[i].Finished = false;
                players[i].Place = 0;
            }
        }

        private void Join(int column)
        {
            Player p = players[column];
            if (p.Joined) return;
            p.Joined = true;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer4p, P{column + 1} joined");

            bool allIn = players.All(x => x.Sink == null || x.Joined);
            if (allIn)
            {
                StartCountdown();
                return;
            }
            if (entryTimer < 0)
                entryTimer = ENTRY_TIME;
            StartTimer();
        }

        private void StartCountdown()
        {
            // 路上的車重新排
            BuildTrack();
            PlaceGrid();
            entryTimer = -1;
            raceTime = 0;
            finishWait = -1;
            finishCount = 0;
            state = GameState.Countdown;
            countdown = 3;
            Logger.Instance.LogMessage(TracingLevel.INFO, $"racer4p, countdown players={players.Count(p => p.Joined)} stages={stages}");
            StartTimer();
        }

        private void BackToLobby()
        {
            StopTimer();
            state = GameState.Lobby;
            entryTimer = -1;
            foreach (Player p in players) p.Joined = false;
            BuildTrack();
            PlaceGrid();
        }

        private void Resume()
        {
            state = pausedFrom;
            StartTimer();
        }

        private void StartTimer()
        {
            if (disposed || frameTimer == null || timerRunning) return;
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
                RenderAndSend();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"racer4p, OnFrame error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref inFrame, 0);
            }
        }

        private void Update(double dt)
        {
            double now = Now();
            foreach (Player p in players)
                if (p.Joined) p.D.Tick(dt, now);

            switch (state)
            {
                case GameState.Lobby:
                    if (entryTimer >= 0)
                    {
                        entryTimer -= dt;
                        if (entryTimer <= 0) StartCountdown();
                    }
                    return;

                case GameState.Countdown:
                    countdown -= dt;
                    if (countdown <= 0)
                    {
                        state = GameState.Racing;
                        foreach (Player p in players)
                            if (p.Joined) p.D.ShowMessage("GO!", null, 0.8);
                    }
                    return;

                case GameState.Racing:
                    break;

                default:
                    return;
            }

            raceTime += dt;
            track.UpdateCars(dt);

            int last = track.Checkpoint.Length - 1;
            foreach (Player p in players)
            {
                if (!p.Joined) continue;
                p.D.Drive(track, dt, !p.Finished, 0.35);
                if (p.Finished || !p.D.ReachedCheckpoint(track)) continue;

                if (p.D.Stage == last)
                {
                    p.Finished = true;
                    p.FinishTime = raceTime;
                    p.Place = ++finishCount;
                    p.D.BoostTimer = 0;
                    p.D.ShowMessage("GOAL!", $"{RacerConst.Ordinal(p.Place)}  {FormatTime(raceTime)}", 99);
                    if (finishWait < 0) finishWait = FINISH_WAIT;
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"racer4p, finish place={p.Place} time={raceTime:F2}");
                }
                else
                {
                    p.D.NextStage();
                    p.D.ShowMessage("CHECKPOINT!", RacerTheme.All[p.D.Stage].Name, 2.5);
                }
            }

            PlayerCollisions();
            UpdatePlaces();

            if (finishWait >= 0)
            {
                finishWait -= dt;
                if (finishWait <= 0 || players.All(p => !p.Joined || p.Finished))
                {
                    state = GameState.Results;
                    UpdatePlaces();
                    StopTimer();
                    Logger.Instance.LogMessage(TracingLevel.INFO, "racer4p, results");
                }
            }
        }

        // 玩家之間互撞：往兩邊推開，後面撞上來的減速
        private void PlayerCollisions()
        {
            for (int i = 0; i < SLOTS; i++)
            {
                for (int j = i + 1; j < SLOTS; j++)
                {
                    Player a = players[i], b = players[j];
                    if (!a.Joined || !b.Joined || a.D.CrashTimer > 0 || b.D.CrashTimer > 0) continue;
                    double dz = a.D.Z - b.D.Z;
                    if (Math.Abs(dz) > RacerConst.SEG_LEN * 0.9) continue;
                    if (!RacerDriver.Overlap(a.D.X, RacerConst.PLAYER_W * 0.9, b.D.X, RacerConst.PLAYER_W * 0.9)) continue;

                    double side = a.D.X >= b.D.X ? 1 : -1;
                    a.D.X += side * 0.12;
                    b.D.X -= side * 0.12;
                    RacerDriver rear = dz < 0 ? a.D : b.D, front = dz < 0 ? b.D : a.D;
                    if (rear.Speed > front.Speed)
                        rear.Speed = front.Speed * 0.95;
                    a.D.ShakeTimer = b.D.ShakeTimer = 0.25;
                }
            }
        }

        // 名次：到終點的照到達順序，其他的照位置
        private void UpdatePlaces()
        {
            int place = finishCount;
            foreach (Player p in players.Where(p => p.Joined && !p.Finished).OrderByDescending(p => p.D.Z))
                p.Place = ++place;
        }

        private static string FormatTime(double t)
        {
            // 1'23"45
            int cs = (int)(t * 100);
            return $"{cs / 6000}'{cs / 100 % 60:00}\"{cs % 100:00}";
        }

        #endregion

        #region 繪圖

        private void RenderIfIdle()
        {
            bool running;
            lock (sync) { running = timerRunning; }
            if (!running) RenderAndSend();
        }

        public void RenderAndSend()
        {
            var outgoing = new List<KeyValuePair<Action<string>, string>>();
            try
            {
                lock (sync)
                {
                    if (disposed) return;
                    for (int i = 0; i < SLOTS; i++)
                    {
                        Player p = players[i];
                        if (p.Sink == null) continue;
                        string b64 = RenderSlot(i);
                        if (b64 == p.LastSent) continue;
                        p.LastSent = b64;
                        outgoing.Add(new KeyValuePair<Action<string>, string>(p.Sink, b64));
                    }
                }
                foreach (var kv in outgoing)
                    kv.Key(kv.Value);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"racer4p, Render error: {ex.Message}");
            }
        }

        private string RenderSlot(int slot)
        {
            Player me = players[slot];
            int view = slot;

            // 比賽中沒加入的格子：看領先者的畫面
            bool spectating = !me.Joined && state != GameState.Lobby;
            if (spectating)
            {
                Player lead = players.Where(p => p.Joined).OrderBy(p => p.Place).FirstOrDefault();
                if (lead != null) view = Array.IndexOf(players, lead);
            }

            var rivals = new List<RacerRival>();
            for (int i = 0; i < SLOTS; i++)
            {
                if (i == view) continue;
                // 開始畫面也把其他格的車畫出來，看得到起跑排位
                if (!players[i].Joined && state != GameState.Lobby) continue;
                RacerDriver o = players[i].D;
                rivals.Add(new RacerRival { Z = o.Z, X = o.X, Color = RacerConst.PlayerColors[i], Lean = o.Lean, CrashTimer = o.CrashTimer });
            }

            using (Bitmap bmp = renderer.Render(track, players[view].D, RacerConst.PlayerColors[view], rivals,
                       r => DrawHud(r, slot, view, spectating)))
            {
                return Tools.ImageToBase64(bmp, true);
            }
        }

        private void DrawHud(RacerRenderer r, int slot, int view, bool spectating)
        {
            const int W = RacerConst.W;
            Player me = players[slot];
            Player v = players[view];
            Color myColor = RacerConst.PlayerColors[slot];
            string tag = $"P{slot + 1}";

            switch (state)
            {
                case GameState.Lobby:
                    r.Band(12, 56);
                    r.TextIn("SUNSET RACER 4P", r.Big, Color.FromArgb(0xFF, 0x8C, 0x20), 12, 18);
                    if (me.Joined)
                    {
                        r.TextIn($"{tag} READY", r.Title, myColor, 28, 24);
                    }
                    else
                    {
                        r.TextIn(tag, r.Big, myColor, 28, 14);
                        r.TextIn("press dial to join", r.Mid, Color.White, 41, 13);
                    }
                    string entry = entryTimer >= 0 ? $"start in {Math.Ceiling(entryTimer)}   tap strip = go" : $"{stages} stages";
                    r.TextIn(entry, r.Small, RacerRenderer.HudYellow, 55, 12);
                    return;

                case GameState.Results:
                    r.Band(14, 72);
                    if (me.Joined)
                    {
                        r.TextIn(RacerConst.Ordinal(me.Place), r.Huge, myColor, 14, 30);
                        r.TextIn(me.Finished ? $"{tag}  {FormatTime(me.FinishTime)}" : $"{tag}  DNF", r.Mid, Color.White, 42, 14);
                    }
                    else
                    {
                        r.TextIn("RESULT", r.Title, RacerRenderer.HudYellow, 16, 26);
                    }
                    string line = string.Join("  ", players.Select((p, i) => new { p, i })
                        .Where(x => x.p.Joined).OrderBy(x => x.p.Place)
                        .Select(x => $"{x.p.Place}.P{x.i + 1}"));
                    r.TextIn(line, r.Small, RacerRenderer.Grey, 58, 12);
                    r.TextIn("press dial for next race", r.Small, RacerRenderer.Grey, 70, 12);
                    return;
            }

            // 比賽中的 HUD
            if (spectating)
            {
                r.Text("WATCHING", r.Small, RacerRenderer.Grey, 2, 0);
                r.Text($"P{view + 1}", r.Big, RacerConst.PlayerColors[view], 2, 8);
            }
            else
            {
                r.Text(state == GameState.Countdown ? tag : RacerConst.Ordinal(v.Place), r.Title, myColor, 2, -2);
                r.Text(tag, r.Small, Color.White, 4, 18);
            }
            r.Text("TIME", r.Small, RacerRenderer.HudYellow, W / 2f, 0, StringAlignment.Center);
            r.Text(FormatTime(raceTime), r.Mid, RacerRenderer.HudYellow, W / 2f, 8, StringAlignment.Center);
            r.StageBar($"STAGE {Math.Min(v.D.Stage, stages - 1) + 1}/{stages}", v.D.StageProgress(track));
            r.Speedo(v.D.Kmh);
            r.BoostPips(v.D, Now());

            if (state == GameState.Countdown)
            {
                r.TextIn(((int)Math.Ceiling(countdown)).ToString(), r.Huge, RacerRenderer.HudYellow, 22, 32);
                return;
            }
            if (state == GameState.Paused)
            {
                r.Band(30, 30);
                r.TextIn("PAUSED", r.Big, Color.White, 30, 18);
                r.TextIn("tap strip to resume", r.Small, RacerRenderer.Grey, 46, 12);
                return;
            }

            if (!spectating)
                r.Message(me.D, Now());
            if (finishWait >= 0 && !v.Finished)
                r.Text($"FINISH IN {Math.Ceiling(finishWait)}", r.Mid, Color.FromArgb(0xFF, 0x50, 0x30), W - 3, H_HUD, StringAlignment.Far);
        }

        private const float H_HUD = RacerConst.H - 14;

        #endregion
    }
}
