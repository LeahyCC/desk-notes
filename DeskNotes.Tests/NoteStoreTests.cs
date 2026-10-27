namespace DeskNotes.Tests;

public sealed class NoteStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "desknotes-tests", Guid.NewGuid().ToString("N"));

    public NoteStoreTests() => NoteStore.SetRoot(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The locked-file test can still be holding the file. The next run uses a new folder.
        }
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var note = new Note
        {
            Text = "hello",
            X = -40,
            Y = 80,
            W = 300,
            H = 120,
            Color = "#1E2A36",
            FontSize = 18.5,
            Z = ZMode.OnDesktop,
            ClickThrough = true,
            Locked = true,
            Opacity = 30,
        };
        note.OtherFrontmatter.Add("tags: [a, b]");

        NoteStore.Save(note);
        var loaded = Only();

        Assert.Equal(note.Id, loaded.Id);
        Assert.Equal("hello", loaded.Text);
        Assert.Equal(-40, loaded.X);
        Assert.Equal(80, loaded.Y);
        Assert.Equal(300, loaded.W);
        Assert.Equal(120, loaded.H);
        Assert.Equal("#1E2A36", loaded.Color);
        Assert.Equal(18.5, loaded.FontSize);
        Assert.Equal(ZMode.OnDesktop, loaded.Z);
        Assert.True(loaded.ClickThrough);
        Assert.True(loaded.Locked);
        Assert.Equal(30, loaded.Opacity);
        Assert.Equal(new[] { "tags: [a, b]" }, loaded.OtherFrontmatter);

        NoteStore.Save(loaded);
        Assert.Equal(new[] { "tags: [a, b]" }, Only().OtherFrontmatter);
    }

    [Fact]
    public void Save_uses_line_feeds()
    {
        var note = new Note { Text = "a\r\nb" };
        NoteStore.Save(note);

        var raw = File.ReadAllText(Path.Combine(NoteStore.NotesDir, note.Id + ".md"));
        Assert.DoesNotContain("\r", raw);
        Assert.Equal("a\nb", Only().Text);
    }

    [Fact]
    public void Unclosed_frontmatter_stays_text()
    {
        Write("plain", "---\nhello\n");
        Assert.Equal("---\nhello\n", Only().Text);
    }

    [Fact]
    public void Quoted_colour_and_unknown_keys_load()
    {
        Write("obs", "---\ncolor: \"#FFFFFF\"\nz: ondesktop\ntags: [a]\n---\nhi");

        var note = Only();
        Assert.Equal("#FFFFFF", note.Color);
        Assert.Equal(ZMode.OnDesktop, note.Z);
        Assert.Equal(new[] { "tags: [a]" }, note.OtherFrontmatter);
        Assert.Equal("hi", note.Text);
        Assert.Equal(55, note.Opacity);
        Assert.False(note.ClickThrough);
        Assert.False(note.Locked);
    }

    [Fact]
    public void Bad_numbers_fall_back()
    {
        Write("bad", "---\nx: NaN\nw: Infinity\nopacity: 200\nz: sideways\n---\n");

        var note = Only();
        Assert.Equal(120, note.X);
        Assert.Equal(260, note.W);
        Assert.Equal(100, note.Opacity);
        Assert.Equal(ZMode.Normal, note.Z);
    }

    [Fact]
    public void Opacity_below_zero_clamps()
    {
        Write("dim", "---\nopacity: -5\n---\n");
        Assert.Equal(0, Only().Opacity);
    }

    [Fact]
    public void Trash_removes_the_note_from_the_folder()
    {
        var note = new Note { Text = "gone" };
        NoteStore.Save(note);
        NoteStore.Trash(note);

        Assert.Empty(NoteStore.LoadAll());
        var trashed = Directory.GetFiles(NoteStore.TrashDir, "*.md");
        var file = Assert.Single(trashed);
        Assert.Contains("gone", File.ReadAllText(file));
    }

    [Fact]
    public void A_locked_file_is_logged_and_the_rest_load()
    {
        var good = new Note { Text = "ok" };
        NoteStore.Save(good);
        var bad = Path.Combine(NoteStore.NotesDir, "locked.md");
        using (new FileStream(bad, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var loaded = NoteStore.LoadAll();
            var note = Assert.Single(loaded);
            Assert.Equal("ok", note.Text);
            Assert.Contains("locked.md", File.ReadAllText(Path.Combine(NoteStore.Root, "error.log")));
        }
    }

    private static Note Only()
    {
        var all = NoteStore.LoadAll();
        return Assert.Single(all);
    }

    private static void Write(string id, string contents)
    {
        Directory.CreateDirectory(NoteStore.NotesDir);
        File.WriteAllText(Path.Combine(NoteStore.NotesDir, id + ".md"), contents);
    }
}
