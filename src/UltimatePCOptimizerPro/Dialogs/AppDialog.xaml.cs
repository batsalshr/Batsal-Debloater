using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UltimatePCOptimizerPro.Dialogs;

public enum DialogResultKind { None, Primary, Secondary, Tertiary }
public enum DialogTone { Info, Success, Warning, Danger, Question }

public sealed class DialogRow
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public string? Detail { get; init; }
    public string? Badge { get; init; }
    public Brush? BadgeBackground { get; init; }
    public Brush? BadgeForeground { get; init; }
    public double Opacity { get; init; } = 1;
}

public sealed partial class AppDialogModel : ObservableObject
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Icon { get; init; } = "";
    public Brush? IconBackground { get; init; }
    public Brush? IconForeground { get; init; }
    public ObservableCollection<DialogRow> Items { get; init; } = new();
    public bool HasItems => Items.Count > 0;
    public double ListMaxHeight { get; init; } = 360;
    public string? Acknowledgement { get; init; }
    public string? TertiaryText { get; init; }
    public Style? PrimaryStyle { get; init; }

    [ObservableProperty] private string? _primaryText;
    [ObservableProperty] private string? _secondaryText;
    [ObservableProperty] private bool _acknowledged;

    public bool PrimaryEnabled => Acknowledgement is null || Acknowledged;
    partial void OnAcknowledgedChanged(bool value) => OnPropertyChanged(nameof(PrimaryEnabled));
}

public partial class AppDialog : Window
{
    public DialogResultKind Result { get; private set; } = DialogResultKind.None;
    private DispatcherTimer? _timer;

    public AppDialog(AppDialogModel model)
    {
        InitializeComponent();
        DataContext = model;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
    }

    /// <summary>Auto-selects <paramref name="onTimeout"/> after the given time, showing a countdown on the secondary button.</summary>
    public void StartCountdown(int seconds, DialogResultKind onTimeout)
    {
        var model = (AppDialogModel)DataContext;
        var baseText = model.SecondaryText ?? "";
        int remaining = seconds;
        model.SecondaryText = $"{baseText} ({remaining})";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            remaining--;
            model.SecondaryText = $"{baseText} ({remaining})";
            if (remaining <= 0) Close(onTimeout);
        };
        _timer.Start();
    }

    private void Close(DialogResultKind r)
    {
        _timer?.Stop();
        Result = r;
        Close();
    }

    private void OnPrimary(object sender, RoutedEventArgs e) => Close(DialogResultKind.Primary);
    private void OnSecondary(object sender, RoutedEventArgs e) => Close(DialogResultKind.Secondary);
    private void OnTertiary(object sender, RoutedEventArgs e) => Close(DialogResultKind.Tertiary);

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
