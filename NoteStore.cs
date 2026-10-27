using System.Globalization;
using System.Text;

namespace DeskNotes;

/// <summary>
/// One markdown file per note under %APPDATA%\DeskNotes\notes. Geometry and look live in a
/// small frontmatter block so the files stay readable in any editor or an Obsidian vault.
/// Deleting moves the file to a trash folder; nothing here hard-deletes.
/// </summary>
public static class NoteStore
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskNotes");
    public static readonly string NotesDir = Path.Combine(Root, "notes");
    public static readonly string TrashDir = Path.Combine(Root, "trash");

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
            catch
            {
                // A hand-edited file with broken frontmatter must not stop every other note loading.
            }
        }
        return list;
    }

    public static void Save(Note n)
    {
        Directory.CreateDirectory(NotesDir);
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append(CultureInfo.InvariantCulture, $"x: {n.X:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"y: {n.Y:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"w: {n.W:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"h: {n.H:0}\n");
        sb.Append(CultureInfo.InvariantCulture, $"color: {n.Color}\n");
        sb.Append(CultureInfo.InvariantCulture, $"font: {n.FontSize:0.#}\n");
        sb.Append(CultureInfo.InvariantCulture, $"z: {n.Z}\n");
        sb.Append("---\n");
        sb.Append(n.Text);
        File.WriteAllText(PathFor(n), sb.ToString());
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

    private static Note Parse(string raw, string id)
    {
        var n = new Note { Id = id };
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        if (lines.Length > 0 && lines[0] == "---")
        {
            for (i = 1; i < lines.Length && lines[i] != "---"; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon < 0) continue;
                var key = lines[i][..colon].Trim();
                var val = lines[i][(colon + 1)..].Trim();
                switch (key)
                {
                    case "x": n.X = Num(val, n.X); break;
                    case "y": n.Y = Num(val, n.Y); break;
                    case "w": n.W = Num(val, n.W); break;
                    case "h": n.H = Num(val, n.H); break;
                    case "color": if (val.Length > 0) n.Color = val; break;
                    case "font": n.FontSize = Num(val, n.FontSize); break;
                    case "z": if (Enum.TryParse<ZMode>(val, true, out var z)) n.Z = z; break;
                }
            }
            i++; // skip the closing ---
        }
        n.Text = string.Join("\n", lines.Skip(i));
        return n;
    }

    private static double Num(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;
}
