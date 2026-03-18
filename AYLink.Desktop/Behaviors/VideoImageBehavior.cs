using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AYLink.Desktop.ViewModels.Pages;
using System;
using System.Diagnostics;

namespace AYLink.Desktop.Behaviors;

/// <summary>
/// 视频控件自注册行为（Attached Property）
/// 当 Image 被挂载到可视树时 自动从 DataContext 获取 ScreenTabViewModel
/// 并调用 AttachVideoImage 从可视树卸载时自动 DetachVideoImage
/// </summary>
public static class VideoImageBehavior
{
    /// <summary>
    /// 附加属性 - 设为 True 即启用自注册行为
    /// </summary>
    public static readonly AttachedProperty<bool> IsAutoAttachProperty =
        AvaloniaProperty.RegisterAttached<Image, bool>(
            "IsAutoAttach",
            typeof(VideoImageBehavior),
            defaultValue: false);

    public static bool GetIsAutoAttach(Image element) =>
        element.GetValue(IsAutoAttachProperty);

    public static void SetIsAutoAttach(Image element, bool value) =>
        element.SetValue(IsAutoAttachProperty, value);

    static VideoImageBehavior()
    {
        IsAutoAttachProperty.Changed.AddClassHandler<Image>(OnIsAutoAttachChanged);
    }

    private static void OnIsAutoAttachChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            image.AttachedToVisualTree += OnAttachedToVisualTree;
            image.DetachedFromVisualTree += OnDetachedFromVisualTree;

            // 如果设置属性时控件已经在可视树上（例如模板重用）立即尝试 Attach
            if (image.GetVisualRoot() != null)
            {
                TryAttach(image);
            }
        }
        else
        {
            image.AttachedToVisualTree -= OnAttachedToVisualTree;
            image.DetachedFromVisualTree -= OnDetachedFromVisualTree;

            TryDetach(image);
        }
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Image image)
        {
            TryAttach(image);
        }
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Image image)
        {
            TryDetach(image);
        }
    }


    private static void TryAttach(Image image)
    {
        if (image.DataContext is ScreenTabViewModel vm)
        {
            vm.AttachVideoImage(image);
        }
    }

    private static void TryDetach(Image image)
    {
        if (image.DataContext is ScreenTabViewModel vm)
        {
            vm.DetachVideoImage();
        }
    }
}
