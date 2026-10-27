using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static DeskNotes.Native;

namespace DeskNotes;

/// <summary>
/// Lets the mouse fall through a note to whatever is underneath, except for a strip along the top.
///
/// Windows only offers click-through for a whole window (WS_EX_TRANSPARENT), and a window that has it
/// never hears about the mouse again. So while the mode is on, a timer polls the cursor and flips the
/// style off whenever the cursor is over the strip, and back on when it leaves. The strip is the only
/// way back out of the mode, via the right-click menu.
/// </summary>
internal sealed class ClickThrough
{
    private readonly Window _window;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private IntPtr _hwnd;
    private bool _transparent;

    public bool Enabled { get; private set; }
    public double StripHeight { get; set; } = 18;

    public ClickThrough(Window window)
    {
        _window = window;
        _poll.Tick += (_, _) => Poll();
        _window.Closed += (_, _) => _poll.Stop();
    }

    public void Set(bool on)
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) return;
        Enabled = on;
        if (on)
        {
            Poll();
            _poll.Start();
        }
        else
        {
            _poll.Stop();
            SetTransparent(false);
        }
    }

    private void Poll()
    {
        if (!GetCursorPos(out var p) || !GetWindowRect(_hwnd, out var r)) return;
        var strip = StripHeight * VisualTreeHelper.GetDpi(_window).DpiScaleY;
        var overStrip = p.x >= r.left && p.x < r.right && p.y >= r.top && p.y < r.top + strip;
        SetTransparent(!overStrip);
    }

    private void SetTransparent(bool transparent)
    {
        if (transparent == _transparent) return;
        _transparent = transparent;
        var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, transparent ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
        // Windows caches the style. Without this, click-through waits for the next move.
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}
