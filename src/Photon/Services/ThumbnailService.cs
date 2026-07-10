using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Photon.App.Services;

/// <summary>
/// Real shell thumbnails (HEIC, RAW, video frames — anything the OS has codecs for) via
/// IShellItemImageFactory, with a bounded LRU cache. Loads run off the UI thread; painting
/// through <see cref="DrawCached"/> happens under the cache lock so an evicted bitmap can
/// never be drawn after disposal.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    public int ThumbSize { get; }

    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Path, Bitmap Image)>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Path, Bitmap Image)> _lru = new();
    private bool _disposed;

    public ThumbnailService(int thumbSize = 96, int cacheCapacity = 600)
    {
        ThumbSize = thumbSize;
        _capacity = Math.Max(32, cacheCapacity);
    }

    public bool IsCached(string path)
    {
        lock (_gate) return _map.ContainsKey(path);
    }

    /// <summary>Loads the thumbnail into the cache on a worker thread (placeholder on failure, never throws).</summary>
    public Task EnsureLoadedAsync(string path, CancellationToken ct = default) =>
        Task.Run(() => EnsureLoaded(path, ct), ct);

    /// <summary>Loads (if needed) and returns a caller-owned copy, safe to hand to a PictureBox.</summary>
    public async Task<Image?> GetThumbnailCloneAsync(string path, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(path, ct).ConfigureAwait(false);
        return GetClone(path);
    }

    public Image? GetClone(string path)
    {
        lock (_gate)
        {
            if (_disposed || !_map.TryGetValue(path, out var node)) return null;
            Touch(node);
            return new Bitmap(node.Value.Image);
        }
    }

    /// <summary>Draws the cached thumbnail letterboxed into dest; false when not cached yet.</summary>
    public bool DrawCached(Graphics g, string path, Rectangle dest)
    {
        lock (_gate)
        {
            if (_disposed || !_map.TryGetValue(path, out var node)) return false;
            Touch(node);
            var img = node.Value.Image;
            var scale = Math.Min((double)dest.Width / img.Width, (double)dest.Height / img.Height);
            var w = Math.Max(1, (int)(img.Width * scale));
            var h = Math.Max(1, (int)(img.Height * scale));
            g.DrawImage(img, dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h);
            return true;
        }
    }

    private void Touch(LinkedListNode<(string Path, Bitmap Image)> node)
    {
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private void EnsureLoaded(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_disposed || _map.ContainsKey(path)) return;
        }

        var bmp = LoadThumbnail(path) ?? CreatePlaceholder(path);

        lock (_gate)
        {
            if (_disposed || _map.ContainsKey(path))
            {
                bmp.Dispose();
                return;
            }
            _map[path] = _lru.AddFirst((path, bmp));
            while (_map.Count > _capacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Path);
                last.Value.Image.Dispose();
            }
        }
    }

    private Bitmap? LoadThumbnail(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var shell = LoadShellThumbnail(path, ThumbSize);
            if (shell is not null) return shell;
        }
        catch { }
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is not null) return icon.ToBitmap();
        }
        catch { }
        return null;
    }

    private Bitmap CreatePlaceholder(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        if (ext.Length == 0) ext = "FILE";
        if (ext.Length > 5) ext = ext[..5];
        var bmp = new Bitmap(ThumbSize, ThumbSize);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(72, 76, 84));
        using var pen = new Pen(Color.FromArgb(110, 114, 122));
        g.DrawRectangle(pen, 0, 0, ThumbSize - 1, ThumbSize - 1);
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        TextRenderer.DrawText(g, ext, font, new Rectangle(0, 0, ThumbSize, ThumbSize), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return bmp;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _lru) entry.Image.Dispose();
            _lru.Clear();
            _map.Clear();
        }
    }

    // ----- Windows shell interop -----

    private const uint SIIGBF_BIGGERSIZEOK = 0x01;

    [SupportedOSPlatform("windows")]
    private static Bitmap? LoadShellThumbnail(string path, int size)
    {
        var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); // IShellItemImageFactory
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var obj) != 0 ||
            obj is not IShellItemImageFactory factory)
            return null;
        try
        {
            var hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK, out var hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try { return Image.FromHbitmap(hbm); }
            finally { DeleteObject(hbm); }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, uint flags, out IntPtr phbm);
    }
}
