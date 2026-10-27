using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DeskNotes;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "DeskNotes";

    public static new App Current => (App)Application.Current;

    private readonly List<NoteWindow> _open = new();
    private Mutex? _single;
    private Forms.NotifyIcon? _tray;
    private Hotkeys? _hotkeys;
    private bool _hidden;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, "DeskNotes-single-instance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var notes = NoteStore.LoadAll();
        if (notes.Count == 0)
            notes.Add(new Note { Text = "Right-click for layer, colour and size.\nDrag by the edge. Ctrl+wheel resizes text." });

        foreach (var n in notes) Open(n);
        _hotkeys = new Hotkeys(() => NewNote(atCursor: true), ToggleAll);
        BuildTray();
    }

    // Windows can end the process soon after this returns, before OnExit gets a turn.
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        foreach (var w in _open) w.FlushSave();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var w in _open) w.FlushSave();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private void Open(Note n)
    {
        var w = new NoteWindow(n);
        _open.Add(w);
        w.Show();
    }

    public void NewNote(NoteWindow? near = null, bool atCursor = false)
    {
        var n = new Note();
        if (atCursor && Native.GetCursorPos(out var p))
        {
            // Cursor is in physical pixels; WPF wants device-independent ones.
            var scale = Native.GetDpiForSystem() / 96.0;
            n.X = p.x / scale;
            n.Y = p.y / scale;
        }
        if (near is not null)
        {
            n.X = near.Left + 30;
            n.Y = near.Top + 30;
            n.W = near.Width;
            n.H = near.Height;
            n.Color = near.Note.Color;
            n.FontSize = near.Note.FontSize;
            n.Z = near.Note.Z;
        }
        NoteStore.Save(n);
        Open(n);
    }

    public void Delete(NoteWindow w)
    {
        w.FlushSave(); // so the copy in the trash has the last few keystrokes too
        try
        {
            NoteStore.Trash(w.Note);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not move the note to the trash folder.\n\n{ex.Message}", "DeskNotes");
            return;
        }
        w.CancelSave(); // a retry still waiting would write the note straight back
        _open.Remove(w);
        w.Close();
    }

    private void ToggleAll()
    {
        _hidden = !_hidden;
        foreach (var w in _open) w.Visibility = _hidden ? Visibility.Hidden : Visibility.Visible;
    }

    private static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is { } exe) key.SetValue(RunName, "\"" + exe + "\"");
            else key.DeleteValue(RunName, throwOnMissingValue: false);
        }
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        var fresh = new Forms.ToolStripMenuItem("New note", null, (_, _) => NewNote());
        if (_hotkeys?.NewNoteRegistered == true) fresh.ShortcutKeyDisplayString = "Ctrl+Alt+N";
        menu.Items.Add(fresh);
        var toggle = new Forms.ToolStripMenuItem("Show / hide all", null, (_, _) => ToggleAll());
        if (_hotkeys?.ToggleRegistered == true) toggle.ShortcutKeyDisplayString = "Ctrl+Alt+H";
        menu.Items.Add(toggle);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var startup = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartsWithWindows };
        startup.CheckedChanged += (_, _) => StartsWithWindows = startup.Checked;
        menu.Items.Add(startup);

        menu.Items.Add("Open notes folder", null, (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", NoteStore.NotesDir));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIcon(),
            Text = "DeskNotes",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => NewNote();
    }

    // The same icon the exe carries. Asking for the small icon size makes Windows pick the frame
    // drawn for the tray (16, 20 or 24 px by display scale) instead of shrinking a big one.
    private static Drawing.Icon TrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/DeskNotes.ico")).Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
