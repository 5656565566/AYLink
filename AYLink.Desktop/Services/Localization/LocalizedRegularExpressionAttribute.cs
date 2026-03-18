using System.ComponentModel.DataAnnotations;

namespace AYLink.Desktop.Services.Localization;

public class LocalizedRegularExpressionAttribute : RegularExpressionAttribute
{
    private readonly string _errorMessageKey;

    private readonly string _defaultMessage;

    public LocalizedRegularExpressionAttribute(string pattern, string errorMessageKey, string defaultMessage = "") : base(pattern)
    {
        _errorMessageKey = errorMessageKey;
        _defaultMessage = defaultMessage;
    }

    public override string FormatErrorMessage(string name)
    {
        var localized = LocalizationManager.Instance[_errorMessageKey];
        return string.IsNullOrEmpty(localized) ? _defaultMessage : localized;
    }
}
