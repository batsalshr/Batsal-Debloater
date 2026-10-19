using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value switch
        {
            null => false,
            string s => s.Length > 0,
            System.Collections.ICollection c => c.Count > 0,
            int i => i > 0,
            _ => true
        };
        if (parameter as string == "invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BytesConverter : IValueConverter
{
    public static string Format(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (Math.Abs(bytes) >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{bytes:0.#} {units[i]}";
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return "n/a";
        double d = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return Format(d) + (parameter as string == "rate" ? "/s" : "");
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Maps status/risk enums to theme brushes so colours stay consistent everywhere.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool soft = parameter as string == "soft";
        string key = value switch
        {
            TweakState.Applied => "Success",
            TweakState.Partial => "Warning",
            TweakState.Unsupported => "Neutral",
            TweakState.Error => "Danger",
            TweakState.NotApplied => "Accent",
            RiskLevel.Low => "Success",
            RiskLevel.Medium => "Warning",
            RiskLevel.High => "Danger",
            RiskLevel.Expert => "Purple",
            Severity.High => "Danger",
            Severity.Medium => "Warning",
            Severity.Low => "Accent",
            Severity.Info => "Neutral",
            ChangeStatus.Applied or ChangeStatus.Restored or ChangeStatus.RolledBack => "Success",
            ChangeStatus.PendingRestart => "Warning",
            ChangeStatus.Failed or ChangeStatus.RollbackFailed or ChangeStatus.RestoreFailed => "Danger",
            OperationStatus.Completed => "Success",
            OperationStatus.CompletedWithErrors or OperationStatus.RolledBack => "Warning",
            OperationStatus.Blocked => "Danger",
            RestorePointStatus.Created => "Success",
            RestorePointStatus.Throttled or RestorePointStatus.NotRequired => "Warning",
            RestorePointStatus => "Danger",
            ServiceCategory.Essential => "Danger",
            ServiceCategory.Recommended => "Warning",
            ServiceCategory.Optional => "Success",
            ServiceCategory.HardwareDependent => "Purple",
            ServiceCategory.ApplicationDependent => "Accent",
            bool b => b ? "Success" : "Neutral",
            _ => "Neutral"
        };
        if (key == "Neutral") return soft ? B("NeutralSoftBrush") : B("TextDimBrush");
        return soft ? B(key + "SoftBrush") : B(key + "Brush");
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        return System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PercentWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double pct || values[1] is not double width) return 0d;
        return Math.Max(0, Math.Min(1, pct / 100.0)) * width;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
