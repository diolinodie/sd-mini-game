using BarRaider.SdTools;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Threading;

namespace MiniGame
{
    /// <summary>地圖上的一台車</summary>
    internal struct RacerMarker
    {
        public Color Color;
        public double Z;
        public string Label;
        public int Place;          // 名次，0 = 不排名
        public bool Finished;
    }

    /// <summary>可以被地圖顯示的比賽（單人版、4P 版）</summary>
    internal interface IRacerSource
    {
        string DeviceId { get; }
        int MapPriority { get; }      // 數字大的優先：4P = 1，單人 = 0
        bool MapActive { get; }       // 正在跑（不是標題畫面）
        /// <summary>回傳賽道並填入車子位置，status 是地圖角落的狀態文字。實作要自己鎖。</summary>
        RacerTrack CollectMarkers(List<RacerMarker> markers, out string status);
    }

    /// <summary>
    /// 地圖儀表板：放在 Stream Deck+ 上方按鍵的 RacerMapAction 共用一個，
    /// 把同一台裝置上的賽車畫成俯視地圖。放了幾顆按鍵就依位置拼成一張大圖。
    /// </summary>
    public sealed class RacerMap
    {
        #region 比賽登記

        private static readonly object hubLock = new object();
        private static readonly List<IRacerSource> sources = new List<IRacerSource>();

        internal static void Register(IRacerSource src)
        {
            lock (hubLock) { if (!sources.Contains(src)) sources.Add(src); }
        }

        internal static void Unregister(IRacerSource src)
        {
            lock (hubLock) { sources.Remove(src); }
        }

        private static List<IRacerSource> SourcesOf(string deviceId)
        {
            lock (hubLock) { return sources.Where(s => s.DeviceId == deviceId).ToList(); }
        }

        #endregion

        #region 共用實例（每台裝置一個）

        private static readonly Dictionary<string, RacerMap> maps = new Dictionary<string, RacerMap>();

        public static RacerMap Acquire(string deviceId)
        {
            deviceId = deviceId ?? "";
            lock (hubLock)
            {
                if (!maps.TryGetValue(deviceId, out RacerMap map))
                {
                    map = new RacerMap(deviceId);
                    maps[deviceId] = map;
                }
                map.refCount++;
                return map;
            }
        }

        public void Release()
        {
            lock (hubLock)
            {
                refCount--;
                if (refCount > 0)
                    return;
                maps.Remove(deviceId);
            }
            lock (sync)
            {
                disposed = true;
                timer?.Dispose();
                timer = null;
            }
        }

        #endregion

        private const int T = 144;           // 每顆按鍵的圖大小
        private const int FRAME_MS = 250;
        private const double FOLLOW_ZOOM = 3.5;

        private sealed class Tile
        {
            public int Col, Row;
            public Action<string> Sink;
            public string LastSent;
        }

        private readonly string deviceId;
        private readonly object sync = new object();
        private readonly List<Tile> tiles = new List<Tile>();
        private int refCount;
        private bool follow;
        private Timer timer;
        private int inFrame;
        private bool disposed;

        private RacerMap(string deviceId)
        {
            this.deviceId = deviceId;
            timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Attach(int col, int row, Action<string> sink)
        {
            lock (sync)
            {
                tiles.RemoveAll(t => t.Col == col && t.Row == row);
                tiles.Add(new Tile { Col = col, Row = row, Sink = sink });
                // 拼圖範圍變了，每格都要重送
                foreach (Tile t in tiles) t.LastSent = null;
                if (!disposed) timer?.Change(0, FRAME_MS);
            }
        }

        public void Detach(int col, int row, Action<string> sink)
        {
            lock (sync)
            {
                tiles.RemoveAll(t => t.Col == col && t.Row == row && t.Sink == sink);
                foreach (Tile t in tiles) t.LastSent = null;
                if (tiles.Count == 0) timer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <summary>按鍵按下：全圖 ↔ 跟著領先者放大</summary>
        public void ToggleView()
        {
            lock (sync)
            {
                follow = !follow;
                foreach (Tile t in tiles) t.LastSent = null;
            }
            Render();
        }

        private void OnTimer(object _)
        {
            if (Interlocked.Exchange(ref inFrame, 1) == 1)
                return;
            try
            {
                Render();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"racer map, render error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref inFrame, 0);
            }
        }

        private void Render()
        {
            List<Tile> snapshot;
            bool followView;
            lock (sync)
            {
                if (disposed || tiles.Count == 0) return;
                snapshot = tiles.ToList();
                followView = follow;
            }

            // 先放開自己的鎖再去拿比賽的資料，避免跟比賽的鎖互卡
            var markers = new List<RacerMarker>();
            RacerTrack track = null;
            string status = null;
            List<IRacerSource> all = SourcesOf(deviceId);
            List<IRacerSource> active = all.Where(s => s.MapActive).ToList();
            if (active.Count > 0)
            {
                int top = active.Max(s => s.MapPriority);
                foreach (IRacerSource s in active.Where(s => s.MapPriority == top))
                {
                    RacerTrack t = s.CollectMarkers(markers, out string st);
                    if (track == null) { track = t; status = st; }
                }
            }
            else if (all.Count > 0)
            {
                // 沒人在跑：顯示賽道，車子都在起點
                IRacerSource s = all.OrderByDescending(x => x.MapPriority).First();
                track = s.CollectMarkers(markers, out status);
            }

            int minC = snapshot.Min(t => t.Col), maxC = snapshot.Max(t => t.Col);
            int minR = snapshot.Min(t => t.Row), maxR = snapshot.Max(t => t.Row);
            int cw = (maxC - minC + 1) * T, ch = (maxR - minR + 1) * T;

            var outgoing = new List<KeyValuePair<Action<string>, string>>();
            using (var canvas = new Bitmap(cw, ch, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                    Draw(g, cw, ch, track, markers, status, followView);

                lock (sync)
                {
                    foreach (Tile t in tiles)
                    {
                        if (t.Col < minC || t.Col > maxC || t.Row < minR || t.Row > maxR) continue;
                        using (Bitmap crop = canvas.Clone(new Rectangle((t.Col - minC) * T, (t.Row - minR) * T, T, T), canvas.PixelFormat))
                        {
                            string b64 = Tools.ImageToBase64(crop, true);
                            if (b64 == t.LastSent) continue;
                            t.LastSent = b64;
                            outgoing.Add(new KeyValuePair<Action<string>, string>(t.Sink, b64));
                        }
                    }
                }
            }

            foreach (var kv in outgoing)
                kv.Key(kv.Value);
        }

        private static void Draw(Graphics g, int cw, int ch, RacerTrack track, List<RacerMarker> markers, string status, bool followView)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, cw, ch), Color.FromArgb(0x1A, 0x1E, 0x3A), Color.FromArgb(0x0C, 0x0E, 0x1A), LinearGradientMode.Vertical))
                g.FillRectangle(bg, 0, 0, cw, ch);

            float u = T / 144f;
            using (var titleFont = new Font("Arial", 20 * u, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel))
            using (var font = new Font("Arial", 13 * u, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                if (track == null)
                {
                    g.DrawString("RACER MAP", titleFont, new SolidBrush(Color.FromArgb(0xFF, 0x8C, 0x20)), new RectangleF(0, 0, cw, ch * 0.6f), center);
                    g.DrawString("no race running", font, Brushes.Gray, new RectangleF(0, ch * 0.45f, cw, ch * 0.4f), center);
                    return;
                }

                PointF[] pts = track.MapPoints;
                RectangleF b = track.MapBounds;
                float margin = 14 * u;
                double scale = Math.Min((cw - margin * 2) / b.Width, (ch - margin * 2) / b.Height);
                double ox = b.X + b.Width / 2, oy = b.Y + b.Height / 2;

                if (followView && markers.Count > 0)
                {
                    RacerMarker lead = markers.OrderBy(m => m.Place == 0 ? int.MaxValue : m.Place).ThenByDescending(m => m.Z).First();
                    PointF p = pts[SegIndex(track, lead.Z)];
                    ox = p.X;
                    oy = p.Y;
                    scale *= FOLLOW_ZOOM;
                }

                PointF Map(PointF p) => new PointF((float)((p.X - ox) * scale + cw / 2.0), (float)((p.Y - oy) * scale + ch / 2.0));

                // 賽道：每關用那一關的地面顏色
                int goal = track.GoalIndex;
                float roadW = Math.Max(3, 7 * u);
                using (var outline = new Pen(Color.FromArgb(0x08, 0x08, 0x10), roadW + 3 * u) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLines(outline, Polyline(pts, 0, goal, Map));
                }
                for (int s = 0; s < track.Checkpoint.Length; s++)
                {
                    int from = track.StageStart[s], to = track.Checkpoint[s];
                    using (var pen = new Pen(RacerTheme.All[s].GrassLight, roadW) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLines(pen, Polyline(pts, from, to, Map));
                }

                // 起點、檢查點、終點
                PointF start = Map(pts[RacerTrack.START_GATE]);
                float fr = 5 * u;
                g.FillEllipse(Brushes.White, start.X - fr, start.Y - fr, fr * 2, fr * 2);
                for (int s = 0; s < track.Checkpoint.Length - 1; s++)
                {
                    PointF c = Map(pts[track.Checkpoint[s]]);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(0x20, 0x40, 0xC0)), c.X - fr, c.Y - fr, fr * 2, fr * 2);
                    g.DrawRectangle(Pens.White, c.X - fr, c.Y - fr, fr * 2, fr * 2);
                }
                PointF gp = Map(pts[goal]);
                float q = fr;
                for (int i = 0; i < 4; i++)
                    g.FillRectangle((i == 0 || i == 3) ? Brushes.White : Brushes.Black, gp.X - q + (i % 2) * q, gp.Y - q + (i / 2) * q, q, q);
                g.DrawRectangle(Pens.White, gp.X - q, gp.Y - q, q * 2, q * 2);

                // 車子：後面的先畫，領先的蓋在上面
                float r = 9 * u;
                using (var outlinePen = new Pen(Color.White, 2 * u))
                using (var labelFont = new Font("Arial", 11 * u, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    foreach (RacerMarker m in markers.OrderBy(m => m.Z))
                    {
                        PointF p = Map(pts[SegIndex(track, m.Z)]);
                        using (var fill = new SolidBrush(m.Color))
                            g.FillEllipse(fill, p.X - r, p.Y - r, r * 2, r * 2);
                        g.DrawEllipse(outlinePen, p.X - r, p.Y - r, r * 2, r * 2);
                        if (m.Label != null)
                        {
                            g.DrawString(m.Label, labelFont, Brushes.Black, new RectangleF(p.X - r + 1, p.Y - r + 1, r * 2, r * 2), center);
                            g.DrawString(m.Label, labelFont, Brushes.White, new RectangleF(p.X - r, p.Y - r, r * 2, r * 2), center);
                        }
                    }
                }

                // 名次表
                var ranked = markers.Where(m => m.Place > 0).OrderBy(m => m.Place).ToList();
                float y = 4 * u;
                foreach (RacerMarker m in ranked)
                {
                    using (var fill = new SolidBrush(m.Color))
                        g.FillRectangle(fill, 4 * u, y + 2 * u, 10 * u, 10 * u);
                    string line = $"{RacerConst.Ordinal(m.Place)} {m.Label}{(m.Finished ? " FIN" : "")}";
                    g.DrawString(line, font, Brushes.Black, 17 * u + 1, y + 1);
                    g.DrawString(line, font, Brushes.White, 17 * u, y);
                    y += 15 * u;
                }

                if (!string.IsNullOrEmpty(status))
                {
                    using (var far = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far })
                    {
                        g.DrawString(status, font, Brushes.Black, new RectangleF(1, 1, cw - 4 * u, ch - 3 * u), far);
                        g.DrawString(status, font, new SolidBrush(RacerRenderer.HudYellow), new RectangleF(0, 0, cw - 4 * u, ch - 3 * u), far);
                    }
                }
            }
        }

        private static int SegIndex(RacerTrack track, double z)
        {
            int i = (int)(z / RacerTrack.SEG_LEN);
            return Math.Max(0, Math.Min(track.GoalIndex, i));
        }

        private static PointF[] Polyline(PointF[] pts, int from, int to, Func<PointF, PointF> map)
        {
            const int STEP = 4;
            var list = new List<PointF>();
            for (int i = from; i < to; i += STEP)
                list.Add(map(pts[i]));
            list.Add(map(pts[to]));
            if (list.Count < 2) list.Add(list[0]);
            return list.ToArray();
        }
    }
}
