namespace DeskNotes.Tests;

public sealed class ExePathTests
{
    [Theory]
    [InlineData(@"C:\Apps\DeskNotes.exe", @"C:\Apps\DeskNotes.exe", true)]
    [InlineData("\"C:\\Apps\\DeskNotes.exe\"", @"c:\apps\desknotes.exe", true)]
    [InlineData("\"C:\\Users\\Colin Leahy\\DeskNotes.exe\" ", @"C:\Users\Colin Leahy\DeskNotes.exe", true)]
    [InlineData(@"C:\Apps\DeskNotes-0.1.0.exe", @"C:\Apps\DeskNotes-0.2.0.exe", false)]
    [InlineData("  ", @"C:\Apps\DeskNotes.exe", false)]
    [InlineData(null, @"C:\Apps\DeskNotes.exe", false)]
    public void Same_path_ignores_case_quotes_and_surrounding_space(string? recorded, string? current, bool same)
    {
        Assert.Equal(same, App.SameExePath(recorded, current));
    }
}
