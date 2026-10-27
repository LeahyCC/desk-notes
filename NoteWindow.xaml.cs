using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeskNotes;

public partial class NoteWindow : Window
{
    // Base tints. Alpha is added at runtime: 55% glass at rest, near-solid while being edited.
    private static readonly (string Name, string Hex)[] Palette =
    {
        ("Dark", "#202020"),
        ("Slate", "#1E2A36"),
        ("Moss", "#1E2F27"),
        ("Plum", "#2A1E33"),
        ("Clay", "#33261E"),
        ("Light", "#FFFFFF"),
    };

    private static readonly Dictionary<ZMode, Color> DotColors = new()
    {
        [ZMode.StayTopmost] = Color.FromRgb(0xFF, 0x6A, 0x3D),
        [ZMode.Topmost] = Color.FromRgb(0xFF, 0xB9, 0x00),
        [ZMode.Bottom] = Color.FromRgb(0x6A, 0xA1, 0xFF),
        [ZMode.OnDesktop] = Color.FromRgb(0x6F, 0xCF, 0x97),
    };

    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(150));

    public Note Note { get; }

    private readonly ZOrder _z;
    private readonly ClickThrough _clickThrough;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly SolidColorBrush _fill = new();
    private readonly SolidColorBrush _edge = new();

    public NoteWindow(Note note)
    {
        InitializeComponent();
        Note = note;

        Left = note.X;
        Top = note.Y;
        Width = note.W;
        Height = note.H;
        Editor.Text = note.Text;
        Editor.FontSize = note.FontSize;
        Frame.Background = _fill;
        Frame.BorderBrush = _edge;
        ApplyColor(animate: false);

        _z = new ZOrder(this);
        _z.Apply(note.Z);
        // The layer is only applied once the window handle exists, so the dot must wait for that too.
        _clickThrough = new ClickThrough(this);
        SourceInitialized += (_, _) =>
        {
            RefreshDot();
            ApplyClickThrough();
        };
        ContextMenu = BuildMenu();

        _save.Tick += (_, _) =>
        {
            _save.Stop();
            NoteStore.Save(Note);
        };
        LocationChanged += (_, _) => RememberGeometry();
        SizeChanged += (_, _) => RememberGeometry();
        Activated += (_, _) => ApplyColor(animate: true);
        Deactivated += (_, _) => ApplyColor(animate: true);
    }

    // ---- persistence ---------------------------------------------------------------------------

    private void QueueSave()
    {
        _save.Stop();
        _save.Start();
    }

    private void RememberGeometry()
    {
        if (!IsLoaded) return;
        Note.X = Left;
        Note.Y = Top;
        Note.W = Width;
        Note.H = Height;
        QueueSave();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        Note.Text = Editor.Text;
        QueueSave();
    }

    // ---- look ------------------------------------------------------------------------------------

    private static bool IsLightTint(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 128;

    private void ApplyColor(bool animate)
    {
        var tint = (Color)ColorConverter.ConvertFromString(Note.Color);
        var light = IsLightTint(tint);

        // Glass at rest so the wallpaper shows through; almost solid while focused so text is readable.
        byte alpha = IsActive ? (byte)0xF2 : light ? (byte)0xA6 : (byte)0x8C;
        var fill = Color.FromArgb(alpha, tint.R, tint.G, tint.B);
        var edge = light ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);

        if (animate)
        {
            _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fill, Fade));
            _edge.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(edge, Fade));
        }
        else
        {
            _fill.Color = fill;
            _edge.Color = edge;
        }

        var ink = light ? Color.FromArgb(0xE4, 0, 0, 0) : Color.FromRgb(0xF2, 0xF2, 0xF2);
        Editor.Foreground = new SolidColorBrush(ink);
        Editor.CaretBrush = Editor.Foreground;
        Editor.SelectionBrush = new SolidColorBrush(light ? Color.FromRgb(0x6A, 0x86, 0x95) : Color.FromRgb(0x88, 0x9E, 0xAA));
        Editor.SelectionOpacity = 0.35;
        Strip.Background = new SolidColorBrush(light ? Color.FromArgb(0x18, 0, 0, 0) : Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    }

    private void RefreshDot()
    {
        if (DotColors.TryGetValue(_z.Mode, out var c))
        {
            Dot.Fill = new SolidColorBrush(c);
            Dot.ToolTip = ZOrder.Label(_z.Mode);
            Dot.Visibility = Visibility.Visible;
        }
        else
        {
            Dot.Visibility = Visibility.Collapsed;
        }
    }

    private void SetZ(ZMode mode)
    {
        _z.Apply(mode);
        Note.Z = _z.Mode;
        RefreshDot();
        ContextMenu = BuildMenu();
        QueueSave();
    }

    private void SetColor(string hex)
    {
        Note.Color = hex;
        ApplyColor(animate: true);
        ContextMenu = BuildMenu();
        QueueSave();
    }

    private void SetClickThrough(bool on)
    {
        Note.ClickThrough = on;
        ApplyClickThrough();
        ContextMenu = BuildMenu();
        QueueSave();
    }

    // While the mouse falls through, the editor must not grab keyboard focus from a click on the strip.
    private void ApplyClickThrough()
    {
        _clickThrough.Set(Note.ClickThrough);
        Editor.IsHitTestVisible = !Note.ClickThrough;
        Editor.Focusable = !Note.ClickThrough;
        Strip.Visibility = Note.ClickThrough ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetFontSize(double size)
    {
        Note.FontSize = Math.Clamp(size, 9, 48);
        Editor.FontSize = Note.FontSize;
        // Keep the line box in step with the glyphs; the 20/14 ratio is the Win11 body text spec.
        TextBlock.SetLineHeight(Editor, Math.Round(Note.FontSize * 20 / 14));
        QueueSave();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var layer = new MenuItem { Header = "Layer" };
        foreach (var mode in Enum.GetValues<ZMode>())
        {
            var item = new MenuItem { Header = ZOrder.Label(mode), IsCheckable = true, IsChecked = mode == Note.Z };
            item.Click += (_, _) => SetZ(mode);
            layer.Items.Add(item);
        }
        menu.Items.Add(layer);

        var colour = new MenuItem { Header = "Colour" };
        foreach (var (name, hex) in Palette)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = hex == Note.Color };
            item.Click += (_, _) => SetColor(hex);
            colour.Items.Add(item);
        }
        menu.Items.Add(colour);

        var text = new MenuItem { Header = "Text" };
        var bigger = new MenuItem { Header = "Bigger", InputGestureText = "Ctrl+Wheel" };
        bigger.Click += (_, _) => SetFontSize(Note.FontSize + 1);
        var smaller = new MenuItem { Header = "Smaller" };
        smaller.Click += (_, _) => SetFontSize(Note.FontSize - 1);
        text.Items.Add(bigger);
        text.Items.Add(smaller);
        menu.Items.Add(text);

        var through = new MenuItem { Header = "Click-through", IsCheckable = true, IsChecked = Note.ClickThrough,
            ToolTip = "Mouse passes through the note. Right-click the strip at the top to turn it off." };
        through.Click += (_, _) => SetClickThrough(through.IsChecked);
        menu.Items.Add(through);

        menu.Items.Add(new Separator());

        var fresh = new MenuItem { Header = "New note" };
        fresh.Click += (_, _) => App.Current.NewNote(this);
        menu.Items.Add(fresh);

        var delete = new MenuItem { Header = "Delete note (to trash folder)" };
        delete.Click += (_, _) => App.Current.Delete(this);
        menu.Items.Add(delete);

        return menu;
    }

    // ---- input -----------------------------------------------------------------------------------

    // Clicks inside the TextBox never reach here (it handles them), so anything that does is the
    // padding around the text. That padding is the drag handle; there is no title bar.
    private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Frame_MouseEnter(object sender, MouseEventArgs e) =>
        Dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, Fade));

    private void Frame_MouseLeave(object sender, MouseEventArgs e) =>
        Dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, Fade));

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetFontSize(Note.FontSize + (e.Delta > 0 ? 1 : -1));
        e.Handled = true;
    }
}
