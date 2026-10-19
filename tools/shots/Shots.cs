using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace MiniGame
{
    // 用遊戲本身的程式碼產生畫面，存成 PNG
    static class Shots
    {
        const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static string Out;

        static FieldInfo Fi(Type t, string n) { for (; t != null; t = t.BaseType) { var f = t.GetField(n, ALL); if (f != null) return f; } throw new Exception("no field " + n); }
        static object F(object o, string n) => Fi(o.GetType(), n).GetValue(o);
        static void S(object o, string n, object v) => Fi(o.GetType(), n).SetValue(o, v);
        static object Call(object o, string m, params object[] a) => o.GetType().GetMethod(m, ALL).Invoke(o, a);
        static void State(object o, string v) { var f = Fi(o.GetType(), "state"); f.SetValue(o, Enum.Parse(f.FieldType, v)); }
        static object Const(Type t, string n) => t.GetField(n, ALL).GetValue(null);

        static Bitmap FromB64(string b64)
        {
            int i = b64.IndexOf(',');
            var bytes = Convert.FromBase64String(i >= 0 ? b64.Substring(i + 1) : b64);
            using (var ms = new MemoryStream(bytes)) return new Bitmap(new Bitmap(ms));
        }

        static void Save(Bitmap b, string name) { b.Save(Path.Combine(Out, name + ".png"), ImageFormat.Png); }

        // 用法：dotnet run -c Release [repo 根目錄]
        // 遊戲畫面先存到暫存資料夾，排好的圖寫到 marketplace/
        static void Main(string[] args)
        {
            string root = args.Length > 0 ? args[0]
                : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));
            Out = Path.Combine(Path.GetTempPath(), "sd-arcade-shots");
            Directory.CreateDirectory(Out);
            Breakout();
            Snake();
            Foosball();
            Race4P();
            Stages();
            Compose.Run(Out, Path.Combine(root, "marketplace"), root);
            Console.WriteLine("ok: " + Path.Combine(root, "marketplace"));
        }

        // ---------- Breakout：球拍自動追球 ----------
        static void Breakout()
        {
            var a = (BreakoutAction)FormatterServices.GetUninitializedObject(typeof(BreakoutAction));
            var settingsType = typeof(BreakoutAction).GetNestedType("PluginSettings", ALL);
            S(a, "settings", settingsType.GetMethod("CreateDefaultSettings").Invoke(null, null));
            S(a, "sync", new object());
            S(a, "bricks", new bool[4, 10]);
            S(a, "rng", new Random(7));
            S(a, "level", 1);
            Call(a, "NewGame");
            Call(a, "Launch");
            int frames = 0;
            while ((int)F(a, "score") < 140 && frames++ < 20000)
            {
                double bx = (double)F(a, "ballX");
                S(a, "paddleX", Math.Max(0, Math.Min(200 - 32, bx - 16 + Math.Sin(frames * 0.05) * 9)));
                Call(a, "Step");
                if (F(a, "state").ToString() == "Ready") Call(a, "Launch");
            }
            // 球往上飛的時候拍
            for (int i = 0; i < 200 && ((double)F(a, "ballVy") > 0 || (double)F(a, "ballY") > 60); i++)
            {
                S(a, "paddleX", Math.Max(0, Math.Min(168, (double)F(a, "ballX") - 12)));
                Call(a, "Step");
            }
            using (var b = (Bitmap)Call(a, "Draw")) Save(b, "breakout");
        }

        // ---------- Snake：貪心走向食物 ----------
        static void Snake()
        {
            var a = (SnakeAction)FormatterServices.GetUninitializedObject(typeof(SnakeAction));
            var settingsType = typeof(SnakeAction).GetNestedType("PluginSettings", ALL);
            S(a, "settings", settingsType.GetMethod("CreateDefaultSettings").Invoke(null, null));
            S(a, "sync", new object());
            S(a, "rng", new Random(3));
            S(a, "snake", new LinkedList<Point>());
            S(a, "occupied", new HashSet<Point>());
            S(a, "pendingTurns", new Queue<int>());
            S(a, "best", 21);
            int cols = (int)Const(typeof(SnakeAction), "COLS"), rows = (int)Const(typeof(SnakeAction), "ROWS");
            Call(a, "NewGame");
            var snake = (LinkedList<Point>)F(a, "snake");
            var occ = (HashSet<Point>)F(a, "occupied");
            var turns = (Queue<int>)F(a, "pendingTurns");
            int Corners()
            {
                int n = 0; Point? prev = null, prevD = null;
                foreach (var p in snake)
                {
                    if (prev != null)
                    {
                        var d = new Point(p.X - prev.Value.X, p.Y - prev.Value.Y);
                        if (prevD != null && d != prevD.Value) n++;
                        prevD = d;
                    }
                    prev = p;
                }
                return n;
            }
            for (int i = 0; i < 20000 && ((int)F(a, "score") < 10 || Corners() < 5 || F(a, "state").ToString() != "Playing"); i++)
            {
                if (F(a, "state").ToString() != "Playing" || (int)F(a, "score") > 18) Call(a, "NewGame");
                int dx = (int)F(a, "dx"), dy = (int)F(a, "dy");
                Point h = snake.First.Value, food = (Point)F(a, "food");
                int bestTurn = 0, bestD = int.MaxValue;
                foreach (int t in new[] { 0, 1, -1 })
                {
                    int ndx = t == 0 ? dx : t > 0 ? -dy : dy, ndy = t == 0 ? dy : t > 0 ? dx : -dx;
                    var n = new Point(h.X + ndx, h.Y + ndy);
                    if (n.X < 0 || n.Y < 0 || n.X >= cols || n.Y >= rows || (occ.Contains(n) && n != snake.Last.Value)) continue;
                    int d = Math.Abs(n.X - food.X) + Math.Abs(n.Y - food.Y);
                    if (d < bestD) { bestD = d; bestTurn = t; }
                }
                if (bestTurn != 0) turns.Enqueue(bestTurn);
                Call(a, "Step");
            }
            // 自動玩的蛇形狀不好看，直接排一條 S 形的蛇（尾 → 頭）
            snake.Clear(); occ.Clear();
            var path = new List<Point>();
            void Line(int x0, int y0, int x1, int y1)
            {
                int sx = Math.Sign(x1 - x0), sy = Math.Sign(y1 - y0);
                for (int x = x0, y = y0; ; x += sx, y += sy) { var p = new Point(x, y); if (path.Count == 0 || path[path.Count - 1] != p) path.Add(p); if (x == x1 && y == y1) break; }
            }
            Line(5, 14, 13, 14); Line(13, 14, 13, 8); Line(13, 8, 21, 8); Line(21, 8, 21, 12); Line(21, 12, 28, 12); Line(28, 12, 28, 6);
            foreach (var p in path) { snake.AddFirst(p); occ.Add(p); }
            S(a, "dx", 0); S(a, "dy", -1);
            S(a, "food", new Point(28, 2));
            S(a, "score", 13);
            State(a, "Playing");
            using (var b = (Bitmap)Call(a, "Draw")) Save(b, "snake");
        }

        // ---------- Foosball ----------
        static void Foosball()
        {
            var g = FoosballGame.Acquire("shots");
            var last = new string[4];
            for (int c = 0; c < 4; c++) { int cc = c; g.Attach(c, b => last[cc] = b); }
            g.Press(0);
            Call(g, "StopTimer");
            var rng = new Random(5);
            for (int i = 0; i < 260; i++)
            {
                if (i % 3 == 0) g.Rotate(rng.Next(4), rng.Next(-3, 4), 4);
                if (i % 17 == 0) g.Press(rng.Next(4));
                Call(g, "Step");
                Call(g, "StopTimer");
                if (F(g, "state").ToString() != "Playing" && i > 200) break;
            }
            using (var b = (Bitmap)Call(g, "Draw")) Save(b, "foosball");
            g.Release();
        }

        // ---------- 4 人賽車：自動駕駛，存好幾個時間點 ----------
        static void Race4P()
        {
            var g = RacerRaceGame.Acquire("race");
            var last = new string[4];
            for (int c = 0; c < 4; c++) { int cc = c; g.Attach(c, b => last[cc] = b); }
            g.SetRace("long", "normal");
            for (int c = 0; c < 4; c++) g.Press(c);
            Call(g, "StopTimer");

            var map = RacerMap.Acquire("race");
            var tiles = new string[8];
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 4; c++) { int k = r * 4 + c; map.Attach(c, r, b => tiles[k] = b); }

            var players = (Array)F(g, "players");
            var track = (RacerTrack)F(g, "track");
            var drivers = Enumerable.Range(0, 4).Select(i => (RacerDriver)F(players.GetValue(i), "D")).ToArray();
            double[] pref = { -0.55, 0.55, 0, -0.55 };
            double[] lane = (double[])pref.Clone();
            double[] lanes = { -0.62, 0, 0.62 };
            // 每個人在不同時間加速，把車隊拉開
            double[][] boosts = { new double[0], new[] { 6.0 }, new[] { 3.0, 14 }, new[] { 9.0, 20 } };

            double t = 0;
            int shot = 0;
            for (int step = 0; step < 25 * 70; step++)
            {
                t += 0.04;
                for (int i = 0; i < 4; i++)
                {
                    RacerDriver d = drivers[i];
                    d.AutoCenter = false;
                    double sp = d.Speed / RacerConst.MAX_SPEED;
                    int idx = (int)(d.Z / RacerConst.SEG_LEN);
                    bool Blocked(double l)
                    {
                        for (int k = 0; k < 28; k++)
                        {
                            var seg = track.Segments[(idx + k) % track.Segments.Count];
                            if (seg.Cars != null && seg.Cars.Any(c => Math.Abs(c.Offset - l) < 0.5)) return true;
                        }
                        return false;
                    }
                    if (Blocked(lane[i]))
                    {
                        var free = lanes.Where(l => !Blocked(l)).OrderBy(l => Math.Abs(l - d.X)).ToList();
                        if (free.Count > 0) lane[i] = free[0];
                    }
                    else if (!Blocked(pref[i])) lane[i] = pref[i];

                    double curve = track.SegmentAt(d.Z).Curve;
                    double ff = sp * sp * curve * RacerConst.CENTRIFUGAL / (RacerConst.STEER_RATE * Math.Min(1, 0.25 + sp));
                    d.Steer = Math.Max(-1, Math.Min(1, ff + (lane[i] - d.X) * 2.2));
                    if (boosts[i].Any(b => t >= b && t - 0.04 < b)) d.Boost();
                }
                Call(g, "Update", 0.04);
                if (step % 50 == 0 && step >= 100)
                {
                    g.RenderAndSend();
                    Call(map, "Render");
                    using (var strip = new Bitmap(800, 100))
                    {
                        using (var gg = Graphics.FromImage(strip))
                            for (int c = 0; c < 4; c++) using (var b = FromB64(last[c])) gg.DrawImage(b, c * 200, 0, 200, 100);
                        Save(strip, $"race_{shot:00}");
                    }
                    using (var keys = new Bitmap(4 * 144, 2 * 144))
                    {
                        using (var gg = Graphics.FromImage(keys))
                            for (int k = 0; k < 8; k++) if (tiles[k] != null) using (var b = FromB64(tiles[k])) gg.DrawImage(b, k % 4 * 144, k / 4 * 144, 144, 144);
                        Save(keys, $"map_{shot:00}");
                    }
                    shot++;
                }
            }
            map.Release();
            g.Release();
        }

        // ---------- 5 個主題：直接用 renderer 畫在每關中段的彎道 ----------
        static void Stages()
        {
            var track = RacerTrack.Build(1986, 5, 2100, 30, RacerConst.MAX_SPEED);
            var renderer = new RacerRenderer();
            for (int s = 0; s < 5; s++)
            {
                int start = track.StageStart[s] + 400;
                int i = start;
                while (i < track.Checkpoint[s] - 60 && !(Math.Abs(track.Segments[i + 25].Curve) > 3 && Math.Abs(track.Segments[i].Curve) < 0.5)) i++;
                var d = new RacerDriver();
                d.Reset(i * RacerConst.SEG_LEN - RacerConst.PLAYER_Z, 0.15);
                d.Speed = RacerConst.MAX_SPEED * 0.85;
                d.Stage = s; d.BgFrom = d.BgTo = s;
                d.FarOffset = i * 0.37; d.NearOffset = i * 0.9;
                d.Steer = track.Segments[i + 25].Curve > 0 ? 0.6 : -0.6;
                using (var b = renderer.Render(track, d, RacerConst.PlayerColors[0], null, r =>
                       {
                           r.StageBar($"STAGE {s + 1}", d.StageProgress(track));
                           r.Speedo(d.Kmh);
                       }))
                    Save(b, $"stage_{s}");
            }
        }
    }
}
