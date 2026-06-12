using System;
using System.Drawing;
using System.IO;

namespace Plugins
{
    public static class ImageUtil
    {
        private const int OrientationTag = 0x0112;

        /// <summary>
        /// Loads an image into a new Bitmap, applying any EXIF orientation so that
        /// phone photos display upright. Returns null if the file cannot be read.
        /// </summary>
        public static Bitmap LoadOriented(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var src = Image.FromStream(fs))
                {
                    var bmp = new Bitmap(src);
                    ApplyExifOrientation(bmp, src);
                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyExifOrientation(Bitmap target, Image source)
        {
            try
            {
                if (Array.IndexOf(source.PropertyIdList, OrientationTag) < 0) return;
                var prop = source.GetPropertyItem(OrientationTag);
                if (prop == null || prop.Value == null || prop.Value.Length < 2) return;
                int orientation = BitConverter.ToUInt16(prop.Value, 0);
                switch (orientation)
                {
                    case 2: target.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                    case 3: target.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                    case 4: target.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                    case 5: target.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                    case 6: target.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                    case 7: target.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                    case 8: target.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                }
            }
            catch
            {
            }
        }
    }
}
