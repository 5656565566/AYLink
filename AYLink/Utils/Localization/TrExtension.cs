using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;

namespace AYLink.Utils.Localization;

public class Locator
{
    public static LocalizationManager Localizer => LocalizationManager.Instance;
}

public class TrExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var staticResource = new StaticResourceExtension("Locator");
        if (staticResource.ProvideValue(serviceProvider) is not Locator)
        {
            throw new InvalidOperationException("Fatal Error: The 'Locator' resource was not found.");
        }

        var localizationManager = Locator.Localizer;

        var binding = new Binding
        {
            Source = localizationManager,
            Path = $"Strings[{Key}]",
            Mode = BindingMode.OneWay
        };

        return binding;
    }
}