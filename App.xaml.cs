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

    protected override void OnExit(ExitEventArgs e)
    {
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
        NoteStore.Trash(w.Note);
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
            Icon = MakeIcon(),
            Text = "DeskNotes",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => NewNote();
    }

    // Drawn at runtime so the project has no binary assets to look after.
    private static Drawing.Icon MakeIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.Clear(Drawing.Color.Transparent);
        using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x2A, 0x2A, 0x2A));
        using var edge = new Drawing.Pen(Drawing.Color.FromArgb(0x9A, 0x9A, 0x9A));
        g.FillRectangle(fill, 5, 5, 22, 22);
        g.DrawRectangle(edge, 5, 5, 21, 21);
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
