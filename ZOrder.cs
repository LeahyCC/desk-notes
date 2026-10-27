using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static DeskNotes.Native;

namespace DeskNotes;

/// <summary>
/// Puts one window into one of five layers, the way Rainmeter does for skins.
///
///   StayTopmost  topmost, re-asserted every second so other topmost windows cannot cover it
///   Topmost      plain topmost
///   Normal       ordinary window
///   Bottom       every time Windows tries to raise it, the request is rewritten to HWND_BOTTOM
///   OnDesktop    Bottom, plus it climbs above the desktop while Show Desktop (Win+D) is active
///
/// Re-parenting into the desktop window (the wallpaper-engine trick) was tried first and does not
/// render on Windows 11 24H2 or later, so OnDesktop uses the same scheme Rainmeter does: a hidden
/// marker window sits at the bottom of the stack, and a timer watches whether the desktop window
/// has jumped above it. That only happens during Show Desktop.
///
/// Notes never minimise, whatever the layer. A sticky note that vanishes on Win+M is no use.
/// </summary>
internal sealed class ZOrder
{
    private static readonly List<ZOrder> OnDesktopNotes = new();
    private static readonly DispatcherTimer DesktopWatch = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private static IntPtr _marker;
    private static bool _showDesktop;

    static ZOrder()
    {
        DesktopWatch.Tick += (_, _) => CheckShowDesktop();
    }

    private readonly Window _window;
    private readonly DispatcherTimer _reassert = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _hwnd;
    private ZMode _mode = ZMode.Normal;
    private ZMode _pending = ZMode.Normal;
    private bool _raised; // OnDesktop note currently lifted above the desktop

    public ZMode Mode => _mode;

    public ZOrder(Window window)
    {
        _window = window;
        _window.SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
            Apply(_pending);
        };
        _window.Closed += (_, _) => Leave(_mode);
        _reassert.Tick += (_, _) => Place(HWND_TOPMOST);
    }

    public void Apply(ZMode mode)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _pending = mode; // window not created yet; SourceInitialized calls back
            return;
        }

        Leave(_mode);
        _mode = mode;

        switch (mode)
        {
            case ZMode.StayTopmost:
                _window.Topmost = true;
                _reassert.Start();
                break;
            case ZMode.Topmost:
                _window.Topmost = true;
                break;
            case ZMode.Normal:
                break;
            case ZMode.Bottom:
                Place(HWND_BOTTOM);
                break;
            case ZMode.OnDesktop:
                Place(HWND_BOTTOM);
                OnDesktopNotes.Add(this);
                EnsureMarker();
                if (_showDesktop) Raise();
                DesktopWatch.Start();
                break;
        }
    }

    private void Leave(ZMode old)
    {
        _reassert.Stop();
        _window.Topmost = false;
        if (old == ZMode.OnDesktop)
        {
            if (_raised) Lower();
            OnDesktopNotes.Remove(this);
            if (OnDesktopNotes.Count == 0) DesktopWatch.Stop();
        }
    }

    private void Place(IntPtr insertAfter) =>
        SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private void Raise()
    {
        _raised = true;
        Place(HWND_TOPMOST);
    }

    private void Lower()
    {
        _raised = false;
        Place(HWND_NOTOPMOST);
        Place(HWND_BOTTOM);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_SYSCOMMAND when ((long)wParam & 0xFFF0) == SC_MINIMIZE:
                handled = true;
                break;

            case WM_WINDOWPOSCHANGING when (_mode == ZMode.Bottom || _mode == ZMode.OnDesktop) && !_raised:
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((pos.flags & SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
        }
        return IntPtr.Zero;
    }

    // ---- Show Desktop detection -------------------------------------------------------------------

    private static void EnsureMarker()
    {
        if (_marker != IntPtr.Zero) return;
        // A bare STATIC control works as a marker: no class to register, never shown, never activated.
        _marker = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "Static", "DeskNotes.Marker", WS_POPUP,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        SetWindowPos(_marker, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>The window that owns the icon grid. Progman on current builds, a WorkerW on older ones.</summary>
    private static IntPtr DesktopWindow()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            return progman;

        var worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            if (FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return worker;
        }
        return progman;
    }

    private static void CheckShowDesktop()
    {
        if (_marker == IntPtr.Zero) return;
        var desktop = DesktopWindow();
        if (desktop == IntPtr.Zero) return;

        // Normally the desktop is the lowest window of all, under our marker. During Show Desktop
        // Windows lifts it above every ordinary window, so walking upward from the marker finds it.
        var above = false;
        for (var h = GetWindow(_marker, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV))
        {
            if (h == desktop) { above = true; break; }
        }

        if (above == _showDesktop) return;
        _showDesktop = above;
        foreach (var note in OnDesktopNotes.ToArray())
        {
            if (above) note.Raise(); else note.Lower();
        }
    }

    public static string Label(ZMode m) => m switch
    {
        ZMode.StayTopmost => "Stay topmost",
        ZMode.Topmost => "Topmost",
        ZMode.Normal => "Normal",
        ZMode.Bottom => "Bottom",
        ZMode.OnDesktop => "On desktop",
        _ => m.ToString(),
    };
}
