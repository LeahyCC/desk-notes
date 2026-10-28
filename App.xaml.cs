using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DeskNotes;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "DeskNotes";
    private const string MutexName = "DeskNotes-single-instance";
    private const string ShowEventName = "DeskNotes-show";
    private const string ReplaceEventName = "DeskNotes-replace";
    private const string RunningFileName = "running.txt";

    public static new App Current => (App)Application.Current;

    private readonly List<NoteWindow> _open = new();
    private Mutex? _single;
    private EventWaitHandle? _show;
    private EventWaitHandle? _replace;
    private Forms.NotifyIcon? _tray;
    private Hotkeys? _hotkeys;
    private bool _hidden;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Events exist before the mutex, so a second launch can signal as soon as it sees we are running.
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _replace = new EventWaitHandle(false, EventResetMode.AutoReset, ReplaceEventName);
        _single = new Mutex(true, MutexName, out var first);
        if (!first && !TakeOver())
        {
            Shutdown();
            return;
        }

        PublishRunningPath();
        ClearStaleStartup();
        // Before the notes load, so a replace signal during startup still quits this copy.
        new Thread(WatchSignals) { IsBackground = true, Name = "DeskNotes.Signals" }.Start();

        base.OnStartup(e);

        // A crash with no window is invisible; leave a trail next to the notes.
        DispatcherUnhandledException += (_, args) =>
        {
            NoteStore.Log(args.Exception.ToString());
            // The app is about to close; keep what was typed in the last moment before it goes.
            foreach (var w in _open) w.FlushSave();
        };

        var notes = NoteStore.LoadAll();
        if (notes.Count == 0)
            notes.Add(new Note { Text = "Right-click for layer, colour and size.\nDrag by the edge. Ctrl+wheel resizes text." });

        foreach (var n in notes) Open(n);
        _hotkeys = new Hotkeys(() => NewNote(atCursor: true), ToggleAll);
        BuildTray();
    }

    // Same exe: show the notes. A different file should be the one left running, so ask this copy to quit.
    // A build from before this signal never hears it, so give up and show the notes instead of waiting.
    private bool TakeOver()
    {
        if (!OtherExeIsRunning())
        {
            _show?.Set();
            return false;
        }

        try
        {
            _replace?.Set();
            _single?.Dispose();
            _single = new Mutex(false, MutexName);
            try
            {
                if (_single.WaitOne(TimeSpan.FromSeconds(5))) return true;
            }
            catch (AbandonedMutexException)
            {
                // The other copy closed without releasing the mutex. This process owns it now.
                return true;
            }

            _show?.Set();
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return false;
        }
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
        ClearRunningPath();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    // The thread is background, so quitting does not wait on it. Shutdown is safe from this thread.
    private void WatchSignals()
    {
        if (_show is null || _replace is null) return;
        while (!Dispatcher.HasShutdownStarted)
        {
            var which = WaitHandle.WaitAny(new WaitHandle[] { _show, _replace });
            if (Dispatcher.HasShutdownStarted) return;
            if (which == 0)
                Dispatcher.BeginInvoke(ShowAll);
            else
            {
                Shutdown();
                return;
            }
        }
    }

    private void ShowAll()
    {
        if (Dispatcher.HasShutdownStarted) return;
        _hidden = false;
        foreach (var w in _open) w.Visibility = Visibility.Visible;
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
            return key?.GetValue(RunName) is string registered && RegisteredExe(registered);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is { } exe) key.SetValue(RunName, "\"" + exe + "\"");
            else key.DeleteValue(RunName, throwOnMissingValue: false);
        }
    }

    // The menu hides the tick when the path is an old copy. Leaving the value would still start that copy at login.
    private static void ClearStaleStartup()
    {
        if (Environment.ProcessPath is null) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(RunName) is not string registered || RegisteredExe(registered)) return;
            key.DeleteValue(RunName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            NoteStore.Log($"Could not update Start with Windows: {ex.Message}");
        }
    }

    private static bool RegisteredExe(string registered) => SameExePath(registered, Environment.ProcessPath);

    /// <summary>Paths match ignoring case and the quotes the Run key stores.</summary>
    internal static bool SameExePath(string? recorded, string? current)
    {
        if (string.IsNullOrWhiteSpace(recorded) || string.IsNullOrWhiteSpace(current)) return false;
        return string.Equals(recorded.Trim().Trim('"'), current.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
    }

    private static string RunningPathFile => Path.Combine(NoteStore.Root, RunningFileName);

    private static void PublishRunningPath()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            Directory.CreateDirectory(NoteStore.Root);
            File.WriteAllText(RunningPathFile, exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteStore.Log($"Could not record the running exe: {ex.Message}");
        }
    }

    // Only remove the file if it still names this exe, so a copy that just took over keeps its own line.
    private static void ClearRunningPath()
    {
        try
        {
            if (!File.Exists(RunningPathFile)) return;
            if (!SameExePath(File.ReadAllText(RunningPathFile), Environment.ProcessPath)) return;
            File.Delete(RunningPathFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Quit still matters more than removing the marker. The next launch overwrites it.
        }
    }

    private static bool OtherExeIsRunning()
    {
        var recorded = ReadRunningPath();
        // The owner writes the file just after it takes the mutex. A second launch can land in that gap.
        if (recorded is null)
        {
            Thread.Sleep(200);
            recorded = ReadRunningPath();
        }
        return recorded is not null && !SameExePath(recorded, Environment.ProcessPath);
    }

    private static string? ReadRunningPath()
    {
        try
        {
            return File.Exists(RunningPathFile) ? File.ReadAllText(RunningPathFile) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = NoteStore.NotesDir,
                UseShellExecute = true,
            }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIcon(),
            Text = TrayText(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => NewNote();
    }

    // Release builds use the tag. A local build stays at 1.0.0, which the contributing notes already mention.
    private static string TrayText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "DeskNotes" : $"DeskNotes {version.ToString(3)}";
    }

    // The same icon the exe carries. Asking for the small icon size makes Windows pick the frame
    // drawn for the tray (16, 20 or 24 px by display scale) instead of shrinking a big one.
    private static Drawing.Icon TrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/DeskNotes.ico")).Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
