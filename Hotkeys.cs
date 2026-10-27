using System.Windows.Interop;
using static DeskNotes.Native;

namespace DeskNotes;

/// <summary>
/// System-wide shortcuts. They live on a message-only window so they work whatever has focus.
/// If another program already owns a combination, that one is skipped quietly rather than failing
/// the whole app; the tray menu still offers the same actions.
/// </summary>
internal sealed class Hotkeys : IDisposable
{
    private const int VK_N = 0x4E;
    private const int VK_H = 0x48;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();

    public bool NewNoteRegistered { get; }
    public bool ToggleRegistered { get; }

    public Hotkeys(Action newNote, Action toggleAll)
    {
        var p = new HwndSourceParameters("DeskNotes.Hotkeys") { WindowStyle = 0, ParentWindow = HWND_MESSAGE };
        _source = new HwndSource(p);
        _source.AddHook(Hook);

        NewNoteRegistered = Register(1, MOD_CONTROL | MOD_ALT, VK_N, newNote);
        ToggleRegistered = Register(2, MOD_CONTROL | MOD_ALT, VK_H, toggleAll);
    }

    private bool Register(int id, uint mods, int vk, Action action)
    {
        if (!RegisterHotKey(_source.Handle, id, mods | MOD_NOREPEAT, (uint)vk)) return false;
        _actions[id] = action;
        return true;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue((int)wParam, out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _source.Dispose();
    }
}
