using Avalonia.Controls;
using AYLink.Desktop.Models;
using System.Collections.Generic;
using System.Linq;

namespace AYLink.Desktop.Views.Dialogs;

public partial class InputDialog : UserControl
{
    private readonly List<InputFieldModel> _fields;
    private readonly Dictionary<string, TextBox> _textBoxes = new();

    public InputDialog()
    {
        InitializeComponent();
        _fields = new List<InputFieldModel>();
    }

    public InputDialog(string description, List<InputFieldModel> fields)
    {
        InitializeComponent();
        _fields = fields;

        if (!string.IsNullOrEmpty(description))
        {
            InputContainer.Children.Add(new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }

        foreach (var field in _fields)
        {
            var textBox = new TextBox
            {
                Watermark = field.Watermark,
                Text = field.Value,
                UseFloatingWatermark = true,
                Classes = { "clearButton" }
            };

            if (!string.IsNullOrEmpty(field.Label))
            {
                // 如果有 Label，可以添加一个 TextBlock 或者使用 FloatingWatermark
                // 这里我们使用 FloatingWatermark，所以不需要额外的 TextBlock
                // 但为了更清晰，我们可以设置 InnerLeftContent 或者直接依赖 Watermark
            }

            _textBoxes[field.Key] = textBox;
            InputContainer.Children.Add(textBox);
        }
    }

    public Dictionary<string, string> GetResults()
    {
        return _textBoxes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Text ?? string.Empty);
    }
}