using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static MiniGame.RacerConst;

namespace MiniGame
{
    /// <summary>
    /// 賽車畫面（200x100）：背景 → 路面（遠到近）→ 景物、車子、對手（遠到近）→ 玩家，
    /// 最後交給呼叫端畫 HUD 與文字。單人版與 4P 版共用。呼叫端要自己鎖。
    /// </summary>
    internal sealed class RacerRenderer : IDisposable
    {
        private const double NEAR = 10;
        private const int DRAW_DIST = 150;                 // 往前畫幾段
        private const double FOG_DENSITY = 4.5;
        private const int LANES = 3;

        private static readonly Color TrunkColor = Color.FromArgb(0x8A, 0x5A, 0x2A);
        private static readonly Color PalmDark = Color.FromArgb(0x1E, 0x7A, 0x2E);
        private static readonly Color PalmLight = Color.FromArgb(0x3C, 0xB0, 0x40);
        private static readonly Color PineColor = Color.FromArgb(0x1C, 0x5A, 0x2C);
        private static readonly Color CactusColor = Color.FromArgb(0x3A, 0x8A, 0x3A);
        private static readonly Color RockColor = Color.FromArgb(0x8A, 0x7A, 0x6A);
        private static readonly Color PoleColor = Color.FromArgb(0xA0, 0xA0, 0xA8);
        private static readonly Color TireColor = Color.FromArgb(0x18, 0x18, 0x18);
        public static readonly Color HudYellow = Color.FromArgb(0xFF, 0xD8, 0x30);
        public static readonly Color HudBlue = Color.FromArgb(0x80, 0xC8, 0xFF);
        public static readonly Color Grey = Color.FromArgb(0xC0, 0xC0, 0xC0);

        private static readonly Color[][] BillboardColors =
        {
            new[] { Color.FromArgb(0xF4, 0xF4, 0xF4), Color.FromArgb(0xE0, 0x20, 0x20) },
            new[] { Color.FromArgb(0x20, 0x50, 0xC0), Color.FromArgb(0xFF, 0xD8, 0x30) },
            new[] { Color.FromArgb(0xFF, 0xC8, 0x20), Color.FromArgb(0x20, 0x20, 0x20) },
        };

        private readonly RacerSegment[] drawn = new RacerSegment[DRAW_DIST];
        private readonly Dictionary<int, Font> fontCache = new Dictionary<int, Font>();
        private readonly Random rng = new Random();
        private readonly SolidBrush brush = new SolidBrush(Color.Black);
        private readonly StringFormat fmtCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        public readonly Font Small = new Font("Arial", 8, FontStyle.Bold, GraphicsUnit.Pixel);
        public readonly Font Mid = new Font("Arial", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        public readonly Font Big = new Font("Arial", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        public readonly Font Title = new Font("Arial", 18, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel);
        public readonly Font Huge = new Font("Arial", 26, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel);

        private Graphics gfx;
        private RacerTrack track;
        private RacerDriver d;
        private IList<RacerRival> rivals;
        private Color carColor;
        private double curFog = 1;
        private Color curFogColor;

        public void Dispose()
        {
            brush.Dispose();
            fmtCenter.Dispose();
            Small.Dispose();
            Mid.Dispose();
            Big.Dispose();
            Title.Dispose();
            Huge.Dispose();
            foreach (Font f in fontCache.Values) f.Dispose();
            fontCache.Clear();
        }

        /// <summary>畫一個玩家看出去的畫面，overlay 用來畫 HUD 與文字</summary>
        public Bitmap Render(RacerTrack track, RacerDriver driver, Color carColor, IList<RacerRival> rivals, Action<RacerRenderer> overlay)
        {
            this.track = track;
            this.d = driver;
            this.carColor = carColor;
            this.rivals = rivals;

            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                gfx = g;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.Clear(Color.Black);

                if (d.ShakeTimer > 0)
                    g.TranslateTransform(rng.Next(-1, 2), rng.Next(-1, 2));

                curFog = 1;
                if (d.BgFade < 1)
                    DrawBackground(RacerTheme.All[d.BgFrom], 255);
                DrawBackground(RacerTheme.All[d.BgTo], d.BgFade < 1 ? (int)(d.BgFade * 255) : 255);
                DrawRoad();

                g.ResetTransform();
                curFog = 1;
                overlay?.Invoke(this);
                gfx = null;
            }
            this.rivals = null;
            return bmp;
        }

        #region 基本圖形

        private Color F(Color c) => curFog >= 1 ? c : Mix(curFogColor, c, curFog);

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static Color Alpha(Color c, int a) => Color.FromArgb(Math.Max(0, Math.Min(255, a)), c);

        public static Color Darker(Color c, double f = 0.7) => Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));

        public static Color Lighter(Color c, double f = 0.4) => Mix(c, Color.White, f);

        private static float Px(double v) => (float)Math.Max(-20000, Math.Min(20000, v));

        public void Rect(Color c, double x, double y, double w, double h)
        {
            if (w <= 0 || h <= 0) return;
            brush.Color = F(c);
            gfx.FillRectangle(brush, Px(x), Px(y), Px(w), Px(h));
        }

        private void Ellipse(Color c, double x, double y, double w, double h)
        {
            if (w <= 0 || h <= 0) return;
            brush.Color = F(c);
            gfx.FillEllipse(brush, Px(x), Px(y), Px(w), Px(h));
        }

        private void Poly(Color c, params double[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new PointF(Px(xy[i * 2]), Px(xy[i * 2 + 1]));
            brush.Color = F(c);
            gfx.FillPolygon(brush, pts);
        }

        private Font SizedFont(double px)
        {
            int size = Math.Max(4, Math.Min(40, (int)px));
            if (!fontCache.TryGetValue(size, out Font f))
            {
                f = new Font("Arial", size, FontStyle.Bold, GraphicsUnit.Pixel);
                fontCache[size] = f;
            }
            return f;
        }

        /// <summary>有陰影的文字，align 決定 x 是左、中、右</summary>
        public void Text(string s, Font font, Color c, float x, float y, StringAlignment align = StringAlignment.Near)
        {
            using (var fmt = new StringFormat { Alignment = align })
            using (var b = new SolidBrush(c))
            {
                gfx.DrawString(s, font, Brushes.Black, x + 1, y + 1, fmt);
                gfx.DrawString(s, font, b, x, y, fmt);
            }
        }

        /// <summary>置中在一列裡的文字（整個畫面寬）</summary>
        public void TextIn(string s, Font font, Color c, float y, float h)
        {
            using (var b = new SolidBrush(c))
            {
                gfx.DrawString(s, font, Brushes.Black, new RectangleF(1, y + 1, W, h), fmtCenter);
                gfx.DrawString(s, font, b, new RectangleF(0, y, W, h), fmtCenter);
            }
        }

        /// <summary>半透明黑底，墊在文字後面</summary>
        public void Band(float y, float h)
        {
            brush.Color = Color.FromArgb(150, 0, 0, 0);
            gfx.FillRectangle(brush, 0, y, W, h);
        }

        /// <summary>車子身上的大字訊息（CHECKPOINT 等），GO! 以外會閃</summary>
        public void Message(RacerDriver driver, double now)
        {
            if (driver.MsgTimer <= 0 || driver.Msg1 == null) return;
            bool blink = driver.Msg1 != "GO!" && (int)(now * 5) % 2 == 1;
            if (!blink)
                TextIn(driver.Msg1, Title, HudYellow, 26, 24);
            if (driver.Msg2 != null)
                TextIn(driver.Msg2, Mid, Color.White, 50, 14);
        }

        /// <summary>右上角的關卡與進度條</summary>
        public void StageBar(string label, double progress)
        {
            Text(label, Small, HudBlue, W - 2, 0, StringAlignment.Far);
            Rect(Color.Black, W - 34, 11, 32, 4);
            Rect(Color.FromArgb(0x50, 0x50, 0x50), W - 33, 12, 30, 2);
            Rect(HudYellow, W - 33, 12, 30 * progress, 2);
        }

        /// <summary>左下角的速度表</summary>
        public void Speedo(double kmh)
        {
            Text(((int)kmh).ToString(), Big, HudYellow, 32, H - 17, StringAlignment.Far);
            Text("km/h", Small, Color.White, 33, H - 11);
        }

        /// <summary>右上角進度條下面，剩幾次加速，加速中會閃</summary>
        public void BoostPips(RacerDriver driver, double now)
        {
            bool flash = driver.Boosting && (int)(now * 8) % 2 == 0;
            for (int i = 0; i < RacerConst.BOOSTS; i++)
            {
                float x = W - 9 - i * 9;
                bool left = i < driver.Boosts;
                Rect(Color.Black, x - 1, 17, 9, 6);
                Rect(left ? (flash ? Color.White : Color.FromArgb(0x30, 0xD0, 0xFF)) : Color.FromArgb(0x40, 0x40, 0x40), x, 18, 7, 4);
            }
        }

        #endregion

        #region 背景

        private static double Wrap(double v, double period)
        {
            v %= period;
            return v < 0 ? v + period : v;
        }

        private void DrawBackground(RacerTheme t, int alpha)
        {
            Graphics g = gfx;
            using (var sky = new LinearGradientBrush(new Rectangle(0, -1, W, HORIZON + 2),
                       Alpha(t.SkyTop, alpha), Alpha(t.SkyBottom, alpha), LinearGradientMode.Vertical))
            {
                g.FillRectangle(sky, 0, 0, W, HORIZON + 1);
            }

            if (t.Sun)
            {
                double sx = Wrap(150 - d.FarOffset * 0.5, W + 60) - 30;
                double sy = HORIZON - 12;
                brush.Color = Alpha(t.SunColor, alpha / 3);
                g.FillEllipse(brush, (float)(sx - 15), (float)(sy - 15), 30, 30);
                brush.Color = Alpha(t.SunColor, alpha);
                g.FillEllipse(brush, (float)(sx - 10), (float)(sy - 10), 20, 20);
            }

            if (t.Clouds)
            {
                brush.Color = Alpha(Color.White, alpha * 3 / 4);
                for (int i = 0; i < 5; i++)
                {
                    double cx = Wrap(i * 53 + 17 - d.FarOffset * 0.6, W + 60) - 30;
                    double cy = 5 + (i * 7) % 14;
                    g.FillEllipse(brush, (float)cx, (float)cy, 22, 6);
                    g.FillEllipse(brush, (float)cx + 5, (float)cy - 3, 12, 6);
                }
            }

            int baseline = t.Sea ? HORIZON - 5 : HORIZON;
            DrawSilhouette(t.FarHeights, d.FarOffset, baseline, Alpha(t.Far, alpha));
            if (t.FarCap != Color.Empty)
            {
                brush.Color = Alpha(t.FarCap, alpha);
                for (int x = 0; x < W; x++)
                {
                    int h = t.FarHeights[(int)Wrap(x + d.FarOffset, RacerTheme.BG_PERIOD)];
                    if (h > 11)
                        g.FillRectangle(brush, x, baseline - h, 1, Math.Min(3, h - 11));
                }
            }

            if (t.Sea)
            {
                brush.Color = Alpha(t.SeaColor, alpha);
                g.FillRectangle(brush, 0, HORIZON - 5, W, 6);
                brush.Color = Alpha(Lighter(t.SeaColor, 0.35), alpha);
                for (int i = 0; i < 9; i++)
                {
                    double wx = Wrap(i * 29 - d.FarOffset * 1.5, W + 20) - 10;
                    g.FillRectangle(brush, (float)wx, HORIZON - 3 + (i % 3), 6, 1);
                }
            }

            if (t.Near != Color.Empty)
                DrawSilhouette(t.NearHeights, d.NearOffset, HORIZON, Alpha(t.Near, alpha));

            // 地平線以下先鋪一層遠處的地面顏色，下坡時看得到
            brush.Color = Alpha(Mix(t.Fog, t.GrassDark, 0.35), alpha);
            g.FillRectangle(brush, 0, HORIZON, W, H - HORIZON);
        }

        private void DrawSilhouette(int[] heights, double offset, int baseline, Color c)
        {
            var pts = new PointF[W / 2 + 3];
            int k = 0;
            for (int x = 0; x <= W; x += 2)
                pts[k++] = new PointF(x, baseline - heights[(int)Wrap(x + offset, RacerTheme.BG_PERIOD)]);
            pts[k++] = new PointF(W, baseline + 1);
            pts[k] = new PointF(0, baseline + 1);
            brush.Color = c;
            gfx.FillPolygon(brush, pts);
        }

        #endregion

        #region 路

        private static void Project(double wy, double wz, double camX, double camY, double camZ,
            out double sx, out double sy, out double sw, out double scale, out double cz)
        {
            cz = wz - camZ;
            scale = 1.0 / Math.Max(cz, NEAR);
            sx = W / 2.0 - scale * camX * K;
            sy = HORIZON - scale * (wy - camY) * K;
            sw = scale * ROAD_W * K;
        }

        private void DrawRoad()
        {
            List<RacerSegment> segs = track.Segments;
            int count = segs.Count;
            double position = d.Position;
            RacerSegment baseSeg = track.SegmentAt(position);
            double basePct = Wrap(position, SEG_LEN) / SEG_LEN;
            RacerSegment playerSeg = track.SegmentAt(d.Z);
            double playerPct = Wrap(d.Z, SEG_LEN) / SEG_LEN;
            double camY = playerSeg.Y1 + (playerSeg.Y2 - playerSeg.Y1) * playerPct + CAM_H;

            // 1. 由近到遠投影，同時算出每段被前面山坡擋到哪裡（Clip）
            double maxy = H;
            double x = 0, dx = -(baseSeg.Curve * basePct);
            for (int n = 0; n < DRAW_DIST; n++)
            {
                int idx = baseSeg.Index + n;
                RacerSegment seg = segs[idx % count];
                double camZ = position - (idx >= count ? track.Length : 0);
                double camX = d.X * ROAD_W - x;

                seg.Fog = Math.Exp(-Math.Pow((double)n / DRAW_DIST, 2) * FOG_DENSITY);
                seg.Clip = maxy;
                Project(seg.Y1, seg.Index * SEG_LEN, camX, camY, camZ, out seg.X1, out seg.ScreenY1, out seg.W1, out seg.Scale1, out seg.CamZ1);
                Project(seg.Y2, (seg.Index + 1) * SEG_LEN, camX - dx, camY, camZ, out seg.X2, out seg.ScreenY2, out seg.W2, out seg.Scale2, out _);
                x += dx;
                dx += seg.Curve;

                seg.Visible = seg.CamZ1 > NEAR && seg.ScreenY2 < seg.ScreenY1 && seg.ScreenY2 < maxy;
                if (seg.Visible)
                    maxy = Math.Min(maxy, seg.ScreenY2);
                drawn[n] = seg;
            }

            // 2. 路面由遠到近畫，近的蓋掉遠的
            for (int n = DRAW_DIST - 1; n >= 0; n--)
                if (drawn[n].Visible)
                    DrawSegment(drawn[n]);

            // 3. 景物、車子、對手、玩家由遠到近
            bool playerDrawn = false;
            for (int n = DRAW_DIST - 1; n >= 1; n--)
            {
                RacerSegment seg = drawn[n];
                if (seg.CamZ1 > NEAR && seg.Clip > 0)
                {
                    RacerTheme theme = RacerTheme.All[seg.Stage];
                    curFog = seg.Fog;
                    curFogColor = theme.Fog;
                    gfx.SetClip(new RectangleF(-10, -100, W + 20, (float)seg.Clip + 100));

                    if (seg.Sprites != null)
                        foreach (RacerSpriteItem sp in seg.Sprites)
                            DrawScenery(seg, sp, theme);
                    if (seg.Cars != null)
                        foreach (RacerCar car in seg.Cars)
                            DrawTraffic(seg, car);
                    if (rivals != null)
                        foreach (RacerRival r in rivals)
                            if (track.SegmentAt(r.Z) == seg)
                                DrawRival(seg, r);
                    if (seg.Gate != RacerGate.None)
                        DrawGate(seg);

                    gfx.ResetClip();
                    curFog = 1;
                }

                if (seg == playerSeg && !playerDrawn)
                {
                    DrawPlayer();
                    playerDrawn = true;
                }
            }
            if (!playerDrawn)
                DrawPlayer();
        }

        private void DrawSegment(RacerSegment seg)
        {
            RacerTheme t = RacerTheme.All[seg.Stage];
            curFog = seg.Fog;
            curFogColor = t.Fog;

            double x1 = seg.X1, y1 = Math.Round(seg.ScreenY1), w1 = seg.W1;
            double x2 = seg.X2, y2 = Math.Round(seg.ScreenY2), w2 = seg.W2;
            double r1 = w1 / 6, r2 = w2 / 6;
            double l1 = w1 / 32, l2 = w2 / 32;

            Rect(seg.Dark ? t.GrassDark : t.GrassLight, 0, y2, W, Math.Min(H, y1) - y2 + 1);

            Color rumble = seg.Dark ? t.RumbleDark : t.RumbleLight;
            Poly(rumble, x1 - w1 - r1, y1, x1 - w1, y1, x2 - w2, y2, x2 - w2 - r2, y2);
            Poly(rumble, x1 + w1 + r1, y1, x1 + w1, y1, x2 + w2, y2, x2 + w2 + r2, y2);
            Poly(seg.Dark ? t.RoadDark : t.RoadLight, x1 - w1, y1, x1 + w1, y1, x2 + w2, y2, x2 - w2, y2);

            if (!seg.Dark)
            {
                double lw1 = w1 * 2 / LANES, lw2 = w2 * 2 / LANES;
                double lx1 = x1 - w1 + lw1, lx2 = x2 - w2 + lw2;
                for (int lane = 1; lane < LANES; lane++)
                {
                    Poly(t.Lane, lx1 - l1 / 2, y1, lx1 + l1 / 2, y1, lx2 + l2 / 2, y2, lx2 - l2 / 2, y2);
                    lx1 += lw1;
                    lx2 += lw2;
                }
            }
            curFog = 1;
        }

        #endregion

        #region 景物

        private void DrawScenery(RacerSegment seg, RacerSpriteItem sp, RacerTheme theme)
        {
            double scale = seg.Scale1;
            double w = RacerTrack.SpriteWidth(sp.Type) * scale * K;
            double h = RacerTrack.SpriteHeight(sp.Type) * scale * K;
            if (w < 0.8 || h < 0.8) return;

            double anchor = seg.X1 + scale * sp.Offset * ROAD_W * K;
            double x = sp.Offset < 0 ? anchor - w : anchor;
            double y = seg.ScreenY1 - h;
            if (x > W || x + w < 0) return;

            if (w < 3)
            {
                // 太遠了，一個色塊就好
                Color c = sp.Type == RacerSprite.Rock ? RockColor
                        : sp.Type == RacerSprite.Billboard ? BillboardColors[sp.Variant][0]
                        : sp.Type == RacerSprite.Lamp ? PoleColor
                        : sp.Type == RacerSprite.Cactus ? CactusColor
                        : theme.Foliage;
                Rect(c, x + w * 0.3, y, Math.Max(1, w * 0.4), h);
                return;
            }

            switch (sp.Type)
            {
                case RacerSprite.Palm: DrawPalm(x, y, w, h, sp.Variant); break;
                case RacerSprite.Tree: DrawTree(x, y, w, h, theme.Foliage); break;
                case RacerSprite.Pine: DrawPine(x, y, w, h); break;
                case RacerSprite.Bush: DrawBush(x, y, w, h, theme.Foliage); break;
                case RacerSprite.Cactus: DrawCactus(x, y, w, h); break;
                case RacerSprite.Rock: DrawRock(x, y, w, h); break;
                case RacerSprite.Billboard: DrawBillboard(x, y, w, h, sp.Variant); break;
                case RacerSprite.Lamp: DrawLamp(x, y, w, h, sp.Offset < 0); break;
            }
        }

        private void DrawPalm(double x, double y, double w, double h, int variant)
        {
            double bx = x + w * 0.5, by = y + h;
            double tx = x + w * (variant == 1 ? 0.62 : variant == 2 ? 0.5 : 0.4), ty = y + h * 0.22;
            double tb = Math.Max(1, w * 0.09), tt = Math.Max(1, w * 0.06);
            Poly(TrunkColor, bx - tb / 2, by, bx + tb / 2, by, tx + tt / 2, ty, tx - tt / 2, ty);

            double[,] leaves = { { -1.0, 0.6 }, { -0.85, -0.15 }, { -0.35, -0.55 }, { 0.35, -0.55 }, { 0.85, -0.15 }, { 1.0, 0.6 }, { -0.2, 0.75 }, { 0.3, 0.7 } };
            double t = Math.Max(1, h * 0.035);
            for (int i = 0; i < leaves.GetLength(0); i++)
            {
                double ex = tx + leaves[i, 0] * w * 0.5, ey = ty + leaves[i, 1] * h * 0.2;
                double mx = tx + leaves[i, 0] * w * 0.27, my = ty + leaves[i, 1] * h * 0.11 - h * 0.05;
                Poly(i % 2 == 0 ? PalmDark : PalmLight, tx, ty - t, mx, my - t, ex, ey, mx, my + t, tx, ty + t);
            }
            if (w > 10)
                Ellipse(Color.FromArgb(0x6A, 0x40, 0x18), tx - w * 0.06, ty, w * 0.12, w * 0.1);
        }

        private void DrawTree(double x, double y, double w, double h, Color foliage)
        {
            Rect(TrunkColor, x + w * 0.44, y + h * 0.5, w * 0.12, h * 0.5);
            Ellipse(Darker(foliage), x, y + h * 0.22, w * 0.62, h * 0.45);
            Ellipse(Darker(foliage), x + w * 0.38, y + h * 0.2, w * 0.62, h * 0.45);
            Ellipse(foliage, x + w * 0.14, y, w * 0.72, h * 0.52);
            Ellipse(Lighter(foliage, 0.25), x + w * 0.3, y + h * 0.06, w * 0.3, h * 0.18);
        }

        private void DrawPine(double x, double y, double w, double h)
        {
            Rect(TrunkColor, x + w * 0.44, y + h * 0.84, w * 0.12, h * 0.16);
            Poly(PineColor, x, y + h * 0.88, x + w, y + h * 0.88, x + w / 2, y + h * 0.4);
            Poly(PineColor, x + w * 0.1, y + h * 0.62, x + w * 0.9, y + h * 0.62, x + w / 2, y + h * 0.15);
            Poly(Lighter(PineColor, 0.12), x + w * 0.22, y + h * 0.38, x + w * 0.78, y + h * 0.38, x + w / 2, y);
        }

        private void DrawBush(double x, double y, double w, double h, Color foliage)
        {
            Ellipse(Darker(foliage), x, y + h * 0.25, w, h * 0.75);
            Ellipse(foliage, x + w * 0.18, y, w * 0.64, h * 0.7);
        }

        private void DrawCactus(double x, double y, double w, double h)
        {
            Rect(CactusColor, x + w * 0.38, y, w * 0.24, h);
            Rect(CactusColor, x + w * 0.06, y + h * 0.28, w * 0.18, h * 0.32);
            Rect(CactusColor, x + w * 0.06, y + h * 0.52, w * 0.34, h * 0.1);
            Rect(CactusColor, x + w * 0.76, y + h * 0.18, w * 0.18, h * 0.3);
            Rect(CactusColor, x + w * 0.6, y + h * 0.4, w * 0.34, h * 0.1);
            Rect(Lighter(CactusColor, 0.3), x + w * 0.42, y + h * 0.04, Math.Max(1, w * 0.06), h * 0.9);
        }

        private void DrawRock(double x, double y, double w, double h)
        {
            Poly(RockColor, x, y + h, x + w * 0.08, y + h * 0.4, x + w * 0.35, y + h * 0.05, x + w * 0.7, y, x + w * 0.95, y + h * 0.45, x + w, y + h);
            Poly(Lighter(RockColor, 0.3), x + w * 0.12, y + h * 0.45, x + w * 0.36, y + h * 0.12, x + w * 0.6, y + h * 0.1, x + w * 0.4, y + h * 0.5);
        }

        private void DrawBillboard(double x, double y, double w, double h, int variant)
        {
            Color[] c = BillboardColors[variant % BillboardColors.Length];
            Rect(PoleColor, x + w * 0.15, y + h * 0.6, Math.Max(1, w * 0.05), h * 0.4);
            Rect(PoleColor, x + w * 0.8, y + h * 0.6, Math.Max(1, w * 0.05), h * 0.4);
            double bh = h * 0.62;
            Rect(Color.FromArgb(0x30, 0x30, 0x30), x, y, w, bh);
            Rect(c[0], x + 1, y + 1, w - 2, bh - 2);
            if (bh >= 7)
            {
                Rect(c[1], x + 1, y + bh * 0.72, w - 2, bh * 0.14);
                using (var b = new SolidBrush(F(c[1])))
                {
                    // 上面大字 SUNSET，下面小字 ARCADE
                    gfx.DrawString("SUNSET", SizedFont(Math.Min(bh * 0.4, w * 0.18)), b, new RectangleF((float)x, (float)y, (float)w, (float)(bh * 0.46)), fmtCenter);
                    gfx.DrawString("ARCADE", SizedFont(Math.Min(bh * 0.22, w * 0.14)), b, new RectangleF((float)x, (float)(y + bh * 0.42), (float)w, (float)(bh * 0.3)), fmtCenter);
                }
            }
        }

        private void DrawLamp(double x, double y, double w, double h, bool armRight)
        {
            double pw = Math.Max(1, w * 0.1);
            double px = armRight ? x + w * 0.15 : x + w * 0.75;
            Rect(PoleColor, px, y + h * 0.06, pw, h * 0.94);
            double ax = armRight ? px : x + w * 0.1;
            Rect(PoleColor, ax, y + h * 0.05, w * 0.7, Math.Max(1, h * 0.025));
            double hx = armRight ? x + w * 0.72 : x;
            Rect(Color.FromArgb(0xFF, 0xF4, 0xC0), hx, y + h * 0.06, w * 0.28, Math.Max(1, h * 0.03));
        }

        #endregion

        #region 車子

        // 在某一段上的位置換算成畫面座標（車子底部中央）與比例
        private static void OnSegment(RacerSegment seg, double z, double offset, out double cx, out double bottom, out double scale)
        {
            double pct = Math.Max(0, Math.Min(1, (z - seg.Index * SEG_LEN) / SEG_LEN));
            scale = seg.Scale1 + (seg.Scale2 - seg.Scale1) * pct;
            cx = seg.X1 + (seg.X2 - seg.X1) * pct + scale * offset * ROAD_W * K;
            bottom = seg.ScreenY1 + (seg.ScreenY2 - seg.ScreenY1) * pct;
        }

        private void DrawTraffic(RacerSegment seg, RacerCar car)
        {
            OnSegment(seg, Wrap(car.Z, track.Length), car.Offset, out double cx, out double bottom, out double scale);
            double w = RacerTrack.CarWidth(car.Type) * scale * K;
            double h = RacerTrack.CarHeight(car.Type) * scale * K;
            if (w < 0.8) return;

            double x = cx - w / 2, y = bottom - h;
            if (x > W || x + w < 0) return;
            if (w < 4)
            {
                Rect(car.Color, x, y, w, h);
                return;
            }

            Rect(Color.FromArgb(0x30, 0x30, 0x30), x - w * 0.02, bottom - h * 0.06, w * 1.04, h * 0.08);
            Rect(TireColor, x + w * 0.04, bottom - h * 0.24, w * 0.17, h * 0.24);
            Rect(TireColor, x + w * 0.79, bottom - h * 0.24, w * 0.17, h * 0.24);

            Color tail = Color.FromArgb(0xE8, 0x20, 0x20);
            switch (car.Type)
            {
                case RacerCarType.Coupe:
                    Rect(car.Color, x, y + h * 0.4, w, h * 0.45);
                    Poly(Darker(car.Color, 0.8), x + w * 0.12, y + h * 0.42, x + w * 0.88, y + h * 0.42, x + w * 0.78, y, x + w * 0.22, y);
                    Poly(Color.FromArgb(0x30, 0x40, 0x60), x + w * 0.2, y + h * 0.37, x + w * 0.8, y + h * 0.37, x + w * 0.73, y + h * 0.08, x + w * 0.27, y + h * 0.08);
                    Rect(tail, x + w * 0.04, y + h * 0.5, w * 0.2, h * 0.12);
                    Rect(tail, x + w * 0.76, y + h * 0.5, w * 0.2, h * 0.12);
                    Rect(Color.FromArgb(0xF0, 0xF0, 0xE0), x + w * 0.42, y + h * 0.58, w * 0.16, h * 0.1);
                    Rect(Darker(car.Color, 0.6), x, y + h * 0.78, w, h * 0.08);
                    break;

                case RacerCarType.Beetle:
                    Ellipse(car.Color, x, y + h * 0.3, w, h * 0.6);
                    Ellipse(car.Color, x + w * 0.16, y, w * 0.68, h * 0.62);
                    Ellipse(Color.FromArgb(0x30, 0x40, 0x60), x + w * 0.3, y + h * 0.1, w * 0.4, h * 0.22);
                    Rect(tail, x + w * 0.1, y + h * 0.52, w * 0.1, h * 0.12);
                    Rect(tail, x + w * 0.8, y + h * 0.52, w * 0.1, h * 0.12);
                    Rect(PoleColor, x + w * 0.05, y + h * 0.8, w * 0.9, h * 0.06);
                    break;

                default:
                    Rect(Color.FromArgb(0xE8, 0xE2, 0xD4), x, y, w, h * 0.84);
                    Rect(car.Color, x, y + h * 0.32, w, h * 0.1);
                    Rect(Color.FromArgb(0x90, 0x90, 0x88), x + w * 0.495, y + h * 0.04, Math.Max(1, w * 0.01), h * 0.76);
                    Rect(Color.FromArgb(0x40, 0x40, 0x40), x, y + h * 0.8, w, h * 0.06);
                    Rect(tail, x + w * 0.02, y + h * 0.72, w * 0.1, h * 0.06);
                    Rect(tail, x + w * 0.88, y + h * 0.72, w * 0.1, h * 0.06);
                    break;
            }
        }

        // 其他玩家：跟自己同一台敞篷車，換顏色、依距離縮放
        private void DrawRival(RacerSegment seg, RacerRival r)
        {
            OnSegment(seg, r.Z, r.X, out double cx, out double bottom, out double scale);
            double s = scale * PLAYER_Z;          // 在 PLAYER_Z 時 = 1
            double w = PLAYER_CAR_W * s;
            if (w < 0.8 || cx + w < 0 || cx - w > W) return;
            if (w < 6)
            {
                Rect(r.Color, cx - w / 2, bottom - w * 0.5, w, w * 0.5);
                return;
            }

            GraphicsState saved = gfx.Save();
            gfx.TranslateTransform((float)cx, (float)bottom);
            gfx.ScaleTransform((float)s, (float)s);
            if (r.CrashTimer > 0)
            {
                double p = 1 - r.CrashTimer / CRASH_TIME;
                gfx.TranslateTransform(0, (float)(-12 - Math.Sin(Math.Min(1, p * 1.4) * Math.PI) * 14));
                gfx.RotateTransform((float)(Math.Min(1, p * 1.4) * 540));
                DrawCarBody(0, 12, 0, false, 0, r.Color);
            }
            else
            {
                DrawCarBody(0, 0, r.Lean, false, 0.5, r.Color);
            }
            gfx.Restore(saved);
        }

        private void DrawGate(RacerSegment seg)
        {
            double x1 = seg.X1, y1 = seg.ScreenY1, w1 = seg.W1;
            double postW = Math.Max(1, w1 * 0.06);
            double postH = w1 * 0.95;
            double lx = x1 - w1 * 1.15 - postW, rx = x1 + w1 * 1.15;
            Rect(Color.FromArgb(0xD8, 0xD8, 0xD8), lx, y1 - postH, postW, postH);
            Rect(Color.FromArgb(0xD8, 0xD8, 0xD8), rx, y1 - postH, postW, postH);

            double bx = lx, bw = rx + postW - lx;
            double bh = Math.Max(1, postH * 0.24), by = y1 - postH;
            string label;
            if (seg.Gate == RacerGate.Checkpoint)
            {
                Rect(Color.FromArgb(0x20, 0x40, 0xC0), bx, by, bw, bh);
                label = "CHECKPOINT";
            }
            else
            {
                // START / GOAL：黑白格子
                Rect(Color.White, bx, by, bw, bh);
                int cols = 20;
                double cw = bw / cols;
                if (cw >= 1)
                {
                    for (int i = 0; i < cols; i++)
                        Rect(Color.Black, bx + i * cw, by + (i % 2 == 0 ? 0 : bh / 2), cw, bh / 2);
                }
                label = seg.Gate == RacerGate.Goal ? "GOAL" : "START";
            }

            if (bh >= 6)
            {
                Font f = SizedFont(bh * 0.7);
                var r = new RectangleF((float)bx, (float)by, (float)bw, (float)bh);
                if (seg.Gate != RacerGate.Checkpoint)
                {
                    double tw = bw * 0.36;
                    Rect(Color.FromArgb(0xD0, 0x10, 0x10), bx + (bw - tw) / 2, by, tw, bh);
                }
                using (var b = new SolidBrush(F(seg.Gate == RacerGate.Checkpoint ? HudYellow : Color.White)))
                    gfx.DrawString(label, f, b, r, fmtCenter);
            }
        }

        private void DrawPlayer()
        {
            double speedPct = d.Speed / MAX_SPEED;
            float cx = W / 2f;
            float by = H - 2;

            if (d.CrashTimer > 0)
            {
                // 翻車：彈起來轉圈
                double p = 1 - d.CrashTimer / CRASH_TIME;
                double hop = Math.Sin(Math.Min(1, p * 1.4) * Math.PI) * 14;
                GraphicsState saved = gfx.Save();
                gfx.TranslateTransform(cx, (float)(by - 12 - hop));
                gfx.RotateTransform((float)(Math.Min(1, p * 1.4) * 540));
                DrawCarBody(0, 12, 0, false, 0, carColor);
                gfx.Restore(saved);
                return;
            }

            if (speedPct > 0.1 && rng.Next(3) == 0)
                by -= 1;
            if (d.OffRoad && d.Speed > 0)
                by -= rng.Next(2);

            DrawCarBody(cx, by, d.Lean, d.Boosting, speedPct, carColor);

            if (d.OffRoad && d.Speed > 0)
            {
                // 開到路外揚起沙塵
                RacerTheme t = RacerTheme.All[track.SegmentAt(d.Z).Stage];
                brush.Color = Alpha(Lighter(t.GrassDark, 0.2), 200);
                for (int i = 0; i < 5; i++)
                {
                    double side = i % 2 == 0 ? -1 : 1;
                    double px = cx + side * (20 + rng.NextDouble() * 10);
                    double py = by - 2 - rng.NextDouble() * 8;
                    double s = 2 + rng.NextDouble() * 3;
                    gfx.FillEllipse(brush, (float)px, (float)py, (float)s, (float)s);
                }
            }
        }

        // 敞篷跑車，從後面看，約 54x30。lean = -1 左轉 / 0 / 1 右轉
        private void DrawCarBody(float cx, float by, int lean, bool boost, double speedPct, Color body)
        {
            float L = lean * 2f, Lb = lean;
            Color hi = Lighter(body, 0.3);
            brush.Color = Color.FromArgb((int)(110 * Math.Min(1, curFog + 0.2)), 0, 0, 0);
            gfx.FillEllipse(brush, cx - 27, by - 4, 54, 6);

            Rect(TireColor, cx - 25 + Lb, by - 9, 8, 9);
            Rect(TireColor, cx + 17 + Lb, by - 9, 8, 9);
            Rect(Color.FromArgb(0x40, 0x40, 0x40), cx - 25 + Lb, by - 6, 8, 1);
            Rect(Color.FromArgb(0x40, 0x40, 0x40), cx + 17 + Lb, by - 6, 8, 1);

            // 車尾
            Rect(body, cx - 24, by - 17, 48, 11);
            Rect(hi, cx - 24, by - 17, 48, 1);
            // 左右各兩顆圓尾燈，中間車牌，下面黑色保險桿
            Color tail = Color.FromArgb(0xFF, 0x30, 0x20);
            Color tailRim = Darker(body, 0.5);
            Ellipse(tailRim, cx - 22, by - 15, 7, 6);
            Ellipse(tailRim, cx - 15, by - 15, 7, 6);
            Ellipse(tailRim, cx + 8, by - 15, 7, 6);
            Ellipse(tailRim, cx + 15, by - 15, 7, 6);
            Ellipse(tail, cx - 21, by - 14, 5, 4);
            Ellipse(tail, cx - 14, by - 14, 5, 4);
            Ellipse(tail, cx + 9, by - 14, 5, 4);
            Ellipse(tail, cx + 16, by - 14, 5, 4);
            Rect(Color.FromArgb(0x20, 0x20, 0x20), cx - 6, by - 14, 12, 5);
            Rect(Color.FromArgb(0xF0, 0xF0, 0xE0), cx - 5, by - 13, 10, 3);
            Rect(Color.FromArgb(0x20, 0x20, 0x20), cx - 24, by - 8, 48, 2);
            Rect(PoleColor, cx - 15, by - 7, 3, 2);
            Rect(PoleColor, cx + 12, by - 7, 3, 2);
            if (boost)
            {
                // 加速時排氣管噴火
                foreach (float ex in new[] { cx - 14.5f, cx + 12.5f })
                {
                    float fl = 4 + rng.Next(4);
                    Ellipse(Color.FromArgb(0xFF, 0x60, 0x10), ex - 2, by - 6, 6, fl);
                    Ellipse(Color.FromArgb(0xFF, 0xE0, 0x60), ex - 0.5f, by - 6, 3, fl * 0.6f);
                }
            }

            // 後車廂蓋、後照鏡
            Rect(body, cx - 22 + L, by - 21, 44, 5);
            Rect(hi, cx - 22 + L, by - 21, 44, 1);
            Rect(body, cx - 27 + L, by - 23, 4, 3);
            Rect(body, cx + 23 + L, by - 23, 4, 3);

            // 擋風玻璃框、頭枕、兩個人
            float hl = L * 1.5f;
            Rect(Color.FromArgb(0x50, 0x50, 0x58), cx - 18 + hl, by - 27, 36, 1);
            Rect(Color.FromArgb(0x50, 0x50, 0x58), cx - 18 + hl, by - 27, 1, 6);
            Rect(Color.FromArgb(0x50, 0x50, 0x58), cx + 17 + hl, by - 27, 1, 6);
            Rect(Color.FromArgb(0x30, 0x20, 0x20), cx - 15 + L, by - 24, 9, 3);
            Rect(Color.FromArgb(0x30, 0x20, 0x20), cx + 6 + L, by - 24, 9, 3);
            Ellipse(Color.FromArgb(0x3A, 0x24, 0x14), cx - 14 + hl, by - 30, 7, 7);
            Color blonde = Color.FromArgb(0xF8, 0xD8, 0x60);
            Ellipse(blonde, cx + 6 + hl, by - 30, 7, 7);
            if (speedPct > 0.2)
                Rect(blonde, cx + 12 + hl, by - 28, 2 + (float)(speedPct * 4), 2);
        }

        #endregion
    }
}
