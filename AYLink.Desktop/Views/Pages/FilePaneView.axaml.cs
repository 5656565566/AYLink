using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AYLink.Core.Models;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.Services.Notifications;
using AYLink.Desktop.ViewModels.Pages;
using AYLink.Desktop.Views;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.Desktop.Views.Pages;

internal sealed class FileDragDataTransfer : IDataTransfer
{
    public required FileTabViewModel Tab { get; init; }
    public required FilePaneViewModel Pane { get; init; }
    public required IReadOnlyList<FileSystemModel> Files { get; init; }
    public required FileSource Source { get; init; }
    public required string Directory { get; init; }
    IReadOnlyList<DataFormat> IDataTransfer.Formats => [];
    IReadOnlyList<IDataTransferItem> IDataTransfer.Items => [];
    void IDisposable.Dispose() { }
}

public partial class FilePaneView : UserControl
{
    public static readonly StyledProperty<FilePaneViewModel?> PaneProperty = AvaloniaProperty.Register<FilePaneView, FilePaneViewModel?>(nameof(Pane));
    public static readonly StyledProperty<FileTabViewModel?> TabProperty = AvaloniaProperty.Register<FilePaneView, FileTabViewModel?>(nameof(Tab));
    public static readonly StyledProperty<bool> ShowLayoutToggleProperty = AvaloniaProperty.Register<FilePaneView, bool>(nameof(ShowLayoutToggle));
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<FilePaneView, bool>(nameof(IsCompact));
    public FilePaneViewModel? Pane { get => GetValue(PaneProperty); set => SetValue(PaneProperty, value); }
    public FileTabViewModel? Tab { get => GetValue(TabProperty); set => SetValue(TabProperty, value); }
    public bool ShowLayoutToggle { get => GetValue(ShowLayoutToggleProperty); set => SetValue(ShowLayoutToggleProperty, value); }
    public bool IsCompact { get => GetValue(IsCompactProperty); private set => SetValue(IsCompactProperty, value); }

    private PointerPressedEventArgs? _dragPress;
    private FileSystemModel? _dragFile;
    private Point _dragPosition;
    private FileSystemModel? _pendingSingleSelection;
    private double _zoomWheelDelta;
    private bool _scrollBreadcrumbs = true;
    private TopLevel? _pathEditingRoot;

    public FilePaneView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Pane_PointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Pane_PointerMoved);
        AddHandler(PointerReleasedEvent, Pane_PointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, (_, _) => { _dragPress = null; _dragFile = null; _pendingSingleSelection = null; });
        FilesList.AddHandler(PointerWheelChangedEvent, Files_PointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, Pane_DragOver);
        AddHandler(DragDrop.DropEvent, Pane_Drop);
        AddHandler(KeyDownEvent, Pane_KeyDown);
        GotFocus += (_, args) => { if (!IsLayoutControl(args.Source as Control) && Pane != null) Tab?.ActivatePane(Pane); };
        BreadcrumbBar.LayoutUpdated += (_, _) =>
        {
            if (!_scrollBreadcrumbs) return;
            _scrollBreadcrumbs = false;
            BreadcrumbScroll.Offset = new Vector(Math.Max(0, BreadcrumbScroll.Extent.Width - BreadcrumbScroll.Viewport.Width), 0);
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !IsVisible) { SearchButton?.Flyout?.Hide(); SetPathEditing(false); }
        if (change.Property == PaneProperty)
        {
            SearchButton?.Flyout?.Hide();
            SetPathEditing(false);
            if (change.OldValue is FilePaneViewModel old) old.PropertyChanged -= Pane_PropertyChanged;
            if (Pane != null) Pane.PropertyChanged += Pane_PropertyChanged;
            UpdateView();
        }
        if (change.Property == BoundsProperty) { IsCompact = Bounds.Width < 380; _scrollBreadcrumbs = true; }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        SearchButton.Flyout?.Hide();
        SetPathEditing(false);
        base.OnDetachedFromVisualTree(args);
        if (Pane != null) Pane.PropertyChanged -= Pane_PropertyChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        if (Pane != null) { Pane.PropertyChanged -= Pane_PropertyChanged; Pane.PropertyChanged += Pane_PropertyChanged; }
        UpdateView();
    }

    private void Pane_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FilePaneViewModel.ViewMode)) UpdateView();
        if (args.PropertyName == nameof(FilePaneViewModel.CurrentPath)) { _scrollBreadcrumbs = true; SetPathEditing(false); }
        if (args.PropertyName is nameof(FilePaneViewModel.SelectedFile) or nameof(FilePaneViewModel.SelectedCount) or nameof(FilePaneViewModel.HasCurrentListing)) UpdateContextMenu();
    }

    private void UpdateView()
    {
        if (FilesList == null || Pane == null) return;
        var mode = Pane.ViewMode;
        FilesList.ItemTemplate = (IDataTemplate)Resources[mode + "Template"]!;
        FilesList.ItemsPanel = (ITemplate<Panel?>)Resources[mode == FileViewMode.Details ? "DetailsPanel" : "WrapPanel"]!;
        DetailsHeader.IsVisible = mode == FileViewMode.Details;
        ViewButton.Content = new FASymbolIcon { Symbol = mode == FileViewMode.Icons ? FASymbol.AllApps : mode == FileViewMode.List ? FASymbol.BulletList : FASymbol.List };
        UpdateContextMenu();
    }

    private void UpdateContextMenu()
    {
        if (FilesList == null || Pane == null) return;
        FilesList.ContextFlyout = Pane.SelectedFile is { } file && Pane.CanOperate(file) ? FileMenu(file) : null;
    }

    private static string Text(string key, string fallback) => LocalizationManager.Instance.GetString("FilePage." + key, fallback);

    private MenuFlyout ChoiceMenu((string Label, bool Selected, Action Choose)[] options, string group = "View")
    {
        var menu = new MenuFlyout();
        foreach (var option in options)
        {
            var item = new MenuItem { Header = option.Label, ToggleType = MenuItemToggleType.Radio, GroupName = group, IsChecked = option.Selected };
            item.Click += (_, _) => option.Choose();
            menu.Items.Add(item);
        }
        return menu;
    }

    private void ViewMenu_Click(object? sender, RoutedEventArgs args)
    {
        if (Pane == null || sender is not Control button) return;
        ChoiceMenu([
            (Text("Details", "详细信息"), Pane.ViewMode == FileViewMode.Details, () => Pane.ViewMode = FileViewMode.Details),
            (Text("List", "列表"), Pane.ViewMode == FileViewMode.List, () => Pane.ViewMode = FileViewMode.List),
            (Text("Icons", "图标"), Pane.ViewMode == FileViewMode.Icons, () => Pane.ViewMode = FileViewMode.Icons)
        ]).ShowAt(button);
    }

    private void SortMenu_Click(object? sender, RoutedEventArgs args)
    {
        if (Pane == null || sender is not Control button) return;
        var menu = ChoiceMenu([
            (Text("Name", "名称"), Pane.SortColumn == FileSortColumn.Name, () => Pane.SortColumn = FileSortColumn.Name),
            (Text("Type", "类型"), Pane.SortColumn == FileSortColumn.Type, () => Pane.SortColumn = FileSortColumn.Type),
            (Text("Size", "大小"), Pane.SortColumn == FileSortColumn.Size, () => Pane.SortColumn = FileSortColumn.Size)
        ], "SortColumn");
        menu.Items.Add(new Separator());
        var directionMenu = ChoiceMenu([
            (Text("Ascending", "升序"), !Pane.SortDescending, () => Pane.SortDescending = false),
            (Text("Descending", "降序"), Pane.SortDescending, () => Pane.SortDescending = true)
        ], "Direction");
        var directionItems = directionMenu.Items.Cast<object>().ToArray();
        directionMenu.Items.Clear();
        foreach (var item in directionItems) menu.Items.Add(item);
        menu.ShowAt(button);
    }

    private void SortBy(FileSortColumn column)
    {
        if (Pane == null) return;
        Pane.SortDescending = Pane.SortColumn == column && !Pane.SortDescending;
        Pane.SortColumn = column;
    }
    private void NameSort_Click(object? sender, RoutedEventArgs args) => SortBy(FileSortColumn.Name);
    private void TypeSort_Click(object? sender, RoutedEventArgs args) => SortBy(FileSortColumn.Type);
    private void SizeSort_Click(object? sender, RoutedEventArgs args) => SortBy(FileSortColumn.Size);
    private void SinglePane_Click(object? sender, RoutedEventArgs args) { if (Tab != null) Tab.IsDualPane = false; }
    private void DualPane_Click(object? sender, RoutedEventArgs args) { if (Tab != null) Tab.IsDualPane = true; }

    private void SetPathEditing(bool editing)
    {
        if (PathInput == null) return;
        _pathEditingRoot?.RemoveHandler(PointerPressedEvent, PathEditing_PointerPressed);
        _pathEditingRoot = null;
        PathInput.IsVisible = editing; BreadcrumbScroll.IsVisible = !editing;
        if (editing)
        {
            // 点击空白处不一定转移焦点，编辑期间额外监听整窗点击。
            _pathEditingRoot = TopLevel.GetTopLevel(this);
            _pathEditingRoot?.AddHandler(PointerPressedEvent, PathEditing_PointerPressed, RoutingStrategies.Tunnel, true);
            PathInput.Text = Pane?.CurrentPath; PathInput.Focus(); PathInput.SelectAll();
        }
    }
    private void EditPath_Click(object? sender, RoutedEventArgs args) => SetPathEditing(true);
    private void PathInput_LostFocus(object? sender, RoutedEventArgs args) => SetPathEditing(false);
    private void PathEditing_PointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (PathInput.IsVisible && args.Source is Visual source && source != PathInput && !source.GetVisualAncestors().Contains(PathInput))
            SetPathEditing(false);
    }
    private async void PathInput_KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { SetPathEditing(false); EditPathButton.Focus(); args.Handled = true; }
        if (args.Key == Key.Enter && Pane != null && !string.IsNullOrWhiteSpace(PathInput.Text))
        {
            args.Handled = true; await Pane.NavigatePathAsync(PathInput.Text); SetPathEditing(false); EditPathButton.Focus();
        }
    }
    private async void Breadcrumb_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is Control { DataContext: FilePathSegment segment } && Pane != null) await Pane.NavigatePathAsync(segment.Path);
    }
    private void SearchFlyout_Opened(object? sender, EventArgs args)
    {
        SearchInput.Focus();
        SearchInput.SelectAll();
    }
    private void ClearSearch_Click(object? sender, RoutedEventArgs args) { if (Pane != null) Pane.SearchText = string.Empty; SearchInput.Focus(); }

    private MenuFlyout FileMenu(FileSystemModel file)
    {
        var menu = new MenuFlyout();
        if (Pane == null || Tab == null) return menu;
        var pane = Pane; var tab = Tab;
        var files = pane.SelectedFiles.Contains(file) ? pane.SelectedFiles.ToArray() : [file];
        var single = files.Length == 1;
        var source = pane.SelectedSource; var directory = pane.CurrentPath;
        void Add(string label, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += async (_, _) =>
            {
                if (files.Any(entry => !pane.CanOperate(entry)) || !ReferenceEquals(source, pane.SelectedSource) || directory != pane.CurrentPath) return;
                try { await action(); }
                catch (Exception ex) { NotificationService.Instance.ShowError(Text("ActionFailed", "文件操作失败"), ex.Message); }
            };
            menu.Items.Add(item);
        }
        if (single && file.IsDirectory)
        {
            Add(Text("CtxOpen", "打开"), () => pane.NavigateTo(file));
            Add(Text("OpenInNewTab", "在新标签页打开"), () =>
            {
                if (this.FindAncestorOfType<FilePage>()?.ViewModel is { } page) page.OpenDirectoryInNewTab(tab, pane, file);
                else if (TopLevel.GetTopLevel(this) is DetachedTabWindow window) window.AddTab(tab.CreateDirectoryTab(pane, file));
                return Task.CompletedTask;
            });
        }
        Add(single ? Text("CopyPath", "复制路径") : Text("CopyPaths", "复制所选路径"), async () =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(string.Join(Environment.NewLine, files.Select(pane.GetEntryPath)));
        });
        if (tab.IsDualPane)
            Add(Text(ReferenceEquals(pane, tab.LeftPane) ? "CtxCopyToRight" : "CtxCopyToLeft", ReferenceEquals(pane, tab.LeftPane) ? "复制到右侧" : "复制到左侧"),
                () => tab.TransferFilesAsync(pane, files), files.All(entry => tab.CanTransfer(pane, entry)));
        else if (source?.IsLocal == false && files.All(entry => !entry.IsDirectory))
            Add(Text("DownloadToLocal", "下载到本地…"), () => DownloadFilesAsync(files));
        menu.Items.Add(new Separator());
        Add(Text("RenameTitle", "重命名"), () => RenameAsync(file), single);
        Add(Text("CtxDelete", "删除"), () => pane.DeleteFilesAsync(files));
        return menu;
    }

    private async void Files_DoubleTapped(object? sender, TappedEventArgs args)
    {
        if ((args.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext is FileSystemModel file && Pane?.CanOperate(file) == true)
        { await Pane.NavigateTo(file); args.Handled = true; }
    }

    private async Task RenameAsync(FileSystemModel file)
    {
        if (Pane?.CanOperate(file) != true) return;
        var pane = Pane; var source = pane.SelectedSource; var directory = pane.CurrentPath;
        var input = new TextBox { Text = file.Name, MinWidth = 260 };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
        var content = new StackPanel { Spacing = 8, Children = { input, error } };
        var dialog = new FAContentDialog
        {
            Title = Text("RenameTitle", "重命名"), Content = content,
            PrimaryButtonText = LocalizationManager.Instance.GetString("Dialog.OK", "确定"),
            CloseButtonText = LocalizationManager.Instance.GetString("Dialog.Cancel", "取消"), DefaultButton = FAContentDialogButton.Primary
        };
        input.TextChanged += (_, _) =>
        {
            error.Text = pane.ValidateRename(file, input.Text ?? string.Empty);
            error.IsVisible = error.Text != null; dialog.IsPrimaryButtonEnabled = error.Text == null;
        };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this)!);
        if (result == FAContentDialogResult.Primary && ReferenceEquals(source, pane.SelectedSource) && directory == pane.CurrentPath)
            await pane.RenameFileAsync(file, input.Text ?? string.Empty);
    }

    private async Task DownloadAsync(FileSystemModel file)
    {
        if (Pane?.CanOperate(file) != true || Tab == null || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage) return;
        var pane = Pane; var tab = Tab; var source = pane.SelectedSource; var directory = pane.CurrentPath;
        var target = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Text("DownloadToLocal", "下载到本地…"), SuggestedFileName = file.Name });
        if (target?.TryGetLocalPath() is { } path && ReferenceEquals(source, pane.SelectedSource) && directory == pane.CurrentPath)
            await tab.DownloadToLocalAsync(pane, file, path);
    }

    private async Task DownloadFilesAsync(IReadOnlyList<FileSystemModel> files)
    {
        if (files.Count == 1) { await DownloadAsync(files[0]); return; }
        if (Pane == null || Tab == null || TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return;
        var pane = Pane; var tab = Tab; var source = pane.SelectedSource; var directory = pane.CurrentPath;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Text("DownloadToLocal", "下载到本地…"), AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } target && ReferenceEquals(source, pane.SelectedSource) && directory == pane.CurrentPath)
            await tab.DownloadFilesToLocalAsync(pane, files, target);
    }

    private void Files_PointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        if (Pane == null || !args.KeyModifiers.HasFlag(KeyModifiers.Control)) { _zoomWheelDelta = 0; return; }
        args.Handled = true;
        Tab?.ActivatePane(Pane);
        _zoomWheelDelta += args.Delta.Y;
        while (Math.Abs(_zoomWheelDelta) >= 1)
        {
            var increase = _zoomWheelDelta > 0;
            Pane.ChangeZoom(increase);
            _zoomWheelDelta += increase ? -1 : 1;
        }
    }

    private bool IsLayoutControl(Control? source) => source == LayoutToggle || source?.GetVisualAncestors().Any(ancestor => ancestor == LayoutToggle) == true;
    private void Pane_PointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (Pane == null || IsLayoutControl(args.Source as Control)) return;
        Tab?.ActivatePane(Pane);
        var item = (args.Source as Control)?.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is not FileSystemModel file || !Pane.CanOperate(file))
        {
            if (IsFileListControl(args.Source as Control) && args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                Pane.SetSelection([]);
            return;
        }
        if (args.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (!Pane.SelectedFiles.Contains(file)) Pane.SetSelection([file]);
            FilesList.ContextFlyout = FileMenu(file);
        }
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (args.Source as Control)?.FindAncestorOfType<Button>() == null)
        {
            _dragPress = args; _dragFile = file; _dragPosition = args.GetPosition(this);
            // Keep a selected group while a drag is still possible; an ordinary click collapses it on release.
            if (args.KeyModifiers == KeyModifiers.None && Pane.SelectedCount > 1 && Pane.SelectedFiles.Contains(file))
            {
                _pendingSingleSelection = file; item.Focus(); args.Pointer.Capture(FilesList); args.Handled = true;
            }
        }
    }

    private void Pane_PointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_pendingSingleSelection is { } file && Pane?.CanOperate(file) == true && args.InitialPressMouseButton == MouseButton.Left)
        { Pane.SetSelection([file]); args.Handled = true; }
        _dragPress = null; _dragFile = null; _pendingSingleSelection = null;
        if (args.Pointer.Captured == FilesList) args.Pointer.Capture(null);
    }

    private bool IsFileListControl(Control? source) => source == FilesList || source?.GetVisualAncestors().Any(ancestor => ancestor == FilesList) == true;
    private async void Pane_PointerMoved(object? sender, PointerEventArgs args)
    {
        if (_dragPress == null || _dragFile == null || Pane?.CanOperate(_dragFile) != true || !Pane.SelectedFiles.Contains(_dragFile) || Tab?.IsDualPane != true) return;
        var settings = Application.Current?.PlatformSettings;
        if (settings == null) return;
        var distance = args.GetPosition(this) - _dragPosition;
        var tapSize = settings.GetTapSize(args.Pointer.Type);
        if (Math.Abs(distance.X) <= tapSize.Width && Math.Abs(distance.Y) <= tapSize.Height) return;
        var press = _dragPress; var files = Pane.SelectedFiles.ToArray();
        _dragPress = null; _dragFile = null; _pendingSingleSelection = null;
        await DragDrop.DoDragDropAsync(press, new FileDragDataTransfer { Tab = Tab, Pane = Pane, Files = files, Source = Pane.SelectedSource!, Directory = Pane.CurrentPath }, DragDropEffects.Copy);
    }
    private bool CanDrop(FileDragDataTransfer data) => Tab == data.Tab && Pane != data.Pane && Pane != null &&
        ReferenceEquals(data.Source, data.Pane.SelectedSource) && data.Directory == data.Pane.CurrentPath && data.Files.Count > 0 && data.Files.All(file => Tab.CanTransfer(data.Pane, file));
    private void Pane_DragOver(object? sender, DragEventArgs args) => args.DragEffects = args.DataTransfer is FileDragDataTransfer data && CanDrop(data) ? DragDropEffects.Copy : DragDropEffects.None;
    private async void Pane_Drop(object? sender, DragEventArgs args)
    {
        if (args.DataTransfer is not FileDragDataTransfer data || !CanDrop(data)) return;
        args.Handled = true;
        await Tab!.TransferFilesAsync(data.Pane, data.Files);
    }
    private async void Pane_KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Source is TextBox or ComboBox || (args.Source as Control)?.FindAncestorOfType<TextBox>() != null ||
            (args.Source as Control)?.FindAncestorOfType<ComboBox>() != null) return;
        if (Pane == null || !IsFileListControl(args.Source as Control)) return;
        if (args.Key == Key.A && args.KeyModifiers.HasFlag(KeyModifiers.Control)) { args.Handled = true; Pane.SetSelection(Pane.VisibleFiles); return; }
        if (args.Key == Key.Escape) { args.Handled = true; Pane.SetSelection([]); return; }
        if (Pane?.SelectedFile is not { } file || !Pane.CanOperate(file)) return;
        if (args.Key == Key.F2) { args.Handled = true; if (Pane.SelectedCount == 1) await RenameAsync(file); }
        if (args.Key == Key.Delete) { args.Handled = true; await Pane.DeleteFilesAsync(Pane.SelectedFiles.ToArray()); }
        if (args.Key == Key.F10 && args.KeyModifiers.HasFlag(KeyModifiers.Shift)) { args.Handled = true; FileMenu(file).ShowAt(FilesList); }
        if (args.Key == Key.Enter && Pane.SelectedCount == 1) { args.Handled = true; await Pane.NavigateTo(file); }
    }
}
