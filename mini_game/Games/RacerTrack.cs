using System;
using System.Collections.Generic;
using System.Drawing;

namespace MiniGame
{
    internal enum RacerSprite { Palm, Tree, Pine, Bush, Cactus, Rock, Billboard, Lamp }

    internal enum RacerGate { None, Start, Checkpoint, Goal }

    internal enum RacerCarType { Coupe, Beetle, Truck }

    internal sealed class RacerSpriteItem
    {
        public RacerSprite Type;
        public double Offset;      // 以半個路寬為單位，負數在左邊；左邊的物件右緣對齊這個位置，右邊的左緣對齊
        public int Variant;
    }

    internal sealed class RacerCar
    {
        public RacerCarType Type;
        public double Offset;      // 車子中心，以半個路寬為單位
        public double Z;
        public double Speed;
        public Color Color;
        public RacerSegment Segment;
    }

    /// <summary>
    /// 路的一小段（長 SEG_LEN）。Y1 → Y2 是前後兩端的高度，Curve 是這段的彎度。
    /// X/Y/W/Scale/Clip/Fog 是每一幀投影出來的結果，畫圖時用。
    /// </summary>
    internal sealed class RacerSegment
    {
        public int Index;
        public double Y1, Y2;
        public double Curve;
        public bool Dark;
        public int Stage;
        public RacerGate Gate;
        public List<RacerSpriteItem> Sprites;
        public List<RacerCar> Cars;

        public double X1, ScreenY1, W1, Scale1, CamZ1;
        public double X2, ScreenY2, W2, Scale2;
        public double Clip;
        public double Fog;
        public bool Visible;
    }

    /// <summary>每一關的配色、背景、路邊景物</summary>
    internal sealed class RacerTheme
    {
        public string Name;
        public Color SkyTop, SkyBottom;
        public bool Sea;
        public Color SeaColor;
        public bool Sun;
        public Color SunColor;
        public bool Clouds;
        public Color Far, FarCap, Near;
        public Color GrassLight, GrassDark;
        public Color RumbleLight, RumbleDark;
        public Color RoadLight, RoadDark;
        public Color Lane;
        public Color Fog;
        public Color Foliage;
        public RacerSprite[] Scenery;
        public RacerSprite? Rows;            // 沿路兩排整齊的景物（海灘的椰子樹）
        public int[] FarHeights;             // 遠山輪廓，週期 BG_PERIOD px
        public int[] NearHeights;            // 近丘輪廓

        public const int BG_PERIOD = 256;

        public static readonly RacerTheme[] All = CreateAll();

        private static RacerTheme[] CreateAll()
        {
            var t = new[]
            {
                new RacerTheme
                {
                    Name = "PALM BEACH",
                    SkyTop = Rgb(0x2E, 0x7B, 0xE0), SkyBottom = Rgb(0xB8, 0xE2, 0xFF),
                    Sea = true, SeaColor = Rgb(0x1F, 0x63, 0xC6),
                    Clouds = true,
                    Far = Rgb(0x4F, 0x8F, 0x6A), Near = Color.Empty,
                    GrassLight = Rgb(0xF0, 0xDC, 0xA6), GrassDark = Rgb(0xE6, 0xCF, 0x94),
                    RumbleLight = Rgb(0xF4, 0xF4, 0xF4), RumbleDark = Rgb(0xE0, 0x24, 0x24),
                    RoadLight = Rgb(0x9A, 0x9A, 0x9E), RoadDark = Rgb(0x92, 0x92, 0x96),
                    Lane = Rgb(0xF4, 0xF4, 0xF4),
                    Fog = Rgb(0xC8, 0xE6, 0xFF),
                    Foliage = Rgb(0x2E, 0x9A, 0x3A),
                    Scenery = new[] { RacerSprite.Palm, RacerSprite.Bush, RacerSprite.Palm, RacerSprite.Billboard },
                    Rows = RacerSprite.Palm,
                },
                new RacerTheme
                {
                    Name = "SUNSET COAST",
                    SkyTop = Rgb(0x3A, 0x1F, 0x6B), SkyBottom = Rgb(0xFF, 0x9A, 0x4A),
                    Sun = true, SunColor = Rgb(0xFF, 0xD2, 0x4A),
                    Far = Rgb(0x6A, 0x2E, 0x5E), Near = Rgb(0x2C, 0x3D, 0x2C),
                    GrassLight = Rgb(0x4C, 0x8C, 0x3A), GrassDark = Rgb(0x44, 0x80, 0x34),
                    RumbleLight = Rgb(0xF4, 0xF4, 0xF4), RumbleDark = Rgb(0xE0, 0x24, 0x24),
                    RoadLight = Rgb(0x7E, 0x78, 0x80), RoadDark = Rgb(0x76, 0x70, 0x78),
                    Lane = Rgb(0xF4, 0xF4, 0xF4),
                    Fog = Rgb(0xFF, 0xB0, 0x70),
                    Foliage = Rgb(0x2E, 0x7A, 0x34),
                    Scenery = new[] { RacerSprite.Palm, RacerSprite.Lamp, RacerSprite.Bush, RacerSprite.Billboard },
                    Rows = RacerSprite.Lamp,
                },
                new RacerTheme
                {
                    Name = "DESERT",
                    SkyTop = Rgb(0x4A, 0x9C, 0xE8), SkyBottom = Rgb(0xFF, 0xE2, 0xA8),
                    Far = Rgb(0xC0, 0x6A, 0x3A), Near = Rgb(0xD8, 0x90, 0x50),
                    GrassLight = Rgb(0xE8, 0xB0, 0x68), GrassDark = Rgb(0xDE, 0xA4, 0x5C),
                    RumbleLight = Rgb(0xF4, 0xF4, 0xF4), RumbleDark = Rgb(0xE0, 0x24, 0x24),
                    RoadLight = Rgb(0x8C, 0x84, 0x7C), RoadDark = Rgb(0x84, 0x7C, 0x74),
                    Lane = Rgb(0xF4, 0xF4, 0xF4),
                    Fog = Rgb(0xFF, 0xE4, 0xB8),
                    Foliage = Rgb(0x6A, 0x8A, 0x3A),
                    Scenery = new[] { RacerSprite.Cactus, RacerSprite.Rock, RacerSprite.Cactus, RacerSprite.Billboard },
                },
                new RacerTheme
                {
                    Name = "ALPINE PASS",
                    SkyTop = Rgb(0x3D, 0x7D, 0xD8), SkyBottom = Rgb(0xD4, 0xEC, 0xFF),
                    Clouds = true,
                    Far = Rgb(0x6C, 0x7C, 0x9C), FarCap = Rgb(0xF4, 0xF8, 0xFF), Near = Rgb(0x2E, 0x6A, 0x3A),
                    GrassLight = Rgb(0x3E, 0x9A, 0x46), GrassDark = Rgb(0x36, 0x8C, 0x3E),
                    RumbleLight = Rgb(0xF4, 0xF4, 0xF4), RumbleDark = Rgb(0xE0, 0x24, 0x24),
                    RoadLight = Rgb(0x88, 0x88, 0x90), RoadDark = Rgb(0x80, 0x80, 0x88),
                    Lane = Rgb(0xF4, 0xF4, 0xF4),
                    Fog = Rgb(0xDD, 0xF0, 0xFF),
                    Foliage = Rgb(0x2E, 0x8A, 0x3A),
                    Scenery = new[] { RacerSprite.Pine, RacerSprite.Pine, RacerSprite.Rock, RacerSprite.Bush },
                },
                new RacerTheme
                {
                    Name = "AUTUMN ROAD",
                    SkyTop = Rgb(0x2A, 0x1E, 0x5A), SkyBottom = Rgb(0xF0, 0x7C, 0x6A),
                    Sun = true, SunColor = Rgb(0xFF, 0x80, 0x50),
                    Far = Rgb(0x50, 0x2E, 0x60), Near = Rgb(0x7A, 0x3A, 0x2A),
                    GrassLight = Rgb(0x9C, 0x7A, 0x34), GrassDark = Rgb(0x90, 0x70, 0x2E),
                    RumbleLight = Rgb(0xF4, 0xF4, 0xF4), RumbleDark = Rgb(0xE0, 0x24, 0x24),
                    RoadLight = Rgb(0x74, 0x70, 0x7C), RoadDark = Rgb(0x6C, 0x68, 0x74),
                    Lane = Rgb(0xF4, 0xF4, 0xF4),
                    Fog = Rgb(0xE8, 0x8A, 0x78),
                    Foliage = Rgb(0xE0, 0x7A, 0x28),
                    Scenery = new[] { RacerSprite.Tree, RacerSprite.Tree, RacerSprite.Lamp, RacerSprite.Billboard },
                },
            };

            for (int i = 0; i < t.Length; i++)
            {
                var rng = new Random(7919 * (i + 1));
                if (t[i].Sea)
                    t[i].FarHeights = Silhouette(rng, 3, 7, -3);     // 海上的小島：大部分是 0
                else if (i == 2)
                    t[i].FarHeights = Mesas(rng);
                else
                    t[i].FarHeights = Silhouette(rng, 9, 8, 0);
                t[i].NearHeights = Silhouette(rng, 3, 3, 0);
            }
            return t;
        }

        private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

        // 幾個整數頻率的 sin 疊起來，週期剛好是 BG_PERIOD，左右接得起來
        private static int[] Silhouette(Random rng, double baseH, double amp, double floor)
        {
            var h = new int[BG_PERIOD];
            int[] freq = { 1, 2, 3, 5, 8 };
            double[] phase = new double[freq.Length];
            double[] a = new double[freq.Length];
            for (int k = 0; k < freq.Length; k++)
            {
                phase[k] = rng.NextDouble() * Math.PI * 2;
                a[k] = amp / (k + 1) * (0.6 + rng.NextDouble() * 0.8);
            }
            for (int x = 0; x < BG_PERIOD; x++)
            {
                double v = baseH;
                for (int k = 0; k < freq.Length; k++)
                    v += a[k] * Math.Sin(2 * Math.PI * freq[k] * x / BG_PERIOD + phase[k]);
                h[x] = (int)Math.Round(Math.Max(floor, v));
                if (h[x] < 0) h[x] = 0;
            }
            return h;
        }

        // 沙漠的平頂台地
        private static int[] Mesas(Random rng)
        {
            var h = new int[BG_PERIOD];
            for (int x = 0; x < BG_PERIOD; x++) h[x] = 2;
            int pos = 0;
            while (pos < BG_PERIOD - 20)
            {
                pos += rng.Next(8, 30);
                int width = rng.Next(14, 40);
                int height = rng.Next(8, 18);
                for (int x = 0; x < width && pos + x < BG_PERIOD; x++)
                {
                    int edge = Math.Min(x, width - 1 - x);
                    int v = edge < 3 ? height * (edge + 1) / 4 : height;
                    h[pos + x] = Math.Max(h[pos + x], v);
                }
                pos += width;
            }
            return h;
        }
    }

    /// <summary>
    /// 一整場比賽的賽道：好幾關接在一起，每關結尾有檢查點，最後一關結尾是終點。
    /// 做法跟一般的 pseudo-3D 賽車一樣：路是一串等長的小段，每段有彎度跟高度。
    /// </summary>
    internal sealed class RacerTrack
    {
        public const double SEG_LEN = 200;
        public const int RUMBLE_LEN = 3;
        public const int START_GATE = 30;          // 起跑門在第幾段（玩家車子在第 8 段左右）
        public const double CAR_SPEED_MIN = 0.32;   // 路上車子的速度，相對極速
        public const double CAR_SPEED_MAX = 0.58;

        public readonly List<RacerSegment> Segments = new List<RacerSegment>();
        public readonly List<RacerCar> Cars = new List<RacerCar>();
        public int[] StageStart;     // 每一關第一段的 index
        public int[] Checkpoint;     // 每一關的檢查點（最後一個是終點）在哪一段

        public double Length => Segments.Count * SEG_LEN;

        private readonly Random rng;
        private double lastY;
        private int curStage;

        private static readonly Color[] CarColors =
        {
            Color.FromArgb(0xF0, 0xF0, 0xF0), Color.FromArgb(0x30, 0x60, 0xD0), Color.FromArgb(0xF0, 0xC8, 0x20),
            Color.FromArgb(0x30, 0xA0, 0x50), Color.FromArgb(0x30, 0x30, 0x38), Color.FromArgb(0xE0, 0x70, 0x20),
            Color.FromArgb(0x80, 0x40, 0xB0), Color.FromArgb(0x60, 0xC0, 0xE0),
        };

        private RacerTrack(int seed)
        {
            rng = new Random(seed);
        }

        public static double SpriteWidth(RacerSprite s)
        {
            switch (s)
            {
                case RacerSprite.Palm: return 1100;
                case RacerSprite.Tree: return 1300;
                case RacerSprite.Pine: return 900;
                case RacerSprite.Bush: return 900;
                case RacerSprite.Cactus: return 500;
                case RacerSprite.Rock: return 1200;
                case RacerSprite.Billboard: return 1800;
                case RacerSprite.Lamp: return 500;
                default: return 800;
            }
        }

        public static double SpriteHeight(RacerSprite s)
        {
            switch (s)
            {
                case RacerSprite.Palm: return 2400;
                case RacerSprite.Tree: return 1900;
                case RacerSprite.Pine: return 2000;
                case RacerSprite.Bush: return 500;
                case RacerSprite.Cactus: return 1100;
                case RacerSprite.Rock: return 800;
                case RacerSprite.Billboard: return 1300;
                case RacerSprite.Lamp: return 2000;
                default: return 800;
            }
        }

        /// <summary>撞得到的寬度佔圖寬的比例（椰子樹只有樹幹撞得到）</summary>
        public static double SpriteHitRatio(RacerSprite s)
        {
            switch (s)
            {
                case RacerSprite.Palm: return 0.25;
                case RacerSprite.Tree: return 0.3;
                case RacerSprite.Pine: return 0.35;
                case RacerSprite.Lamp: return 0.3;
                case RacerSprite.Bush: return 0.7;
                default: return 0.85;
            }
        }

        public static double CarWidth(RacerCarType t)
        {
            switch (t)
            {
                case RacerCarType.Beetle: return 720;
                case RacerCarType.Truck: return 900;
                default: return 820;
            }
        }

        public static double CarHeight(RacerCarType t)
        {
            switch (t)
            {
                case RacerCarType.Beetle: return 520;
                case RacerCarType.Truck: return 1050;
                default: return 440;
            }
        }

        #region 俯視地圖

        private const double MAP_TURN = 0.0016;   // 每段的彎度換算成俯視圖上轉幾弧度

        private PointF[] mapPoints;
        private RectangleF mapBounds;

        /// <summary>每一段在俯視地圖上的位置（任意單位），彎度累加出來的</summary>
        public PointF[] MapPoints
        {
            get
            {
                if (mapPoints == null) BuildMap();
                return mapPoints;
            }
        }

        /// <summary>起點到終點的範圍（不含終點後滑行用的直路）</summary>
        public RectangleF MapBounds
        {
            get
            {
                if (mapPoints == null) BuildMap();
                return mapBounds;
            }
        }

        public int GoalIndex => Checkpoint[Checkpoint.Length - 1];

        private void BuildMap()
        {
            var pts = new PointF[Segments.Count];
            double heading = -Math.PI / 2, x = 0, y = 0;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new PointF((float)x, (float)y);
                if (i <= GoalIndex)
                {
                    minX = Math.Min(minX, pts[i].X); maxX = Math.Max(maxX, pts[i].X);
                    minY = Math.Min(minY, pts[i].Y); maxY = Math.Max(maxY, pts[i].Y);
                }
                heading += Segments[i].Curve * MAP_TURN;
                x += Math.Cos(heading);
                y += Math.Sin(heading);
            }
            mapBounds = new RectangleF(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            mapPoints = pts;
        }

        #endregion

        public RacerSegment SegmentAt(double z)
        {
            int n = Segments.Count;
            int i = (int)Math.Floor(z / SEG_LEN) % n;
            if (i < 0) i += n;
            return Segments[i];
        }

        public static RacerTrack Build(int seed, int stages, int stageSegs, int carsPerStage, double maxSpeed)
        {
            var t = new RacerTrack(seed);
            t.StageStart = new int[stages];
            t.Checkpoint = new int[stages];

            for (int s = 0; s < stages; s++)
            {
                t.curStage = s;
                t.StageStart[s] = t.Segments.Count;
                if (s == 0)
                    t.AddStraight(50);

                double difficulty = 0.8 + 0.1 * s;
                while (t.Segments.Count - t.StageStart[s] < stageSegs)
                    t.AddRandomSection(difficulty);

                // 檢查點前把路拉平、拉直
                t.AddRoad(25, 25, 25, 0, -t.lastY / SEG_LEN);
                t.AddStraight(20);

                t.Checkpoint[s] = t.Segments.Count;
                if (s < stages - 1)
                {
                    // 過了檢查點就是下一關的配色
                    t.curStage = s + 1;
                    t.AddStraight(30);
                }
                else
                {
                    // 終點後面留一大段直路，衝線後滑行用，也避免畫到賽道開頭
                    t.AddStraight(400);
                }
            }

            t.Segments[START_GATE].Gate = RacerGate.Start;
            for (int s = 0; s < stages; s++)
                t.Segments[t.Checkpoint[s]].Gate = s == stages - 1 ? RacerGate.Goal : RacerGate.Checkpoint;

            t.PlaceScenery();
            t.SpawnCars(stages, carsPerStage, maxSpeed);
            return t;
        }

        #region 路

        private void AddSegment(double curve, double y)
        {
            int n = Segments.Count;
            Segments.Add(new RacerSegment
            {
                Index = n,
                Y1 = lastY,
                Y2 = y,
                Curve = curve,
                Dark = (n / RUMBLE_LEN) % 2 == 1,
                Stage = curStage,
            });
            lastY = y;
        }

        private void AddRoad(int enter, int hold, int leave, double curve, double hill)
        {
            double startY = lastY;
            double endY = startY + hill * SEG_LEN;
            double total = enter + hold + leave;
            for (int n = 0; n < enter; n++)
                AddSegment(EaseIn(0, curve, (double)n / enter), EaseInOut(startY, endY, n / total));
            for (int n = 0; n < hold; n++)
                AddSegment(curve, EaseInOut(startY, endY, (enter + n) / total));
            for (int n = 0; n < leave; n++)
                AddSegment(EaseInOut(curve, 0, (double)n / leave), EaseInOut(startY, endY, (enter + hold + n) / total));
        }

        private void AddStraight(int n)
        {
            AddRoad(n / 4, n / 2, n - n / 4 - n / 2, 0, 0);
        }

        private void AddRandomSection(double difficulty)
        {
            int len = rng.Next(25, 70);
            double[] curves = { 2, 3.5, 5 };
            double curve = curves[rng.Next(curves.Length)] * difficulty * (rng.Next(2) == 0 ? -1 : 1);

            double hill = rng.Next(3) == 0 ? 0 : rng.Next(-25, 26);
            // 一直往上或往下太久就拉回來
            double level = lastY / SEG_LEN;
            if (Math.Abs(level) > 50)
                hill = -level * (0.4 + rng.NextDouble() * 0.4);

            double r = rng.NextDouble();
            if (r < 0.18)
            {
                AddRoad(len / 3, len, len / 3, 0, hill);
            }
            else if (r < 0.65)
            {
                AddRoad(len / 2, len, len / 2, curve, hill);
            }
            else if (r < 0.85)
            {
                // S 彎
                AddRoad(len / 3, len / 2, len / 3, curve, hill / 2);
                AddRoad(len / 3, len / 2, len / 3, -curve, -hill / 2);
            }
            else
            {
                // 長長的緩彎
                AddRoad(40, 90, 40, curve * 0.45, hill);
            }
        }

        private static double EaseIn(double a, double b, double p) => a + (b - a) * p * p;

        private static double EaseInOut(double a, double b, double p) => a + (b - a) * (-Math.Cos(p * Math.PI) / 2 + 0.5);

        #endregion

        #region 景物、車子

        private bool NearGate(int i)
        {
            for (int s = 0; s < Checkpoint.Length; s++)
                if (Math.Abs(i - Checkpoint[s]) < 4) return true;
            return Math.Abs(i - START_GATE) < 4;
        }

        private void AddSprite(int i, RacerSprite type, double offset)
        {
            var seg = Segments[i];
            if (seg.Sprites == null) seg.Sprites = new List<RacerSpriteItem>();
            seg.Sprites.Add(new RacerSpriteItem { Type = type, Offset = offset, Variant = rng.Next(3) });
        }

        private void PlaceScenery()
        {
            for (int i = 20; i < Segments.Count - 200; i++)
            {
                if (NearGate(i)) continue;
                RacerTheme theme = RacerTheme.All[Segments[i].Stage];

                // 整齊的行道樹 / 路燈，一段有一段沒有
                if (theme.Rows.HasValue && (i / 160) % 2 == 0 && i % 8 == 0)
                {
                    AddSprite(i, theme.Rows.Value, -1.2);
                    AddSprite(i, theme.Rows.Value, 1.2);
                    continue;
                }

                if (i % 220 == 110)
                {
                    AddSprite(i, RacerSprite.Billboard, rng.Next(2) == 0 ? -1.15 : 1.15);
                    continue;
                }

                if (rng.NextDouble() < 0.14)
                {
                    RacerSprite type = theme.Scenery[rng.Next(theme.Scenery.Length)];
                    double side = rng.Next(2) == 0 ? -1 : 1;
                    AddSprite(i, type, side * (1.2 + rng.NextDouble() * 2.4));
                }
            }
        }

        private void SpawnCars(int stages, int carsPerStage, double maxSpeed)
        {
            double[] lanes = { -0.66, 0, 0.66 };
            for (int s = 0; s < stages; s++)
            {
                int from = StageStart[s] + (s == 0 ? 60 : 10);
                int to = Checkpoint[s] - 10;
                for (int k = 0; k < carsPerStage; k++)
                {
                    int i = rng.Next(from, to);
                    double r = rng.NextDouble();
                    var car = new RacerCar
                    {
                        Type = r < 0.55 ? RacerCarType.Coupe : r < 0.8 ? RacerCarType.Beetle : RacerCarType.Truck,
                        Offset = lanes[rng.Next(lanes.Length)] + (rng.NextDouble() - 0.5) * 0.1,
                        Z = (i + rng.NextDouble()) * SEG_LEN,
                        Speed = maxSpeed * (CAR_SPEED_MIN + rng.NextDouble() * (CAR_SPEED_MAX - CAR_SPEED_MIN)),
                        Color = CarColors[rng.Next(CarColors.Length)],
                    };
                    if (car.Type == RacerCarType.Truck)
                        car.Speed *= 0.85;
                    car.Segment = Segments[i];
                    if (car.Segment.Cars == null) car.Segment.Cars = new List<RacerCar>();
                    car.Segment.Cars.Add(car);
                    Cars.Add(car);
                }
            }
        }

        /// <summary>路上的車往前開，換段時搬到新的段落</summary>
        public void UpdateCars(double dt)
        {
            foreach (RacerCar car in Cars)
            {
                car.Z += car.Speed * dt;
                if (car.Z >= Length) car.Z -= Length;
                RacerSegment seg = SegmentAt(car.Z);
                if (seg == car.Segment) continue;
                car.Segment.Cars.Remove(car);
                if (seg.Cars == null) seg.Cars = new List<RacerCar>();
                seg.Cars.Add(car);
                car.Segment = seg;
            }
        }

        #endregion
    }
}
