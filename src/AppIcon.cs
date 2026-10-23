using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PhoneLink
{
    /// <summary>
    /// 程序图标：手机样式。运行时按多尺寸生成 ICO（绿色版不依赖外部 ico 文件），
    /// 任务栏取 32px、托盘取 16px，各自清晰。
    /// </summary>
    static class AppIcon
    {
        static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        static Icon _icon;
        static MemoryStream _stream;

        public static Icon Get()
        {
            if (_icon != null) return _icon;

            byte[] bytes = BuildIcoBytes();
            _stream = new MemoryStream(bytes);   // Icon 会延迟读取，流必须保持打开
            _icon = new Icon(_stream);
            return _icon;
        }

        /// <summary>生成多尺寸 ICO 字节流（build.ps1 会写进 exe 的 PE 资源，运行时图标也用它）</summary>
        public static byte[] BuildIcoBytes()
        {
            var images = new byte[Sizes.Length][];
            for (int i = 0; i < Sizes.Length; i++)
            {
                using (Bitmap bmp = Render(Sizes[i]))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images[i] = ms.ToArray();
                }
            }

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)0);              // reserved
                w.Write((short)1);              // type: icon
                w.Write((short)Sizes.Length);

                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int s = Sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));  // width（0 表示 256）
                    w.Write((byte)(s >= 256 ? 0 : s));  // height
                    w.Write((byte)0);                   // 调色板
                    w.Write((byte)0);                   // reserved
                    w.Write((short)1);                  // planes
                    w.Write((short)32);                 // bpp
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }

                foreach (byte[] img in images) w.Write(img);
                return ms.ToArray();
            }
        }

        static Bitmap Render(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                var body = new RectangleF(s * 0.24f, s * 0.05f, s * 0.52f, s * 0.90f);
                float radius = body.Width * 0.30f;

                // 机身：强调色渐变
                using (GraphicsPath path = Round(body, radius))
                {
                    using (var brush = new LinearGradientBrush(
                        new PointF(0, body.Top), new PointF(0, body.Bottom),
                        Color.FromArgb(255, 122, 162, 255),
                        Color.FromArgb(255, 139, 108, 255)))
                    {
                        g.FillPath(brush, path);
                    }
                    using (var pen = new Pen(Color.FromArgb(64, 255, 255, 255), Math.Max(1f, s * 0.012f)))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                // 屏幕
                var screen = new RectangleF(
                    body.X + body.Width * 0.12f,
                    body.Y + body.Height * 0.115f,
                    body.Width * 0.76f,
                    body.Height * 0.70f);
                using (GraphicsPath sp = Round(screen, Math.Max(1f, screen.Width * 0.10f)))
                using (var sb = new SolidBrush(Color.FromArgb(244, 255, 255, 255)))
                {
                    g.FillPath(sb, sp);
                }

                // 听筒（小尺寸省略，避免糊成一团）
                if (s >= 32)
                {
                    float sw = body.Width * 0.26f;
                    float sh = Math.Max(1f, s * 0.028f);
                    var spk = new RectangleF(
                        body.X + (body.Width - sw) / 2f,
                        body.Y + body.Height * 0.055f - sh / 2f,
                        sw, sh);
                    using (GraphicsPath p = Round(spk, sh / 2f))
                    using (var b = new SolidBrush(Color.FromArgb(232, 255, 255, 255)))
                    {
                        g.FillPath(b, p);
                    }
                }

                // 底部 HOME 圆点
                float d = Math.Max(1.6f, s * 0.055f);
                using (var b = new SolidBrush(Color.FromArgb(236, 255, 255, 255)))
                {
                    g.FillEllipse(b,
                        body.X + (body.Width - d) / 2f,
                        body.Y + body.Height * 0.885f - d / 2f,
                        d, d);
                }
            }
            return bmp;
        }

        static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            if (r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
