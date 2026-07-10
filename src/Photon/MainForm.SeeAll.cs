using Photon.App.Services;
using Photon.Core.Models;

namespace Photon.App;

/// <summary>
/// The "See All" viewer: every scanned file with thumbnail + name. Owner-drawn grid that
/// only paints the visible tiles and lazily loads their thumbnails in small batches, so
/// 10k+ files stay smooth. MainForm helper — intentionally not part of the Forms module.
/// </summary>
internal sealed class SeeAllFilesForm : Form
{
    private const int TileWidth = 150;
    private const int TileHeight = 148;
    private const int Pad = 8;
    private const int BatchSize = 12;

    private readonly IReadOnlyList<MediaFile> _files;
    private readonly ThumbnailService _thumbs;
    private readonly BufferedPanel _canvas;
    private readonly VScrollBar _scroll;
    private readonly CancellationTokenSource _cts = new();
    private readonly Queue<string> _pending = new();
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private bool _pumping;

    public SeeAllFilesForm(IReadOnlyList<MediaFile> files, ThumbnailService thumbnails)
    {
        _files = files;
        _thumbs = thumbnails;

        Text = $"All files ({files.Count:N0}) - Photon";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 720);
        MinimumSize = new Size(560, 400);
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;

        _scroll = new VScrollBar { Dock = DockStyle.Right, Width = SystemInformation.VerticalScrollBarWidth };
        _canvas = new BufferedPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
        Controls.Add(_canvas);
        Controls.Add(_scroll);

        _scroll.ValueChanged += (_, _) => _canvas.Invalidate();
        _canvas.Paint += OnPaintCanvas;
        _canvas.Resize += (_, _) => { UpdateScrollRange(); _canvas.Invalidate(); };
        _canvas.MouseWheel += OnWheel;
        MouseWheel += OnWheel;
        _canvas.MouseDown += (_, _) => _canvas.Focus();
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Escape: Close(); break;
                case Keys.PageDown: ScrollBy(_canvas.ClientSize.Height); break;
                case Keys.PageUp: ScrollBy(-_canvas.ClientSize.Height); break;
                case Keys.Home: if (_scroll.Enabled) _scroll.Value = 0; break;
                case Keys.End: if (_scroll.Enabled) _scroll.Value = MaxScroll; break;
            }
        };
        Shown += (_, _) => { UpdateScrollRange(); _canvas.Focus(); };
        FormClosed += (_, _) => _cts.Cancel();
        ThemeService.FixGaps(this);
    }

    private int Columns => Math.Max(1, (_canvas.ClientSize.Width - Pad * 2) / TileWidth);

    private int MaxScroll => Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1);

    private void UpdateScrollRange()
    {
        var rows = (_files.Count + Columns - 1) / Columns;
        var content = rows * TileHeight + Pad * 2;
        var view = _canvas.ClientSize.Height;
        if (content <= view || view <= 0)
        {
            _scroll.Enabled = false;
            _scroll.Minimum = 0;
            _scroll.Maximum = 0;
            _scroll.Value = 0;
            return;
        }
        _scroll.Enabled = true;
        _scroll.Minimum = 0;
        _scroll.Maximum = content;
        _scroll.LargeChange = Math.Max(1, view);
        _scroll.SmallChange = TileHeight / 2;
        if (_scroll.Value > MaxScroll) _scroll.Value = MaxScroll;
    }

    private void ScrollBy(int delta)
    {
        if (!_scroll.Enabled) return;
        _scroll.Value = Math.Clamp(_scroll.Value + delta, 0, MaxScroll);
    }

    private void OnWheel(object? sender, MouseEventArgs e) =>
        ScrollBy(-Math.Sign(e.Delta) * TileHeight);

    private void OnPaintCanvas(object? sender, PaintEventArgs e)
    {
        var cols = Columns;
        var offset = _scroll.Enabled ? _scroll.Value : 0;
        var firstRow = Math.Max(0, (offset - Pad) / TileHeight);
        var lastRow = (offset + _canvas.ClientSize.Height) / TileHeight + 1;
        using var nameFont = new Font(Font.FontFamily, 8f);

        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var index = row * cols + col;
                if (index >= _files.Count) return;
                var file = _files[index];
                var x = Pad + col * TileWidth;
                var y = Pad + row * TileHeight - offset;

                var thumbRect = new Rectangle(x + (TileWidth - _thumbs.ThumbSize) / 2, y, _thumbs.ThumbSize, _thumbs.ThumbSize);
                if (!_thumbs.DrawCached(e.Graphics, file.FilePath, thumbRect))
                {
                    e.Graphics.FillRectangle(SystemBrushes.ControlLight, thumbRect);
                    e.Graphics.DrawRectangle(SystemPens.ControlDark, thumbRect);
                    QueueLoad(file.FilePath);
                }

                var nameRect = new Rectangle(x + 2, y + _thumbs.ThumbSize + 4, TileWidth - 4, TileHeight - _thumbs.ThumbSize - 8);
                TextRenderer.DrawText(e.Graphics, file.FileName, nameFont, nameRect, SystemColors.ControlText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    // Load queue lives on the UI thread; only the actual decodes run on workers.
    private void QueueLoad(string path)
    {
        if (_cts.IsCancellationRequested || !_queued.Add(path)) return;
        _pending.Enqueue(path);
        Pump();
    }

    private async void Pump()
    {
        if (_pumping) return;
        _pumping = true;
        try
        {
            while (_pending.Count > 0 && !_cts.IsCancellationRequested)
            {
                var batch = new List<string>(BatchSize);
                while (batch.Count < BatchSize && _pending.Count > 0)
                    batch.Add(_pending.Dequeue());
                try
                {
                    await Task.WhenAll(batch.Select(p => _thumbs.EnsureLoadedAsync(p, _cts.Token)));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Individual failures fall back to placeholder thumbnails.
                }
                foreach (var p in batch) _queued.Remove(p); // allow re-request after LRU eviction
                if (!IsDisposed) _canvas.Invalidate();
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
        }
    }
}
