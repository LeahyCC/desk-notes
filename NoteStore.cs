using System.Globalization;
using System.Text;

namespace DeskNotes;

/// <summary>
/// One markdown file per note under %APPDATA%\DeskNotes\notes. Geometry and look live in a
/// small frontmatter block so the files stay readable in any editor or an Obsidian vault.
/// Frontmatter lines the app does not use (tags, aliases) are kept and written back.
/// Deleting moves the file to a trash folder; nothing here hard-deletes.
/// </summary>
public static class NoteStore
{
    private static string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskNotes");

    public static string Root => _root;
    public static string NotesDir => Path.Combine(_root, "notes");
    public static string TrashDir => Path.Combine(_root, "trash");

    /// <summary>Tests point the store at a temp folder. The app keeps the AppData default.</summary>
    internal static void SetRoot(string path) => _root = path;

    // Best-effort. A failure here must not take the notes down with it.
    internal static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(Path.Combine(Root, "error.log"), $"{DateTime.Now:s} {message}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log sits beside the notes. If that folder cannot be written, there is nowhere else to put it.
        }
    }

    public static List<Note> LoadAll()
    {
        Directory.CreateDirectory(NotesDir);
        var list = new List<Note>();
        foreach (var file in Directory.EnumerateFiles(NotesDir, "*.md"))
        {
            try
            {
                list.Add(Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)));
            }
            catch (Exception ex)
            {
                // One bad file must not stop every other note loading.
                Log($"Could not load {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return list;
    }

    // Written beside the note, flushed, then swapped in, so a crash or power cut mid-write
    // leaves the old file whole instead of a truncated one.
    public static void Save(Note n)
    {
        Directory.CreateDirectory(NotesDir);
        var path = PathFor(n);
        var tmp = path + ".tmp";
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(Encoding.UTF8.GetBytes(Format(n)));
            f.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static void Trash(Note n)
    {
        var src = PathFor(n);
        if (!File.Exists(src)) return;
        Directory.CreateDirectory(TrashDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        File.Move(src, Path.Combine(TrashDir, $"{n.Id}-{stamp}.md"), overwrite: true);
    }

    private static string PathFor(Note n) => Path.Combine(NotesDir, n.Id + ".md");

    private static string Format(Note n)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append(CultureInfo.InvariantCulture, $"x: {n.X:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"y: {n.Y:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"w: {n.W:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"h: {n.H:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"color: {n.Color}\n");
        sb.Append(CultureInfo.InvariantCulture, $"font: {n.FontSize:0.#}\n");
        sb.Append(CultureInfo.InvariantCulture, $"z: {n.Z}\n");
        if (n.ClickThrough) sb.Append("clickthrough: true\n");
        if (n.Locked) sb.Append("locked: true\n");
        sb.Append(CultureInfo.InvariantCulture, $"opacity: {n.Opacity}\n");
        foreach (var line in n.OtherFrontmatter) sb.Append(line).Append('\n');
        sb.Append("---\n");
        // The TextBox types CRLF for Enter while the frontmatter is LF. One line ending per file.
        sb.Append(n.Text.Replace("\r\n", "\n"));
        return sb.ToString();
    }

    private static Note Parse(string raw, string id)
    {
        var n = new Note { Id = id };
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        // Frontmatter only counts once it is closed. A note that just opens with a --- rule is all text;
        // reading it as frontmatter would hide the text, and the next save would write it away.
        var end = lines[0] == "---" ? Array.IndexOf(lines, "---", 1) : -1;
        for (var i = 1; i < end; i++)
        {
            var colon = lines[i].IndexOf(':');
            // Indented lines belong to some other key (a YAML list, say), never to one of ours.
            var key = colon > 0 && !char.IsWhiteSpace(lines[i][0]) ? lines[i][..colon].Trim() : "";
            var val = Unquote(lines[i][(colon + 1)..].Trim());
            switch (key)
            {
                case "x": n.X = Num(val, n.X); break;
                case "y": n.Y = Num(val, n.Y); break;
                case "w": n.W = Num(val, n.W); break;
                case "h": n.H = Num(val, n.H); break;
                case "color": if (val.Length > 0) n.Color = val; break;
                case "font": n.FontSize = Num(val, n.FontSize); break;
                case "z": if (Enum.TryParse<ZMode>(val, true, out var z) && Enum.IsDefined(z)) n.Z = z; break;
                case "clickthrough": n.ClickThrough = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "locked": n.Locked = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case "opacity": n.Opacity = Math.Clamp((int)Num(val, n.Opacity), 0, 100); break;
                default: n.OtherFrontmatter.Add(lines[i]); break;
            }
        }
        n.Text = string.Join("\n", lines.Skip(end + 1));
        return n;
    }

    // YAML tools such as Obsidian quote values, so "#202020" has to read the same as #202020.
    private static string Unquote(string s) =>
        s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0] ? s[1..^1] : s;

    // NaN and Infinity parse as numbers but break WPF sizes, so they fall back like any other junk.
    private static double Num(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : fallback;
}
