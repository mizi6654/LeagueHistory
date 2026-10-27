using System.Collections.Concurrent;

namespace League.Caching
{
    /// <summary>
    /// 皮肤浏览器专用：图片只解码一次，由缓存统一释放。
    /// 支持多线程 GetOrLoad（Parallel / ThreadPool）。
    /// </summary>
    public sealed class SkinBrowserImageCache : IDisposable
    {
        private readonly ConcurrentDictionary<string, Image> _map =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>同一 key 只允许一个 loader 执行，避免重复解码。</summary>
        private readonly ConcurrentDictionary<string, object> _keyLocks =
            new(StringComparer.OrdinalIgnoreCase);

        private volatile bool _disposed;

        public Image? GetOrLoad(string key, Func<Image?> loader)
        {
            if (_disposed || string.IsNullOrEmpty(key)) return null;

            if (_map.TryGetValue(key, out var existing))
                return existing;

            var gate = _keyLocks.GetOrAdd(key, _ => new object());
            lock (gate)
            {
                if (_disposed) return null;
                if (_map.TryGetValue(key, out existing))
                    return existing;

                Image? img = null;
                try { img = loader(); }
                catch { img = null; }

                if (img == null) return null;

                // 若并发下已有人放入，丢掉本次，用已有的
                if (_map.TryAdd(key, img))
                    return img;

                try { img.Dispose(); } catch { }
                return _map.TryGetValue(key, out existing) ? existing : null;
            }
        }

        public Image? Get(string key)
        {
            if (_disposed || string.IsNullOrEmpty(key)) return null;
            return _map.TryGetValue(key, out var img) ? img : null;
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var kv in _map)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _map.Clear();
            _keyLocks.Clear();
        }

        public static Image? LoadScaledFromFile(string path, int maxW, int maxH)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var src = Image.FromStream(fs);
                return Scale(src, maxW, maxH);
            }
            catch { return null; }
        }

        public static Image Scale(Image src, int maxW, int maxH)
        {
            if (src.Width <= maxW && src.Height <= maxH)
                return new Bitmap(src);

            double ratio = Math.Min(maxW / (double)src.Width, maxH / (double)src.Height);
            int w = Math.Max(1, (int)(src.Width * ratio));
            int h = Math.Max(1, (int)(src.Height * ratio));
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, w, h);
            return bmp;
        }

        public static Image? LoadCroppedFromFile(string path, int targetW, int targetH)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var src = Image.FromStream(fs);
                return CropScale(src, targetW, targetH);
            }
            catch { return null; }
        }

        public static Image? CropScale(Image src, int targetW, int targetH)
        {
            if (src == null || targetW <= 0 || targetH <= 0) return null;

            double targetRatio = (double)targetW / targetH;
            double srcRatio = (double)src.Width / src.Height;

            int cropW = src.Width, cropH = src.Height;
            int x = 0, y = 0;
            if (srcRatio > targetRatio)
            {
                cropW = Math.Max(1, (int)(src.Height * targetRatio));
                x = (src.Width - cropW) / 2;
            }
            else
            {
                cropH = Math.Max(1, (int)(src.Width / targetRatio));
                y = (src.Height - cropH) / 2;
            }

            var result = new Bitmap(targetW, targetH);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src,
                    new Rectangle(0, 0, targetW, targetH),
                    new Rectangle(x, y, cropW, cropH),
                    GraphicsUnit.Pixel);
            }
            return result;
        }
    }
}