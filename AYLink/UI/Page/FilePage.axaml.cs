using AYLink.ADB;
using AYLink.UI.Themes;
using AYLink.UIModel;
using AYLink.Utils;
using AYLink.Utils.Localization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AYLink.UI;

// [新增] 创建一个数据结构来分离逻辑键和显示名
public record FileSourceItem(string Key, string DisplayName);

public partial class FilePage : UserControl
{
    private AdbFileManager? adbFileManager = null;
    private TreeView? _dragSourceTreeView;
    private bool _isTransferInProgress = false;

    private enum TransferOperationType { Upload, Download, Move, Unsupported }

    private record TransferDetails(
        string SourceFullPath,
        string DestinationFullPath,
        bool SourceIsLocal,
        bool TargetIsLocal,
        TreeView SourceTreeView,
        TreeView TargetTreeView,
        string SourcePath,
        string TargetPath
    );

    private string _leftPaht = string.Empty;
    private string _rightPaht = string.Empty;

    public FilePage()
    {
        InitializeComponent();
        InitializeSources();

        LeftTreeView.AddHandler(DragDrop.DragOverEvent, TreeView_DragOver);
        LeftTreeView.AddHandler(DragDrop.DropEvent, TreeView_Drop);
        RightTreeView.AddHandler(DragDrop.DragOverEvent, TreeView_DragOver);
        RightTreeView.AddHandler(DragDrop.DropEvent, TreeView_Drop);

        LeftPathTextBox.KeyDown += PathTextBox_KeyDown;
        RightPathTextBox.KeyDown += PathTextBox_KeyDown;

        LeftTreeView.DoubleTapped += TreeView_DoubleTapped;
        RightTreeView.DoubleTapped += TreeView_DoubleTapped;

        SetupContextMenuEvents();

        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;
    }

    // [新增] 当语言发生变化时，刷新UI上的文本
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 保存当前选择的键
        var leftKey = (LeftSourceComboBox.SelectedItem as FileSourceItem)?.Key;
        var rightKey = (RightSourceComboBox.SelectedItem as FileSourceItem)?.Key;

        // 重新初始化数据源，这会使用新的语言
        InitializeSources();

        // 恢复之前的选择
        if (leftKey != null)
        {
            LeftSourceComboBox.SelectedItem = (LeftSourceComboBox.ItemsSource as ObservableCollection<FileSourceItem>)
                ?.FirstOrDefault(item => item.Key == leftKey);
        }
        if (rightKey != null)
        {
            RightSourceComboBox.SelectedItem = (RightSourceComboBox.ItemsSource as ObservableCollection<FileSourceItem>)
                ?.FirstOrDefault(item => item.Key == rightKey);
        }
    }

    // ... SetupContextMenuEvents 和右键菜单部分保持不变 ...
    #region Unchanged Code Block 1
    private void SetupContextMenuEvents()
    {
        LeftCtxOpen.Click += LeftCtxOpen_Click;
        LeftCtxUpload.Click += LeftCtxUpload_Click;
        LeftCtxOpenPath.Click += (s, e) => FileSystemHelper.Open(_leftPaht);

        RightCtxUpload.Click += RightCtxUpload_Click;

        if (LeftTreeView.ContextFlyout is MenuFlyout leftFlyout)
        {
            leftFlyout.Opening += (s, e) =>
            {
                bool isValid = LeftTreeView.SelectedItem is FileSystemModel model && model.Name != "..";
                LeftCtxOpen.IsEnabled = isValid;
                LeftCtxUpload.IsEnabled = isValid;
            };
        }
        if (RightTreeView.ContextFlyout is MenuFlyout rightFlyout)
        {
            rightFlyout.Opening += (s, e) =>
            {
                bool isValid = RightTreeView.SelectedItem is FileSystemModel model && model.Name != "..";
                RightCtxUpload.IsEnabled = isValid;
            };
        }
    }

    #region 右键菜单
    private async Task HandleContextMenuTransfer(FileSystemModel sourceItem, TreeView sourceTreeView, TreeView targetTreeView, TransferOperationType operationType)
    {
        if (_isTransferInProgress)
        {
            await DialogHelper.MessageShowAsync(L.Tr("FilePage_TaskInProgress_Title"), L.Tr("FilePage_TaskInProgress_Message"));
            return;
        }
        if (adbFileManager == null)
        {
            await DialogHelper.MessageShowAsync(L.Tr("FilePage_NoDevice_Title"), L.Tr("FilePage_NoDevice_Message"));
            return;
        }
        if (sourceItem.IsDirectory)
        {
            await DialogHelper.MessageShowAsync(L.Tr("FilePage_OperationUnsupported_Title"), L.Tr("FilePage_DirectoryCopyUnsupported_Message"));
            return;
        }

        _isTransferInProgress = true;
        try
        {
            bool sourceIsLocal = sourceTreeView == LeftTreeView;
            string sourcePath = sourceIsLocal ? _leftPaht : _rightPaht;
            string targetPath = sourceIsLocal ? _rightPaht : _leftPaht;

            string sourceFullPath = Path.Combine(sourcePath, sourceItem.Name);
            string destinationFullPath = Path.Combine(targetPath, sourceItem.Name);

            if (!sourceIsLocal) sourceFullPath = sourceFullPath.Replace('\\', '/');
            if (sourceIsLocal) destinationFullPath = destinationFullPath.Replace('\\', '/');

            var details = new TransferDetails(
                SourceFullPath: sourceFullPath,
                DestinationFullPath: destinationFullPath,
                SourceIsLocal: sourceIsLocal,
                TargetIsLocal: !sourceIsLocal,
                SourceTreeView: sourceTreeView,
                TargetTreeView: targetTreeView,
                SourcePath: sourcePath,
                TargetPath: targetPath
            );

            await ExecuteTransferWithProgressAsync(details, sourceItem, operationType);
        }
        finally
        {
            _isTransferInProgress = false;
        }
    }
    private async void LeftCtxUpload_Click(object? sender, RoutedEventArgs e)
    {
        if (LeftTreeView.SelectedItem is FileSystemModel selectedItem)
        {
            await HandleContextMenuTransfer(selectedItem, LeftTreeView, RightTreeView, TransferOperationType.Upload);
        }
    }
    private async void RightCtxUpload_Click(object? sender, RoutedEventArgs e)
    {
        if (RightTreeView.SelectedItem is FileSystemModel selectedItem)
        {
            await HandleContextMenuTransfer(selectedItem, RightTreeView, LeftTreeView, TransferOperationType.Download);
        }
    }
    private void LeftCtxOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (LeftTreeView.SelectedItem is not FileSystemModel selectedItem || selectedItem.Name == "..") return;
        if (string.IsNullOrEmpty(_leftPaht)) return;

        var fullPath = Path.Combine(_leftPaht, selectedItem.Name);
        FileSystemHelper.Open(fullPath);
    }
    #endregion
    #endregion

    #region UI 导航与数据加载 (核心修改部分)

    public void SetDevice(DeviceModel deviceModel)
    {
        adbFileManager = deviceModel.FileManager;
        InitializeSources();
        LoadInitialData();
    }

    // [核心修改]
    private void InitializeSources()
    {
        // 使用 FileSourceItem 对象列表，而不是字符串列表
        var leftSources = new ObservableCollection<FileSourceItem>
        {
            new("FilePage_Source_LocalHome", L.Tr("FilePage_Source_LocalHome")),
            new("FilePage_Source_LocalRoot", L.Tr("FilePage_Source_LocalRoot"))
        };

        var rightSources = new ObservableCollection<FileSourceItem>();
        if (adbFileManager == null)
        {
            rightSources.Add(new("FilePage_Source_NoDevice", L.Tr("FilePage_Source_NoDevice")));
        }
        else
        {
            rightSources.Add(new("FilePage_Source_AndroidInternal", L.Tr("FilePage_Source_AndroidInternal")));
            rightSources.Add(new("FilePage_Source_AndroidSdCard", L.Tr("FilePage_Source_AndroidSdCard")));
        }
        LeftSourceComboBox.ItemsSource = leftSources;
        RightSourceComboBox.ItemsSource = rightSources;

        // 告诉 ComboBox 显示哪个属性
        LeftSourceComboBox.DisplayMemberBinding = new Binding("DisplayName");
        RightSourceComboBox.DisplayMemberBinding = new Binding("DisplayName");

        // 设置默认选中项
        if (LeftSourceComboBox.Items.Count > 0) LeftSourceComboBox.SelectedIndex = 0;
        if (RightSourceComboBox.Items.Count > 0) RightSourceComboBox.SelectedIndex = 0;
    }

    // [核心修改]
    private async void LoadInitialData()
    {
        // 使用稳定的 Key 来获取初始路径
        var leftInitialPath = GetInitialPathForSource("FilePage_Source_LocalHome");
        await LoadDataForTreeView(LeftTreeView, (LeftSourceComboBox.SelectedItem as FileSourceItem), leftInitialPath);

        var rightInitialPath = GetInitialPathForSource("FilePage_Source_AndroidInternal");
        await LoadDataForTreeView(RightTreeView, (RightSourceComboBox.SelectedItem as FileSourceItem), rightInitialPath);
    }

    // [核心修改]
    private static string GetInitialPathForSource(string? key)
    {
        // 使用稳定的 Key 进行逻辑判断
        return key switch
        {
            "FilePage_Source_LocalHome" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "FilePage_Source_LocalRoot" => Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "/",
            "FilePage_Source_AndroidInternal" => "/",
            "FilePage_Source_AndroidSdCard" => "/sdcard/",
            _ => "/"
        };
    }

    // [核心修改]
    private async Task LoadDataForTreeView(TreeView treeView, FileSourceItem? sourceItem, string path)
    {
        if (sourceItem == null || string.IsNullOrEmpty(path)) return;
        var key = sourceItem.Key; // 使用 Key 进行逻辑判断

        if (treeView == LeftTreeView)
        {
            _leftPaht = path;
            LeftPathTextBox.Text = path;
        }
        else
        {
            _rightPaht = path;
            RightPathTextBox.Text = path;
        }

        var items = new ObservableCollection<FileSystemModel>();
        try
        {
            ObservableCollection<FileSystemModel>? newItems = null;
            switch (key)
            {
                case "FilePage_Source_LocalHome":
                case "FilePage_Source_LocalRoot":
                    newItems = GetLocalDirectoryListing(path);
                    break;
                case "FilePage_Source_AndroidInternal":
                case "FilePage_Source_AndroidSdCard":
                    if (adbFileManager != null) newItems = await adbFileManager.ListDirectoryAsync(path);
                    break;
            }
            if (newItems != null)
            {
                foreach (var item in newItems.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name))
                {
                    items.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Loading directory '{path}': {ex.Message}");
        }
        treeView.ItemsSource = items;
    }

    // [核心修改]
    private async void SourceComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 判断选中的对象是 FileSourceItem
        if (sender is ComboBox { SelectedItem: FileSourceItem sourceItem } comboBox)
        {
            // 使用 Key 获取路径
            string initialPath = GetInitialPathForSource(sourceItem.Key);
            if (comboBox.Name == "LeftSourceComboBox")
            {
                await LoadDataForTreeView(LeftTreeView, sourceItem, initialPath);
            }
            else if (comboBox.Name == "RightSourceComboBox")
            {
                await LoadDataForTreeView(RightTreeView, sourceItem, initialPath);
            }
        }
    }

    #endregion

    // ... RefreshButton_Click, PathTextBox_KeyDown, TreeView_DoubleTapped 和 拖拽与传输部分 保持不变 ...
    #region Unchanged Code Block 2
    private ObservableCollection<FileSystemModel> GetLocalDirectoryListing(string path)
    {
        var items = new ObservableCollection<FileSystemModel>();
        try
        {
            var dirInfo = new DirectoryInfo(path);
            items.Add(new FileSystemModel(name: "..", size: 0, isDirectory: true));
            foreach (var dir in dirInfo.GetDirectories())
            {
                items.Add(new FileSystemModel(name: dir.Name, size: 0, isDirectory: true));
            }
            foreach (var file in dirInfo.GetFiles())
            {
                items.Add(new FileSystemModel(name: file.Name, size: (int)file.Length, isDirectory: false));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Getting local listing for '{path}': {ex.Message}");
        }
        return items;
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            if (button.Name == "LeftRefreshButton")
            {
                await LoadDataForTreeView(LeftTreeView, LeftSourceComboBox.SelectedItem as FileSourceItem, LeftPathTextBox.Text!);
            }
            else if (button.Name == "RightRefreshButton")
            {
                await LoadDataForTreeView(RightTreeView, RightSourceComboBox.SelectedItem as FileSourceItem, RightPathTextBox.Text!);
            }
        }
    }
    private async void PathTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox pathTextBox)
        {
            TreeView treeView = pathTextBox.Name == "LeftPathTextBox" ? LeftTreeView : RightTreeView;
            ComboBox sourceComboBox = pathTextBox.Name == "LeftPathTextBox" ? LeftSourceComboBox : RightSourceComboBox;
            await LoadDataForTreeView(treeView, sourceComboBox.SelectedItem as FileSourceItem, pathTextBox.Text!);
        }
    }
    private async void TreeView_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Control sourceControl) return;
        if (sourceControl.DataContext is FileSystemModel selectedItem && selectedItem.IsDirectory)
        {
            var treeView = (TreeView)sender!;
            var currentPath = treeView == LeftTreeView ? _leftPaht : _rightPaht;
            string newPath;
            if (selectedItem.Name == "..")
            {
                newPath = Path.GetDirectoryName(currentPath.TrimEnd(['/', '\\'])) ?? currentPath;
                if (string.IsNullOrEmpty(newPath)) newPath = "/";
            }
            else
            {
                newPath = Path.Combine(currentPath, selectedItem.Name);
            }
            if (treeView == RightTreeView)
            {
                newPath = newPath.Replace('\\', '/');
            }
            var sourceComboBox = treeView == LeftTreeView ? LeftSourceComboBox : RightSourceComboBox;
            await LoadDataForTreeView(treeView, sourceComboBox.SelectedItem as FileSourceItem, newPath);
        }
    }

    #region 拖拽与传输
    private async void Item_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Panel panel && panel.DataContext is FileSystemModel)
        {
            _dragSourceTreeView = panel.FindAncestorOfType<TreeView>();
            var dragData = new DataObject();
            dragData.Set("FileSystemModel", panel.DataContext);
            await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
        }
    }
    private void TreeView_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        if (!e.Data.Contains("FileSystemModel")) return;
        var draggedItem = e.Data.Get("FileSystemModel") as FileSystemModel;
        if (draggedItem == null) return;
        var targetControl = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (targetControl?.DataContext is FileSystemModel targetItem)
        {
            if (targetItem.IsDirectory && targetItem != draggedItem) e.DragEffects = DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.Move;
        }
    }
    private async void TreeView_Drop(object? sender, DragEventArgs e)
    {
        if (_isTransferInProgress)
        {
            await DialogHelper.MessageShowAsync(L.Tr("FilePage_TaskInProgress_Title"), L.Tr("FilePage_TaskInProgress_Message"));
            return;
        }
        if (adbFileManager == null)
        {
            await DialogHelper.MessageShowAsync(L.Tr("FilePage_NoDevice_Title"), L.Tr("FilePage_NoDevice_Message"));
            return;
        }
        if (!e.Data.Contains("FileSystemModel") || e.Data.Get("FileSystemModel") is not FileSystemModel draggedItem) return;

        _isTransferInProgress = true;
        try
        {
            if (!TryGetTransferDetails(sender, e, out var details)) return;
            var operationType = GetOperationType(details!, draggedItem);
            if (operationType == TransferOperationType.Unsupported)
            {
                await DialogHelper.MessageShowAsync(L.Tr("FilePage_OperationUnsupported_Title"), L.Tr("FilePage_DragDropUnsupported_Message"));
                return;
            }
            await ExecuteTransferWithProgressAsync(details!, draggedItem, operationType);
        }
        finally
        {
            _isTransferInProgress = false;
            _dragSourceTreeView = null;
        }
    }
    private bool TryGetTransferDetails(object? sender, DragEventArgs e, out TransferDetails? details)
    {
        details = null;
        if (_dragSourceTreeView is null || sender is not TreeView targetTreeView) return false;
        var sourceTreeView = _dragSourceTreeView;
        var draggedItem = e.Data.Get("FileSystemModel") as FileSystemModel;
        if (draggedItem is null) return false;

        bool sourceIsLocal = sourceTreeView == LeftTreeView;
        string sourcePath = sourceIsLocal ? _leftPaht : _rightPaht;
        string targetPath = sourceTreeView == targetTreeView ? sourcePath : (sourceIsLocal ? _rightPaht : _leftPaht);

        string sourceFullPath = Path.Combine(sourcePath, draggedItem.Name);

        var dropTarget = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        string targetDirectoryPath = (dropTarget?.DataContext is FileSystemModel targetItem && targetItem.IsDirectory)
            ? Path.Combine(targetPath, targetItem.Name)
            : targetPath;
        var destinationFullPath = Path.Combine(targetDirectoryPath, draggedItem.Name);

        bool targetIsLocal = targetTreeView == LeftTreeView;
        if (!sourceIsLocal) sourceFullPath = sourceFullPath.Replace('\\', '/');
        if (!targetIsLocal) destinationFullPath = destinationFullPath.Replace('\\', '/');

        details = new TransferDetails(sourceFullPath, destinationFullPath, sourceIsLocal, targetIsLocal, sourceTreeView, targetTreeView, sourcePath, targetPath);
        return true;
    }
    private TransferOperationType GetOperationType(TransferDetails details, FileSystemModel item)
    {
        if (details.SourceIsLocal && details.TargetIsLocal) return TransferOperationType.Unsupported;
        if (!details.SourceIsLocal && details.TargetIsLocal) return TransferOperationType.Download;
        if (details.SourceIsLocal && !details.TargetIsLocal) return TransferOperationType.Upload;
        if (!details.SourceIsLocal && !details.TargetIsLocal) return TransferOperationType.Move;
        return TransferOperationType.Unsupported;
    }
    private async Task ExecuteTransferWithProgressAsync(TransferDetails details, FileSystemModel item, TransferOperationType operation)
    {
        var cts = new CancellationTokenSource();
        var progressDialog = DialogHelper.GetProgressShow(
            L.Tr("FilePage_FileTransfer_Title"),
            L.Tr("FilePage_FileTransfer_Message", item.Name),
            true,
            () => cts.Cancel()
        );
        var progressReporter = new Progress<double>(value => progressDialog.ProgressValue = value);
        DialogHelper.ShowProgress();
        try
        {
            await Task.Run(async () =>
            {
                switch (operation)
                {
                    case TransferOperationType.Download:
                        await adbFileManager!.DownloadFileAsync(details.SourceFullPath, details.DestinationFullPath, progressReporter, cts.Token);
                        break;
                    case TransferOperationType.Upload:
                        await adbFileManager!.UploadFileAsync(details.SourceFullPath, details.DestinationFullPath, progressReporter, cts.Token);
                        break;
                    case TransferOperationType.Move:
                        progressDialog.ShowProgress = false;
                        await adbFileManager!.MoveFileAsync(details.SourceFullPath, details.DestinationFullPath, cts.Token);
                        break;
                }
            }, cts.Token);
        }
        catch (OperationCanceledException) { /* 静默处理 */ }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                DialogHelper.MessageShowAsync(L.Tr("FilePage_TransferError_Title"), L.Tr("FilePage_TransferError_Message", ex.Message))
            );
        }
        finally
        {
            DialogHelper.CloseProgress();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await LoadDataForTreeView(details.SourceTreeView, (details.SourceTreeView == LeftTreeView ? LeftSourceComboBox : RightSourceComboBox).SelectedItem as FileSourceItem, details.SourcePath);
                if (details.SourceTreeView != details.TargetTreeView)
                {
                    await LoadDataForTreeView(details.TargetTreeView, (details.TargetTreeView == LeftTreeView ? LeftSourceComboBox : RightSourceComboBox).SelectedItem as FileSourceItem, details.TargetPath);
                }
            });
        }
    }
    #endregion
    #endregion
}