using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Hakaru.Common;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => (v is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => v is Visibility vis && vis == Visibility.Visible;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is bool b && !b;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => v is bool b && !b;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => (v is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>0〜1 の比率を、指定した最大幅（px、ConverterParameter）に対する幅に変換します。</summary>
public class RatioToWidthConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        double r = v switch { double d => d, float f => f, _ => 0 };
        double max = 200;
        if (p is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var m)) max = m;
        r = Math.Clamp(r, 0, 1);
        return Math.Max(0.0, r * max);
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
