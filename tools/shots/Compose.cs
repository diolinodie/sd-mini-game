using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace MiniGame
{
    // 把遊戲畫面排成 Marketplace 用的 1920x960 圖
    static class Compose
    {
        const int W = 1920, H = 960;
        static string Raw, Dst, Root;

        static readonly Color Orange = Color.FromArgb(0xFF, 0x8C, 0x20);
        static readonly Color Pink = Color.FromArgb(0xFF, 0x4F, 0x8B);
        static readonly Color Text2 = Color.FromArgb(0xC8, 0xC8, 0xD8);
        static readonly Color Dim = Color.FromArgb(0x8A, 0x8A, 0xA0);

        public static void Run(string raw, string dst, string root)
        {
            Raw = raw; Dst = dst; Root = root;
            Directory.CreateDirectory(dst);
            Thumbnail();
            Race4P();
            Foosball();
            Classics();
            Stages();
        }

        static Bitmap Img(string n) => new Bitmap(Path.Combine(Raw, n + ".png"));

        static Graphics Begin(Bitmap b)
        {
            var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(0x24, 0x16, 0x45), Color.FromArgb(0x0A, 0x0A, 0x16), 90f))
                g.FillRectangle(bg, 0, 0, W, H);
            // 淡淡的地平線格子
            using (var p = new Pen(Color.FromArgb(18, 255, 140, 32), 2))
            {
                for (int y = 0; y < H; y += 48) g.DrawLine(p, 0, y, W, y);
                for (int x = 0; x < W; x += 48) g.DrawLine(p, x, 0, x, H);
            }
            return g;
        }

        static void End(Graphics g, Bitmap b, string name)
        {
            g.Dispose();
            b.Save(Path.Combine(Dst, name + ".png"), ImageFormat.Png);
            b.Dispose();
        }

        static Font F(float px, FontStyle st = FontStyle.Regular, string fam = "Segoe UI") => new Font(fam, px, st, GraphicsUnit.Pixel);

        static void Title(Graphics g, string title, string sub, float x = 120, float y = 56)
        {
            using (var f = F(64, FontStyle.Regular, "Segoe UI Black"))
            using (var b = new SolidBrush(Color.White))
                g.DrawString(title, f, b, x, y);
            using (var f = F(30))
            using (var b = new SolidBrush(Text2))
                g.DrawString(sub, f, b, x + 4, y + 92);
            using (var b = new LinearGradientBrush(new RectangleF(x + 4, y - 18, 160, 8), Orange, Pink, 0f))
                g.FillRectangle(b, x + 4, y - 18, 160, 8);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // 像素圖放大，整數倍、最近鄰
        static void Pixel(Graphics g, Image img, float x, float y, float w, float h)
        {
            var im = g.InterpolationMode;
            var po = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, x, y, w, h);
            g.InterpolationMode = im;
            g.PixelOffsetMode = po;
        }

        // 觸控條 + 下面四顆旋鈕，scale 2 時觸控條 1600x200
        static void Strip(Graphics g, Image strip, float top, string[] labels = null, Color[] colors = null)
        {
            float sw = 1600, sh = 200, x = (W - sw) / 2;
            using (var body = Round(new RectangleF(x - 28, top - 28, sw + 56, sh + 290), 36))
            {
                using (var b = new LinearGradientBrush(new RectangleF(x, top - 30, sw, sh + 320), Color.FromArgb(0x2C, 0x2C, 0x34), Color.FromArgb(0x16, 0x16, 0x1C), 90f))
                    g.FillPath(b, body);
                using (var p = new Pen(Color.FromArgb(0x46, 0x46, 0x52), 2)) g.DrawPath(p, body);
            }
            g.FillRectangle(Brushes.Black, x - 4, top - 4, sw + 8, sh + 8);
            Pixel(g, strip, x, top, sw, sh);

            for (int i = 0; i < 4; i++)
            {
                float cx = x + 200 + i * 400, cy = top + sh + 104, r = 62;
                using (var sh2 = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillEllipse(sh2, cx - r + 4, cy - r + 8, r * 2, r * 2);
                using (var b = new LinearGradientBrush(new RectangleF(cx - r, cy - r, r * 2, r * 2), Color.FromArgb(0x5A, 0x5A, 0x64), Color.FromArgb(0x22, 0x22, 0x28), 60f))
                    g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                using (var b = new LinearGradientBrush(new RectangleF(cx - r, cy - r, r * 2, r * 2), Color.FromArgb(0x34, 0x34, 0x3C), Color.FromArgb(0x40, 0x40, 0x48), 60f))
                    g.FillEllipse(b, cx - r + 10, cy - r + 10, r * 2 - 20, r * 2 - 20);
                using (var p = new Pen(Color.FromArgb(0xB0, 0xB0, 0xBC), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, cx, cy - r + 16, cx, cy - r + 34);

                if (labels != null && labels[i] != null)
                {
                    Color c = colors != null ? colors[i] : Color.White;
                    using (var f = F(26, FontStyle.Bold))
                    using (var b = new SolidBrush(c))
                    using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
                        g.DrawString(labels[i], f, b, cx, cy + r + 18, fmt);
                }
            }
        }

        static void Card(Graphics g, Image img, float x, float y, float w, float h, float rad = 14)
        {
            using (var sh = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            using (var path = Round(new RectangleF(x + 6, y + 12, w, h), rad))
                g.FillPath(sh, path);
            using (var path = Round(new RectangleF(x, y, w, h), rad))
            {
                var st = g.Save();
                g.SetClip(path);
                Pixel(g, img, x, y, w, h);
                g.Restore(st);
                using (var p = new Pen(Color.FromArgb(70, 255, 255, 255), 2)) g.DrawPath(p, path);
            }
        }

        static Bitmap Crop(Image src, int x, int w)
        {
            var b = new Bitmap(w, src.Height);
            using (var g = Graphics.FromImage(b)) g.DrawImage(src, new Rectangle(0, 0, w, src.Height), new Rectangle(x, 0, w, src.Height), GraphicsUnit.Pixel);
            return b;
        }

        // ---------- 縮圖 ----------
        static void Thumbnail()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);

            // 夕陽
            using (var b = new LinearGradientBrush(new RectangleF(0, 560, W, 400), Color.FromArgb(0, Orange), Color.FromArgb(70, Pink), 90f))
                g.FillRectangle(b, 0, 560, W, 400);

            using (var icon = new Bitmap(Path.Combine(Root, @"mini_game\Images\pluginIcon@2x.png")))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, 110, 150, 230, 230);
            }
            using (var f = F(150, FontStyle.Regular, "Segoe UI Black"))
            using (var b = new LinearGradientBrush(new RectangleF(110, 380, 820, 190), Color.FromArgb(0xFF, 0xD0, 0x40), Pink, 90f))
                g.DrawString("SUNSET", f, b, 100, 370);
            using (var f = F(150, FontStyle.Regular, "Segoe UI Black"))
                g.DrawString("ARCADE", f, Brushes.White, 100, 540);
            using (var f = F(38))
            using (var b = new SolidBrush(Text2))
                g.DrawString("Arcade games on the Stream Deck+\ndials & touch strip", f, b, 114, 750);

            using (var race = Img("race_20"))
            using (var p1 = Crop(race, 0, 200))
            using (var p3 = Crop(race, 400, 200))
            using (var br = Img("breakout"))
            using (var sn = Img("snake"))
            using (var fo = Img("foosball"))
            using (var foc = Crop(fo, 200, 400))
            {
                float gx = 1000, gy = 120;
                Card(g, p1, gx, gy, 400, 200);
                Card(g, p3, gx + 430, gy, 400, 200);
                Card(g, br, gx, gy + 230, 400, 200);
                Card(g, sn, gx + 430, gy + 230, 400, 200);
                Card(g, foc, gx, gy + 460, 830, 200);
            }
            End(g, bmp, "thumbnail");
        }

        // ---------- 4 人賽車 ----------
        static void Race4P()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);
            Title(g, "4-player split-screen racing", "Every dial is a player. Press to join, rotate to steer, press to boost.");

            using (var map = Img("map_20"))
            {
                // 8 顆按鍵 = 一張大地圖
                float kx = 1180, ky = 44, k = 144, gap = 12;
                using (var f = F(22, FontStyle.Bold))
                using (var b = new SolidBrush(Dim))
                    g.DrawString("LIVE RACE MAP ON THE KEYS", f, b, kx, ky + 2 * k + gap + 10);
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 4; c++)
                    {
                        using (var tile = new Bitmap(144, 144))
                        {
                            using (var tg = Graphics.FromImage(tile)) tg.DrawImage(map, new Rectangle(0, 0, 144, 144), new Rectangle(c * 144, r * 144, 144, 144), GraphicsUnit.Pixel);
                            Card(g, tile, kx + c * (k + gap), ky + r * (k + gap), k, k, 18);
                        }
                    }
            }
            using (var race = Img("race_20"))
                Strip(g, race, 420, new[] { "P1", "P2", "P3", "P4" }, RacerConst.PlayerColors);
            End(g, bmp, "gallery-1-racer-4p");
        }

        // ---------- 足球 ----------
        static void Foosball()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);
            Title(g, "2-player foosball", "One table across the whole touch strip. Rotate to slide the rods, press to kick.");
            Color red = Color.FromArgb(0xFF, 0x5A, 0x5A), blue = Color.FromArgb(0x6A, 0xA8, 0xFF);
            using (var fo = Img("foosball"))
                Strip(g, fo, 330,new[] { "RED", "BLUE", "RED", "BLUE" }, new[] { red, blue, red, blue });
            End(g, bmp, "gallery-2-foosball");
        }

        // ---------- 單旋鈕遊戲 ----------
        static void Classics()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);
            Title(g, "One dial, one game", "Breakout, Snake and Sunset Racer each run on a single dial. Mix and match.");
            using (var strip = new Bitmap(800, 100))
            {
                using (var sg = Graphics.FromImage(strip))
                {
                    string[] n = { "breakout", "snake", "stage_1", "stage_4" };
                    for (int i = 0; i < 4; i++) using (var im = Img(n[i])) sg.DrawImage(im, i * 200, 0, 200, 100);
                    // 每顆旋鈕的畫面之間畫一條細線，黑底的遊戲才分得出來
                    using (var p = new Pen(Color.FromArgb(0x3A, 0x3A, 0x44), 1))
                        for (int i = 1; i < 4; i++) sg.DrawLine(p, i * 200 - 0.5f, 0, i * 200 - 0.5f, 100);
                }
                Strip(g, strip, 330, new[] { "Breakout", "Snake", "Sunset Racer", "Sunset Racer" }, new[] { Color.White, Color.White, Color.White, Color.White });
            }
            End(g, bmp, "gallery-3-classics");
        }

        // ---------- 五個關卡 ----------
        static void Stages()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);
            Title(g, "Five stages to the goal", "Sunset Racer is a pseudo-3D arcade racer with hills, curves and traffic.");
            string[] names = { "PALM BEACH", "SUNSET COAST", "DESERT", "ALPINE PASS", "AUTUMN ROAD" };
            float w = 400, h = 200, gap = 44;
            for (int s = 0; s < 5; s++)
            {
                int row = s < 3 ? 0 : 1;
                int col = s < 3 ? s : s - 3;
                float rowW = row == 0 ? 3 * w + 2 * gap : 2 * w + gap;
                float x = (W - rowW) / 2 + col * (w + gap);
                float y = 270 + row * (h + 96);
                using (var im = Img($"stage_{s}")) Card(g, im, x, y, w, h);
                using (var f = F(24, FontStyle.Bold))
                using (var b = new SolidBrush(Text2))
                    g.DrawString($"{s + 1}  {names[s]}", f, b, x + 4, y + h + 18);
            }
            End(g, bmp, "gallery-4-stages");
        }
    }
}
