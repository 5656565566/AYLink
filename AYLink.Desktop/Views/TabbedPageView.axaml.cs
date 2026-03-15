using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AYLink.Desktop.ViewModels;
using FluentAvalonia.UI.Controls;

namespace AYLink.Desktop.Views;

/// <summary>
/// 通用标签页视图控件
/// 提供 TabView + 空状态 + 状态栏 的统一布局
/// 通过 StyledProperty 注入具体的内容模板和状态栏模板
/// </summary>
public partial class TabbedPageView : UserControl
{
    #region Styled Properties

    /// <summary>
    /// Tab 内容模板 - 每个标签页内部渲染的 DataTemplate
    /// </summary>
    public static readonly StyledProperty<Avalonia.Controls.Templates.IDataTemplate?> TabContentTemplateProperty =
        AvaloniaProperty.Register<TabbedPageView, Avalonia.Controls.Templates.IDataTemplate?>(nameof(TabContentTemplate));

    public Avalonia.Controls.Templates.IDataTemplate? TabContentTemplate
    {
        get => GetValue(TabContentTemplateProperty);
        set => SetValue(TabContentTemplateProperty, value);
    }

    /// <summary>
    /// 状态栏模板 - 底部状态栏的 DataTemplate（DataContext 为 SelectedTab）
    /// </summary>
    public static readonly StyledProperty<Avalonia.Controls.Templates.IDataTemplate?> StatusBarTemplateProperty =
        AvaloniaProperty.Register<TabbedPageView, Avalonia.Controls.Templates.IDataTemplate?>(nameof(StatusBarTemplate));

    public Avalonia.Controls.Templates.IDataTemplate? StatusBarTemplate
    {
        get => GetValue(StatusBarTemplateProperty);
        set => SetValue(StatusBarTemplateProperty, value);
    }

    /// <summary>
    /// 是否显示底部状态栏（默认 true）
    /// </summary>
    public static readonly StyledProperty<bool> ShowStatusBarProperty =
        AvaloniaProperty.Register<TabbedPageView, bool>(nameof(ShowStatusBar), true);

    public bool ShowStatusBar
    {
        get => GetValue(ShowStatusBarProperty);
        set => SetValue(ShowStatusBarProperty, value);
    }

    /// <summary>
    /// 空状态图标内容（可以是 SymbolIcon、PathIcon 等任意 Control）
    /// </summary>
    public static readonly StyledProperty<object?> EmptyStateIconProperty =
        AvaloniaProperty.Register<TabbedPageView, object?>(nameof(EmptyStateIcon));

    public object? EmptyStateIcon
    {
        get => GetValue(EmptyStateIconProperty);
        set => SetValue(EmptyStateIconProperty, value);
    }

    #endregion

    public TabbedPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 统一的标签页关闭处理 - 调用 TabItemViewModelBase.CloseTabCommand
    /// </summary>
    private void TabView_TabCloseRequested(TabView _, TabViewTabCloseRequestedEventArgs args)
    {
        // FluentAvalonia TabView 的 args.Item 可能是 ViewModel 或 TabViewItem
        if (args.Item is TabItemViewModelBase tabVm)
        {
            tabVm.CloseTabCommand.Execute(null);
        }
        else if (args.Tab?.DataContext is TabItemViewModelBase tabVm2)
        {
            tabVm2.CloseTabCommand.Execute(null);
        }
    }
}