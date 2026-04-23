using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace AYLink.Controls;

/// <summary>
/// 临时修复 Avalonia 11.3.x 中 ComboBox 弹出层关闭时报 "PlatformImpl is null" 警告的问题
/// 实际上 12.x 也没修复
/// 修复方法来自: https://github.com/AvaloniaUI/Avalonia/issues/19892
/// </summary>
public class SafeComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);

    private IPointer? _pointer;

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointer = e.Pointer;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SelectedIndexProperty)
        {
            _pointer?.Capture(null);
        }
        
        base.OnPropertyChanged(change);
    }
}