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

        // 標題 + 副標題，副標題超過 maxW 會自動換行
        static void Title(Graphics g, string title, string sub, float x = 120, float y = 56, float maxW = 1600)
        {
            using (var f = F(64, FontStyle.Regular, "Segoe UI Black"))
            using (var b = new SolidBrush(Color.White))
                g.DrawString(title, f, b, x, y);
            using (var f = F(30))
            using (var b = new SolidBrush(Text2))
                g.DrawString(sub, f, b, new RectangleF(x + 4, y + 92, maxW, 200));
            using (var b = new LinearGradientBrush(new RectangleF(x + 4, y - 18, 160, 8), Orange, Pink, 0f))
                g.FillRectangle(b, x + 4, y - 18, 160, 8);
        }

        static void Caption(Graphics g, string text, float x, float y, float w)
        {
            using (var f = F(22, FontStyle.Bold))
            using (var b = new SolidBrush(Dim))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(text, f, b, new RectangleF(x, y, w, 40), fmt);
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

        // 官方的 Stream Deck+ 正面圖（Figma 的 Stream Deck Templates 匯出，1024x972），版權是 Elgato 的，不放進 git
        // 以下座標都是量原圖得來的
        const string DevFile = "stream-deck-plus.png";
        const float SrcW = 1024, SrcH = 972;
        static readonly float[] SrcKeyX = { 117, 341, 565, 789 }, SrcKeyY = { 84, 236 };
        const float SrcKey = 118, SrcKeyR = 12;
        const float SrcStripX = 66, SrcStripY = 410, SrcStripW = 890, SrcStripH = 192;
        static readonly float[] SrcDialX = { 175, 400, 624, 848 };
        const float SrcDialY = 771, SrcDialR = 56;

        // 縮放到觸控條剛好 800px 寬，遊戲畫面 1:1 不失真
        const float DevS = 800 / SrcStripW;
        const float DevW = SrcW * DevS, DevH = SrcH * DevS; // 920 x 874

        static Bitmap DevImg;

        // keys：4x2 顆按鍵的畫面，每顆 144x144，可為 null（按鍵保持原圖的黑色）
        static void Device(Graphics g, float x, float y, Image strip, Image keys = null, string[] labels = null, Color[] colors = null)
        {
            if (DevImg == null)
            {
                string path = Path.Combine(Root, "tools", "shots", DevFile);
                if (!File.Exists(path))
                    throw new FileNotFoundException("Export the Stream Deck+ from Elgato's Figma Stream Deck Templates as PNG and save it here", path);
                DevImg = new Bitmap(path);
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(DevImg, x, y, DevW, DevH);

            // 按鍵
            if (keys != null)
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 4; c++)
                        using (var path = Round(new RectangleF(x + SrcKeyX[c] * DevS, y + SrcKeyY[r] * DevS, SrcKey * DevS, SrcKey * DevS), SrcKeyR * DevS))
                        {
                            var st = g.Save();
                            g.SetClip(path);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(keys, path.GetBounds(), new RectangleF(c * 144, r * 144, 144, 144), GraphicsUnit.Pixel);
                            g.Restore(st);
                        }

            // 觸控條：800x100 的畫面放在玻璃的正中間
            float sx = x + SrcStripX * DevS;
            float sy = y + (SrcStripY + SrcStripH / 2) * DevS - 50;
            Pixel(g, strip, sx, sy, 800, 100);

            // 旋鈕下方的標籤
            if (labels != null)
                for (int i = 0; i < 4; i++)
                {
                    if (labels[i] == null) continue;
                    Color c = colors != null ? colors[i] : Color.White;
                    using (var f = F(22, FontStyle.Bold))
                    using (var b = new SolidBrush(c))
                    using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
                        g.DrawString(labels[i], f, b, x + SrcDialX[i] * DevS, y + (SrcDialY + SrcDialR) * DevS + 10, fmt);
                }
        }

        // 右邊放裝置，左邊放標題和 2 倍大的觸控條特寫
        const float DevX = W - 40 - DevW, DevY = (H - DevH - 36) / 2, LeftW = 800;

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
            Title(g, "4-player racing", "Split-screen on the touch strip, one player per dial. Press to join, rotate to steer, press to boost (3 per race).", maxW: LeftW);
            using (var race = Img("race_20"))
            using (var map = Img("map_20"))
            {
                using (var a = Crop(race, 0, 400)) Card(g, a, 120, 330, 800, 200);
                using (var b = Crop(race, 400, 400)) Card(g, b, 120, 570, 800, 200);
                Caption(g, "TOUCH STRIP, 2X", 120, 790, 800);
                Device(g, DevX, DevY, race, map, new[] { "P1", "P2", "P3", "P4" }, RacerConst.PlayerColors);
                Caption(g, "RACER MAP ON THE KEYS: LIVE POSITIONS AND RANKING", DevX, DevY + DevH + 4, DevW);
            }
            End(g, bmp, "gallery-1-racer-4p");
        }

        // ---------- 足球 ----------
        static void Foosball()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);
            Title(g, "2-player foosball", "One table across the whole touch strip. Red plays dials 1 and 3, blue plays dials 2 and 4. Rotate to slide the rods, press to kick, hold to lift.", maxW: LeftW);
            Color red = Color.FromArgb(0xFF, 0x5A, 0x5A), blue = Color.FromArgb(0x6A, 0xA8, 0xFF);
            using (var fo = Img("foosball"))
            {
                using (var a = Crop(fo, 0, 400)) Card(g, a, 120, 330, 800, 200);
                using (var b = Crop(fo, 400, 400)) Card(g, b, 120, 570, 800, 200);
                Caption(g, "TOUCH STRIP, 2X", 120, 790, 800);
                Device(g, DevX, DevY, fo, null, new[] { "RED", "BLUE", "RED", "BLUE" }, new[] { red, blue, red, blue });
            }
            End(g, bmp, "gallery-2-foosball");
        }

        // ---------- 單旋鈕遊戲 ----------
        static void Classics()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);
            Title(g, "One dial, one game", "Breakout, Snake and Sunset Racer each run on a single dial. Mix and match across the four dials.", maxW: LeftW);
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
                using (var br = Img("breakout")) Card(g, br, 120, 330, 400, 200);
                using (var sn = Img("snake")) Card(g, sn, 120, 570, 400, 200);
                using (var st = Img("stage_1")) Card(g, st, 540, 330, 400, 200);
                using (var st = Img("stage_4")) Card(g, st, 540, 570, 400, 200);
                Caption(g, "TOUCH STRIP, 2X", 120, 790, 820);
                Device(g, DevX, DevY, strip, null, new[] { "Breakout", "Snake", "Sunset Racer", "Sunset Racer" }, new[] { Color.White, Color.White, Color.White, Color.White });
            }
            End(g, bmp, "gallery-3-classics");
        }

        // ---------- 五個關卡 ----------
        static void Stages()
        {
            var bmp = new Bitmap(W, H);
            var g = Begin(bmp);
            Title(g, "Five stages to the goal", "Sunset Racer is a pseudo-3D arcade racer with hills, curves and traffic. Reach each checkpoint before time runs out.");
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
