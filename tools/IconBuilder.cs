using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconBuilder
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: IconBuilder <ico-output> <png-preview-output>");
            return 2;
        }

        var images = new List<byte[]>();
        foreach (var size in Sizes)
        {
            using (var bitmap = DrawIcon(size))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                images.Add(stream.ToArray());
            }
        }

        WriteIco(args[0], images);
        using (var preview = DrawIcon(256))
            preview.Save(args[1], ImageFormat.Png);

        return 0;
    }

    private static Bitmap DrawIcon(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            var cropInset = size * 0.17f;
            var cropLength = size * 0.195f;
            using (var cropOutline = new Pen(Color.FromArgb(7, 94, 168),
                Math.Max(2.2f, size * 0.078f)))
            using (var cropPen = new Pen(Color.FromArgb(70, 195, 223),
                Math.Max(1.25f, size * 0.043f)))
            {
                cropOutline.StartCap = LineCap.Round;
                cropOutline.EndCap = LineCap.Round;
                cropOutline.LineJoin = LineJoin.Miter;
                cropPen.StartCap = LineCap.Round;
                cropPen.EndCap = LineCap.Round;
                cropPen.LineJoin = LineJoin.Miter;
                DrawCorner(graphics, cropOutline, cropInset, cropInset, cropLength, 1, 1);
                DrawCorner(graphics, cropOutline, size - cropInset, size - cropInset,
                    cropLength, -1, -1);
                DrawCorner(graphics, cropPen, cropInset, cropInset, cropLength, 1, 1);
                DrawCorner(graphics, cropPen, size - cropInset, size - cropInset, cropLength, -1, -1);
            }

            using (var bolt = new GraphicsPath())
            using (var boltBrush = new SolidBrush(Color.FromArgb(255, 180, 59)))
            using (var boltOutline = new Pen(Color.FromArgb(7, 94, 168),
                Math.Max(1f, size * 0.031f)))
            {
                bolt.AddPolygon(new[]
                {
                    new PointF(size * 0.62f, size * 0.11f),
                    new PointF(size * 0.26f, size * 0.55f),
                    new PointF(size * 0.46f, size * 0.55f),
                    new PointF(size * 0.35f, size * 0.90f),
                    new PointF(size * 0.75f, size * 0.38f),
                    new PointF(size * 0.54f, size * 0.38f)
                });
                boltOutline.LineJoin = LineJoin.Round;
                graphics.DrawPath(boltOutline, bolt);
                graphics.FillPath(boltBrush, bolt);
            }

        }

        return bitmap;
    }

    private static void DrawCorner(Graphics graphics, Pen pen, float x, float y,
        float length, int horizontalDirection, int verticalDirection)
    {
        using (var path = new GraphicsPath())
        {
            path.AddLines(new[]
            {
                new PointF(x, y + length * verticalDirection),
                new PointF(x, y),
                new PointF(x + length * horizontalDirection, y)
            });
            graphics.DrawPath(pen, path);
        }
    }

    private static void WriteIco(string path, IList<byte[]> images)
    {
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Count);

            var offset = 6 + images.Count * 16;
            for (var index = 0; index < images.Count; index++)
            {
                var size = Sizes[index];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[index].Length);
                writer.Write(offset);
                offset += images[index].Length;
            }

            foreach (var image in images)
                writer.Write(image);
        }
    }
}
