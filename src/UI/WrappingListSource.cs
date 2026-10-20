using System.Collections;
using System.Collections.Specialized;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.Views;
using TguiAttr = Terminal.Gui.Drawing.Attribute;

namespace SttConsole.UI;

/// <summary>
/// Custom IListDataSource for ListView.
/// Splits items on '\n' and word-wraps lines that exceed the display column width
/// (double-width characters count as 2 columns). The cache is rebuilt on each
/// Render call when the width changes, so window resizes are handled automatically.
/// </summary>
public sealed class WrappingListSource(IApplication app, string indent = "") : IListDataSource
{
    private readonly IApplication _app = app;
    private readonly string _indent = indent;
    private readonly List<string> _items = [];
    private List<string> _rows = [];
    private int _lastWidth = -1;
    private bool _isDirty = true;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public bool SuspendCollectionChangedEvent { get; set; }

    // Before the first Render, assume width 80. After Render, return the real expanded row count.
    public int Count => GetRows(_lastWidth > 0 ? _lastWidth : 80).Count;
    public int MaxItemLength => _rows.Count > 0 ? _rows.Max(r => r.GetColumns(true)) : 0;

    public void Add(string item)
    {
        _items.Add(item);
        _isDirty = true;
        Notify();
    }

    public void Clear()
    {
        _items.Clear();
        _rows.Clear();
        _isDirty = true;
        Notify();
    }

    /// <summary>Fire a Reset notification regardless of SuspendCollectionChangedEvent (for batch updates).</summary>
    public void RaiseReset() =>
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    public void Dispose() { }
    public bool IsMarked(int item) => false;
    public void SetMark(int item, bool value) { }
    public bool RenderMark(ListView container, int col, int line, bool marked, bool isSelected) => false;
    public IList ToList() => _items;

    private void Notify()
    {
        if (!SuspendCollectionChangedEvent)
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private List<string> GetRows(int width)
    {
        if (!_isDirty && width == _lastWidth)
            return _rows;
        _isDirty = false;
        _lastWidth = width;
        _rows = [.. _items.SelectMany(s => Expand(s, width, _indent))];
        return _rows;
    }

    // Split on '\n', then wrap each line by display column width (double-width = 2 columns).
    // indent: prefix added to the second and subsequent wrapped lines.
    private static IEnumerable<string> Expand(string text, int width, string indent)
    {
        var indentLen = indent.Length; // assumed ASCII spaces, so columns == length
        var isFirstLine = true;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Replace("\r", ""); // strip \r to prevent cursor-jump overwrites via AddStr
            var prefix = isFirstLine ? "" : indent;
            var availWidth = isFirstLine ? width : Math.Max(1, width - indentLen);

            if (width <= 0 || line.GetColumns(true) <= availWidth)
            {
                yield return prefix + line;
                isFirstLine = false;
                continue;
            }

            var startIdx = 0;
            var isFirstChunk = true;
            while (startIdx < line.Length)
            {
                var isHead = isFirstLine && isFirstChunk;
                var pfx = isHead ? "" : indent;
                var budget = isHead ? width : Math.Max(1, width - indentLen);

                var colCount = 0;
                var endIdx = startIdx;
                while (endIdx < line.Length)
                {
                    Rune.DecodeFromUtf16(line.AsSpan(endIdx), out var r, out var consumed);
                    var cw = Math.Max(0, r.GetColumns());
                    if (colCount + cw > budget)
                        break;
                    colCount += cw;
                    endIdx += consumed;
                }

                if (endIdx == startIdx)
                {
                    // View is extremely narrow — force-advance by one character to avoid an infinite loop
                    Rune.DecodeFromUtf16(line.AsSpan(startIdx), out _, out var consumed);
                    endIdx = startIdx + consumed;
                }

                yield return pfx + line[startIdx..endIdx];
                startIdx = endIdx;
                isFirstChunk = false;
                isFirstLine = false;
            }
        }
    }

    // Byte length of the timestamp prefix "[HH:mm:ss]  " (ASCII, fixed)
    private const int TimestampLen = 12; // '[' + 8 + ']' + 2 spaces

    /// <summary>
    /// Map a foreground color to a dimmer variant, independent of the active theme.
    /// Named 16-colors use a bright→dark mapping; true colors use the fg/bg midpoint.
    /// </summary>
    private static Color DimColor(Color fg, Color bg)
    {
        // Named 16-color: bright/light → dark/dim
        (ColorName16 From, ColorName16 To)[] map =
        [
            (ColorName16.White,         ColorName16.Gray),
            (ColorName16.Gray,          ColorName16.DarkGray),
            (ColorName16.BrightYellow,  ColorName16.Yellow),
            (ColorName16.BrightMagenta, ColorName16.Magenta),
            (ColorName16.BrightRed,     ColorName16.Red),
            (ColorName16.BrightCyan,    ColorName16.Cyan),
            (ColorName16.BrightGreen,   ColorName16.Green),
            (ColorName16.BrightBlue,    ColorName16.Blue),
            (ColorName16.Black,         ColorName16.Gray),
        ];

        // Use GetClosestNamedColor16 instead of Equals — Equals includes alpha and is unreliable for named colors
        var closest = fg.GetClosestNamedColor16();
        foreach (var (from, to) in map)
        {
            if (closest == from)
                return new Color(to);
        }

        // True color: midpoint between foreground and background
        return new Color((fg.R + bg.R) / 2, (fg.G + bg.G) / 2, (fg.B + bg.B) / 2);
    }

    public void Render(ListView container, bool marked, int item, int col, int line, int width, int start)
    {
        var rows = GetRows(width);
        container.Move(col, line);

        var text = item < rows.Count ? rows[item] : "";

        // When start > 0, skip the first `start` display columns
        if (start > 0)
        {
            var skip = start;
            var charIdx = 0;
            while (charIdx < text.Length && skip > 0)
            {
                Rune.DecodeFromUtf16(text.AsSpan(charIdx), out var r, out var consumed);
                var cw = Math.Max(0, r.GetColumns());
                if (skip < cw)
                    break;
                skip -= cw;
                charIdx += consumed;
            }
            text = text[charIdx..];
        }

        // Render the "[HH:mm:ss]  " timestamp prefix in a dimmer color
        var hasTimestamp = text.Length >= TimestampLen
                            && text[0] == '[' && text[9] == ']';

        if (hasTimestamp && _app.Driver != null)
        {
            var drv = _app.Driver;
            var orig = drv.GetAttribute();
            var fg = orig.Foreground;
            var bg = orig.Background;
            var dim = new TguiAttr(DimColor(fg, bg), bg);

            drv.SetAttribute(dim);
            drv.AddStr(text[..TimestampLen]);
            drv.SetAttribute(orig);
            drv.AddStr(text[TimestampLen..]);
        }
        else
        {
            _app.Driver?.AddStr(text);
        }

        var pad = width - text.GetColumns(true);
        if (pad > 0)
            _app.Driver?.AddStr(new string(' ', pad));
    }
}
