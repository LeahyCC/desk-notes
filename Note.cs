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
    public string Text { get; set; } = "";
}
