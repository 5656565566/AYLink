using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AYLink.Desktop.Services.Localization;

/// <summary>
/// 将翻译格式串与一个绑定参数组合，语言或参数变化时重新格式化。
/// </summary>
public class TrFExtension : TrExtension
{
    public TrFExtension() { }

    public TrFExtension(string key) : base(key) { }

    [AssignBinding]
    public BindingBase? Argument { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Argument == null)
            throw new InvalidOperationException("TrF requires an Argument binding.");

        return new MultiBinding
        {
            Bindings = { (BindingBase)base.ProvideValue(serviceProvider), Argument },
            Converter = FormatConverter.Instance,
            Mode = BindingMode.OneWay
        };
    }

    private sealed class FormatConverter : IMultiValueConverter
    {
        public static FormatConverter Instance { get; } = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count != 2 || values[0] is not string format || values[1] == AvaloniaProperty.UnsetValue)
                return AvaloniaProperty.UnsetValue;

            try
            {
                return string.Format(culture, format, values[1]);
            }
            catch (FormatException ex)
            {
                return new BindingNotification(ex, BindingErrorType.Error);
            }
        }
    }
}
