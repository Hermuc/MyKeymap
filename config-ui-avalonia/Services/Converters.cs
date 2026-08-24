using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace MyKeymap.Settings.Services;

/// <summary>int <-> 字符串 (触发延时等数值输入框; 解析失败时拒绝写回)。</summary>
public sealed class IntToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? i.ToString(culture) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && int.TryParse(s.Trim(), NumberStyles.Integer, culture, out var v)
            ? v
            : AvaloniaProperty.UnsetValue;
}

/// <summary>bool 取反 (如 HasSubKeymap -> 上层下拉禁用)。</summary>
public sealed class BoolNotConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>字符串非空判定 (SaveNotice 等短暂提示的显隐)。</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
