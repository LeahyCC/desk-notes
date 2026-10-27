namespace DeskNotes;

/// <summary>Where a note sits in the window stack. Mirrors Rainmeter's "Position" menu.</summary>
public enum ZMode
{
    StayTopmost,
    Topmost,
    Normal,
    Bottom,
    OnDesktop,
}

public sealed class Note
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public double W { get; set; } = 260;
    public double H { get; set; } = 200;
    public string Color { get; set; } = "#202020";
    public double FontSize { get; set; } = 14;
    public ZMode Z { get; set; } = ZMode.Normal;
    public bool ClickThrough { get; set; }
    public bool Locked { get; set; }
    /// <summary>How see-through the note is at rest, 0 to 100. Editing always goes near solid.</summary>
    public int Opacity { get; set; } = 55;
    public string Text { get; set; } = "";
    /// <summary>Frontmatter lines the app does not use, such as Obsidian tags. Written back as found.</summary>
    public List<string> OtherFrontmatter { get; } = new();
}
