using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

internal static class MakeIcon
{
    private static GraphicsPath Shield()
    {
        GraphicsPath p = new GraphicsPath();
        p.StartFigure();
        p.AddBezier(128, 13, 155, 29, 186, 39, 219, 43);
        p.AddLine(219, 43, 216, 120);
        p.AddBezier(216, 120, 213, 174, 182, 215, 128, 242);
        p.AddBezier(128, 242, 74, 215, 43, 174, 40, 120);
        p.AddLine(40, 120, 37, 43);
        p.AddBezier(37, 43, 70, 39, 101, 29, 128, 13);
        p.CloseFigure();
        return p;
    }

    private static GraphicsPath Bubble()
    {
        GraphicsPath p = new GraphicsPath();
        p.StartFigure();
        p.AddArc(70, 67, 28, 28, 180, 90);
        p.AddLine(84, 67, 172, 67);
        p.AddArc(158, 67, 28, 28, 270, 90);
        p.AddLine(186, 81, 186, 133);
        p.AddArc(158, 119, 28, 28, 0, 90);
        p.AddLine(172, 147, 122, 147);
        p.AddLine(122, 147, 90, 173);
        p.AddLine(90, 173, 94, 147);
        p.AddLine(94, 147, 84, 147);
        p.AddArc(70, 119, 28, 28, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Bitmap Render(int size)
    {
        const int renderSize = 1024;
        using (Bitmap large = new Bitmap(renderSize, renderSize, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(large))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(renderSize / 256f, renderSize / 256f);
                using (GraphicsPath shield = Shield())
                using (LinearGradientBrush fill = new LinearGradientBrush(new Point(55, 33), new Point(191, 219), Color.FromArgb(37, 106, 211), Color.FromArgb(14, 48, 112)))
                using (Pen outline = new Pen(Color.FromArgb(73, 223, 204), 9))
                {
                    outline.LineJoin = LineJoin.Round;
                    g.FillPath(fill, shield);
                    g.DrawPath(outline, shield);
                }
                using (GraphicsPath bubble = Bubble())
                using (SolidBrush white = new SolidBrush(Color.FromArgb(248, 253, 255)))
                    g.FillPath(white, bubble);
                // A substantial diagonal cut remains clear at notification-area size.
                using (Pen cut = new Pen(Color.FromArgb(24, 77, 153), 28))
                using (Pen highlight = new Pen(Color.FromArgb(78, 231, 205), 10))
                {
                    cut.StartCap = cut.EndCap = LineCap.Round;
                    highlight.StartCap = highlight.EndCap = LineCap.Round;
                    g.DrawLine(cut, 88, 155, 175, 69);
                    g.DrawLine(highlight, 88, 155, 175, 69);
                }
            }
            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(large, new Rectangle(0, 0, size, size), 0, 0, renderSize, renderSize, GraphicsUnit.Pixel);
            }
            return result;
        }
    }

    private static int Main(string[] args)
    {
        string destination = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
        Directory.CreateDirectory(destination);
        int[] sizes = {16, 20, 24, 32, 40, 48, 64, 128, 256};
        List<byte[]> frames = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (Bitmap icon = Render(size))
            using (MemoryStream data = new MemoryStream())
            {
                icon.Save(data, ImageFormat.Png);
                frames.Add(data.ToArray());
                if (size == 256) icon.Save(Path.Combine(destination, "StreamPrivacy.png"), ImageFormat.Png);
                if (size == 16 || size == 32) icon.Save(Path.Combine(destination, "StreamPrivacy-" + size + ".png"), ImageFormat.Png);
            }
        }
        using (BinaryWriter writer = new BinaryWriter(File.Create(Path.Combine(destination, "StreamPrivacy.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
        using (Icon test = new Icon(Path.Combine(destination, "StreamPrivacy.ico"), new Size(16, 16)))
        {
            if (test.Width != 16 || test.Height != 16) throw new Exception("Icon frame validation failed.");
        }
        Console.WriteLine("Generated 9-size ICO and PNG previews in " + destination);
        return 0;
    }
}
