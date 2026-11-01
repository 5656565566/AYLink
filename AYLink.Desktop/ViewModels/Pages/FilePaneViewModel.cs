using AYLink.Core.ADB;
using AYLink.Core.Devices;
using AYLink.Core.Models;
using AYLink.Desktop.Models;
using AYLink.Desktop.Services.Agent;
using AYLink.Desktop.Services.Localization;
using AYLink.Desktop.Services.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AYLink.Desktop.ViewModels.Pages;

public partial class FilePaneViewModel : ViewModelBase
{
    private readonly ObservableCollection<FileSource> _customSources = [];
    private ObservableCollection<FileSystemModel>? _observedFiles;
    private FileSource? _loadedSource;
    private string? _loadedPath;
    private int _loadVersion;
    private bool _changingSources;
    private bool _updatingSelection;
    private static readonly int[] IconSizes = [32, 48, 64, 96, 128];

    public ObservableCollection<FileSystemModel> SelectedFiles { get; } = [];

    [ObservableProperty]
    public partial ObservableCollection<FileSource> AvailableSources { get; set; } = [];
    [ObservableProperty]
    public partial FileSource? SelectedSource { get; set; }
    [ObservableProperty]
    public partial string CurrentPath { get; set; } = string.Empty;
    [ObservableProperty]
    public partial ObservableCollection<FileSystemModel> Files { get; set; } = [];
    [ObservableProperty]
    public partial IReadOnlyList<FileSystemModel> VisibleFiles { get; set; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedName))]
    public partial FileSystemModel? SelectedFile { get; set; }
    [ObservableProperty]
    public partial bool IsLoading { get; set; }
    [ObservableProperty]
    public partial string LoadError { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty]
    public partial FileViewMode ViewMode { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconTileWidth), nameof(IconTileHeight))]
    public partial int IconSize { get; set; } = 32;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameSort), nameof(IsTypeSort), nameof(IsSizeSort))]
    public partial FileSortColumn SortColumn { get; set; }
    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    public int ItemCount => Files.Count(file => file.Name != "..");
    public string SelectedName => SelectedFile?.Name ?? string.Empty;
    public int SelectedCount => SelectedFiles.Count;
    public bool HasMultipleSelection => SelectedCount > 1;
    public double IconTileWidth => Math.Max(120, IconSize + 48);
    public double IconTileHeight => IconSize + 64;
    public int VisibleCount => VisibleFiles.Count;
    public bool HasFilter => !string.IsNullOrEmpty(SearchText);
    public bool HasError => !string.IsNullOrEmpty(LoadError);
    public bool IsEmpty => !IsLoading && !HasError && VisibleCount == 0;
    public bool HasCurrentListing => !IsLoading && !HasError && SelectedSource != null &&
        ReferenceEquals(SelectedSource, _loadedSource) && CurrentPath == _loadedPath;
    public bool CanNavigateUp => GetParentPath() != null;
    public bool IsNameSort => SortColumn == FileSortColumn.Name;
    public bool IsTypeSort => SortColumn == FileSortColumn.Type;
    public bool IsSizeSort => SortColumn == FileSortColumn.Size;
    public IReadOnlyList<FilePathSegment> Breadcrumbs
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentPath) || SelectedSource == null) return [];
            var result = new List<FilePathSegment>();
            var root = SelectedSource.IsLocal ? Path.GetPathRoot(CurrentPath) ?? string.Empty : "/";
            var path = root;
            result.Add(new FilePathSegment(root.TrimEnd('\\', '/') is { Length: > 0 } label ? label : "/", root));
            var separators = SelectedSource.IsLocal ? new[] { '/', '\\' } : ['/'];
            foreach (var part in CurrentPath[root.Length..].Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                path = SelectedSource.IsLocal ? Path.Combine(path, part) : path.TrimEnd('/') + "/" + part;
                result.Add(new FilePathSegment(part, path));
            }
            return result;
        }
    }

    public FilePaneViewModel()
    {
        SelectedFiles.CollectionChanged += SelectionChanged;
        ObserveFiles();
        RefreshSources(autoSelect: false);
    }

    public FilePaneViewModel(FilePaneViewModel source)
    {
        SelectedFiles.CollectionChanged += SelectionChanged;
        ObserveFiles();
        foreach (var custom in source._customSources) _customSources.Add(custom);
        AvailableSources = new(source.AvailableSources);
        ViewMode = source.ViewMode;
        IconSize = source.IconSize;
        SortColumn = source.SortColumn;
        SortDescending = source.SortDescending;
        SetLocation(source.SelectedSource, source.CurrentPath);
    }

    public void RefreshSources(bool autoSelect = true)
    {
        var selected = SelectedSource;
        var path = CurrentPath;
        var localizer = LocalizationManager.Instance;
        var sources = new ObservableCollection<FileSource>
        {
            new() { Name = localizer.GetString("FilePage.SourceLocalHome", "本地 - 用户目录"), InitialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
            new() { Name = localizer.GetString("FilePage.SourceLocalRoot", "本地 - 系统盘"), InitialPath = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? "C:\\" }
        };
        if (AdbManager.Instance.TryStartAdbServer())
        {
            foreach (var device in AdbManager.Instance.GetConnectedDevices())
            {
                sources.Add(new FileSource { Name = string.Format(localizer.GetString("FilePage.SourceAndroidInternal", "{0} - 内部存储"), device.Name), Device = device, InitialPath = "/" });
                sources.Add(new FileSource { Name = string.Format(localizer.GetString("FilePage.SourceAndroidSdCard", "{0} - SD卡"), device.Name), Device = device, InitialPath = "/sdcard/" });
            }
        }
        foreach (var source in _customSources) sources.Add(source);

        var match = selected == null ? null : sources.FirstOrDefault(source =>
            source.IsLocal == selected.IsLocal && source.Device?.Serial == selected.Device?.Serial &&
            ReferenceEquals(source.AgentFileManager, selected.AgentFileManager) && source.InitialPath == selected.InitialPath);
        if (match != null)
        {
            selected!.Name = match.Name;
            sources[sources.IndexOf(match)] = selected;
        }
        // Replacing the source list must not turn a refresh into a source-change navigation.
        _changingSources = true;
        try
        {
            AvailableSources = sources;
            if (autoSelect)
            {
                SelectedSource = match != null ? selected : sources.FirstOrDefault();
                CurrentPath = match != null ? path : SelectedSource?.InitialPath ?? string.Empty;
            }
        }
        finally { _changingSources = false; }
        UpdateState();
    }

    public void SelectLocalHome() => SelectedSource = AvailableSources.FirstOrDefault(source => source.IsLocal);

    public void SelectDevice(DeviceModel device)
    {
        var source = AvailableSources.FirstOrDefault(item => item.Device?.Serial == device.Serial && item.InitialPath == "/sdcard/");
        if (source == null)
        {
            AvailableSources.Add(new FileSource
            {
                Name = string.Format(LocalizationManager.Instance.GetString("FilePage.SourceAndroidInternal", "{0} - 内部存储"), device.Name),
                Device = device, InitialPath = "/"
            });
            source = new FileSource
            {
                Name = string.Format(LocalizationManager.Instance.GetString("FilePage.SourceAndroidSdCard", "{0} - SD卡"), device.Name),
                Device = device, InitialPath = "/sdcard/"
            };
            AvailableSources.Add(source);
        }
        SelectedSource = source;
    }

    public void SelectRemoteDevice(DeviceDescriptor remoteDevice, string serverId, string? initialPath = null)
    {
        var runtime = Services.AgentSessionService.Instance.FindServer(serverId)
            ?? throw new InvalidOperationException($"未找到远程服务器 {serverId}");
        var source = new FileSource
        {
            Name = string.Format(LocalizationManager.Instance.GetString("FilePage.SourceAndroidInternal", "{0} - 内部存储"), remoteDevice.Name),
            AgentFileManager = new AgentFileManager(runtime, remoteDevice.RemoteDeviceId ?? 0), InitialPath = "/sdcard/"
        };
        _customSources.Clear();
        _customSources.Add(source);
        RefreshSources(autoSelect: false);
        SetLocation(source, string.IsNullOrWhiteSpace(initialPath) ? source.InitialPath : initialPath.Trim());
    }

    public void SetLocation(FileSource? source, string path)
    {
        _changingSources = true;
        try { SelectedSource = source; CurrentPath = path; }
        finally { _changingSources = false; }
        SearchText = string.Empty;
        SelectedFile = null;
        _ = LoadFilesAsync();
    }

    partial void OnSelectedSourceChanged(FileSource? value)
    {
        if (_changingSources) return;
        CurrentPath = value?.InitialPath ?? string.Empty;
        SearchText = string.Empty;
        SelectedFile = null;
        _ = LoadFilesAsync();
    }
    partial void OnCurrentPathChanged(string value) { UpdateState(); OnPropertyChanged(nameof(Breadcrumbs)); }
    partial void OnIsLoadingChanged(bool value) => UpdateState();
    partial void OnLoadErrorChanged(string value) => UpdateState();
    partial void OnFilesChanged(ObservableCollection<FileSystemModel> value) { ObserveFiles(); RebuildVisibleFiles(); }
    partial void OnSearchTextChanged(string value) => RebuildVisibleFiles();
    partial void OnSortColumnChanged(FileSortColumn value) => RebuildVisibleFiles();
    partial void OnSortDescendingChanged(bool value) => RebuildVisibleFiles();
    partial void OnSelectedFileChanged(FileSystemModel? value)
    {
        if (_updatingSelection) return;
        SetSelection(value == null ? [] : [value]);
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_updatingSelection) return;
        _updatingSelection = true;
        try
        {
            SelectedFile = args.NewItems?.OfType<FileSystemModel>().LastOrDefault()
                ?? (SelectedFile != null && SelectedFiles.Contains(SelectedFile) ? SelectedFile : SelectedFiles.FirstOrDefault());
        }
        finally { _updatingSelection = false; }
        NotifySelection();
    }

    public void SetSelection(IEnumerable<FileSystemModel> files)
    {
        var selected = files.Where(VisibleFiles.Contains).Distinct().ToArray();
        _updatingSelection = true;
        try
        {
            SelectedFiles.Clear();
            foreach (var file in selected) SelectedFiles.Add(file);
            SelectedFile = selected.LastOrDefault();
        }
        finally { _updatingSelection = false; }
        NotifySelection();
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectedName));
    }

    public void ChangeZoom(bool increase)
    {
        if (increase)
        {
            if (ViewMode == FileViewMode.Details) ViewMode = FileViewMode.List;
            else if (ViewMode == FileViewMode.List) { IconSize = IconSizes[0]; ViewMode = FileViewMode.Icons; }
            else IconSize = IconSizes.FirstOrDefault(size => size > IconSize, IconSizes[^1]);
        }
        else
        {
            if (ViewMode == FileViewMode.Icons && IconSize > IconSizes[0]) IconSize = IconSizes.Last(size => size < IconSize);
            else if (ViewMode == FileViewMode.Icons) ViewMode = FileViewMode.List;
            else if (ViewMode == FileViewMode.List) ViewMode = FileViewMode.Details;
        }
    }

    private void ObserveFiles()
    {
        if (_observedFiles != null) _observedFiles.CollectionChanged -= FilesChanged;
        _observedFiles = Files;
        _observedFiles.CollectionChanged += FilesChanged;
    }
    private void FilesChanged(object? sender, NotifyCollectionChangedEventArgs args) => RebuildVisibleFiles();

    private void RebuildVisibleFiles()
    {
        var selected = SelectedFiles.ToArray();
        var comparer = Comparer<string>.Create((left, right) => CultureInfo.CurrentCulture.CompareInfo.Compare(
            left, right, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering));
        var files = Files.Where(file => file.Name != ".." && file.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
        var ordered = files.OrderByDescending(file => file.IsDirectory);
        VisibleFiles = (SortColumn switch
        {
            FileSortColumn.Size => SortDescending ? ordered.ThenByDescending(file => file.Size) : ordered.ThenBy(file => file.Size),
            FileSortColumn.Type => SortDescending ? ordered.ThenByDescending(file => Path.GetExtension(file.Name), comparer) : ordered.ThenBy(file => Path.GetExtension(file.Name), comparer),
            _ => SortDescending ? ordered.ThenByDescending(file => file.Name, comparer) : ordered.ThenBy(file => file.Name, comparer)
        }).ThenBy(file => file.Name, comparer).ToArray();
        SetSelection(selected);
        UpdateState();
    }

    private void UpdateState()
    {
        OnPropertyChanged(nameof(ItemCount)); OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasFilter)); OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasCurrentListing)); OnPropertyChanged(nameof(CanNavigateUp));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task LoadFilesAsync()
    {
        var version = ++_loadVersion;
        var source = SelectedSource;
        var path = CurrentPath;
        if (source == null || string.IsNullOrWhiteSpace(path))
        {
            Files = []; IsLoading = false; LoadError = string.Empty; return;
        }
        var sameDirectory = ReferenceEquals(source, _loadedSource) && path == _loadedPath;
        var selectedNames = sameDirectory ? SelectedFiles.Select(file => file.Name).ToHashSet(StringComparer.Ordinal) : [];
        IsLoading = true;
        LoadError = string.Empty;
        if (!sameDirectory) { Files = []; SelectedFile = null; }
        try
        {
            var items = source.IsLocal ? await ReadLocalFilesAsync(path)
                : source.IsRemoteAgent ? await source.AgentFileManager!.ListDirectoryAsync(path)
                : await source.Device!.FileManager.ListDirectoryAsync(path);
            // A source/path change owns the UI even when an older request finishes last.
            if (version != _loadVersion || !ReferenceEquals(source, SelectedSource) || path != CurrentPath) return;
            _loadedSource = source; _loadedPath = path;
            Files = items;
            SetSelection(VisibleFiles.Where(file => selectedNames.Contains(file.Name)));
        }
        catch (Exception ex)
        {
            if (version != _loadVersion || !ReferenceEquals(source, SelectedSource) || path != CurrentPath) return;
            Files = []; SelectedFile = null; LoadError = ex.Message;
        }
        finally { if (version == _loadVersion) IsLoading = false; }
    }

    private static Task<ObservableCollection<FileSystemModel>> ReadLocalFilesAsync(string path) => Task.Run(() =>
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) throw new DirectoryNotFoundException($"目录不存在: {path}");
        var files = new ObservableCollection<FileSystemModel>();
        if (directory.Parent != null) files.Add(new FileSystemModel("..", 0, true));
        foreach (var child in directory.GetDirectories()) files.Add(new FileSystemModel(child.Name, 0, true));
        foreach (var file in directory.GetFiles()) files.Add(new FileSystemModel(file.Name, (uint)file.Length));
        return files;
    });

    public string GetEntryPath(FileSystemModel file) => SelectedSource?.IsLocal == true
        ? Path.Combine(CurrentPath, file.Name) : CurrentPath.TrimEnd('/') + "/" + file.Name;

    private string? GetParentPath()
    {
        if (SelectedSource == null || string.IsNullOrWhiteSpace(CurrentPath)) return null;
        if (SelectedSource.IsLocal)
        {
            try { return Directory.GetParent(CurrentPath)?.FullName; }
            catch (ArgumentException) { return null; }
        }
        var path = CurrentPath.TrimEnd('/');
        if (path.Length == 0) return null;
        var index = path.LastIndexOf('/');
        return index > 0 ? path[..index] : "/";
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task NavigateTo(FileSystemModel? file)
    {
        if (file == null || !file.IsDirectory || !HasCurrentListing) return;
        var path = file.Name == ".." ? GetParentPath() : GetEntryPath(file);
        if (path != null) await NavigatePathAsync(path);
    }
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task NavigatePathAsync(string path)
    {
        CurrentPath = path; SearchText = string.Empty; SelectedFile = null;
        await LoadFilesAsync();
    }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task GoUp()
    {
        if (GetParentPath() is { } path) await NavigatePathAsync(path);
    }
    [RelayCommand]
    private async Task Refresh() { RefreshSources(); await LoadFilesAsync(); }

    public bool CanOperate(FileSystemModel? file) => file != null && file.Name != ".." && HasCurrentListing && Files.Contains(file);

    public string? ValidateRename(FileSystemModel file, string name)
    {
        var localizer = LocalizationManager.Instance;
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\0']) >= 0 ||
            SelectedSource?.IsLocal == true && name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return localizer.GetString("FilePage.InvalidName", "请输入有效的单个文件名称。");
        var comparison = SelectedSource?.IsLocal == true ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Files.Any(item => !ReferenceEquals(item, file) && string.Equals(item.Name, name, comparison))
            ? localizer.GetString("FilePage.NameExists", "当前目录已存在同名项目。") : null;
    }

    public async Task<bool> RenameFileAsync(FileSystemModel file, string name)
    {
        if (!CanOperate(file)) return false;
        var source = SelectedSource!;
        var path = CurrentPath;
        var originalPath = GetEntryPath(file);
        var error = ValidateRename(file, name);
        if (error != null) { NotificationService.Instance.ShowError(LocalizationManager.Instance.GetString("FilePage.RenameTitle", "重命名"), error); return false; }
        if (name == file.Name) return true;
        try
        {
            bool success;
            if (source.IsLocal)
            {
                var target = Path.Combine(path, name);
                await Task.Run(() => { if (file.IsDirectory) Directory.Move(originalPath, target); else File.Move(originalPath, target); });
                success = true;
            }
            else success = source.IsRemoteAgent ? await source.AgentFileManager!.RenameFileAsync(originalPath, name)
                : await source.Device!.FileManager.MoveFileAsync(originalPath, path.TrimEnd('/') + "/" + name);
            if (!success)
            {
                NotificationService.Instance.ShowError(LocalizationManager.Instance.GetString("FilePage.RenameTitle", "重命名"),
                    LocalizationManager.Instance.GetString("FilePage.RenameFailed", "无法重命名该项目。"));
                return false;
            }
            if (ReferenceEquals(source, SelectedSource) && CurrentPath == path) await LoadFilesAsync();
            return true;
        }
        catch (Exception ex) { NotificationService.Instance.ShowError(LocalizationManager.Instance.GetString("FilePage.RenameTitle", "重命名"), ex.Message); return false; }
    }

    [RelayCommand]
    public Task DeleteFileAsync(FileSystemModel? file) => file == null ? Task.CompletedTask : DeleteFilesAsync([file]);

    public async Task DeleteFilesAsync(IReadOnlyList<FileSystemModel> files)
    {
        var selected = files.Distinct().ToArray();
        if (selected.Length == 0 || selected.Any(file => !CanOperate(file))) return;
        var source = SelectedSource!;
        var directory = CurrentPath;
        // Capture all paths before showing the confirmation, since another tab can navigate independently.
        var entries = selected.Select(file => (file.Name, file.IsDirectory, Path: GetEntryPath(file))).ToArray();
        var localizer = LocalizationManager.Instance;
        var message = entries.Length == 1
            ? string.Format(localizer.GetString("FilePage.ConfirmDeleteMessage", "确定要删除 {0} 吗？"), entries[0].Name)
            : string.Format(localizer.GetString("FilePage.ConfirmDeleteSelected", "确定要删除选中的 {0} 个项目吗？"), entries.Length);
        var result = await DialogService.ShowMessageAsync(localizer.GetString("FilePage.ConfirmDeleteTitle", "确认删除"),
            message, localizer.GetString("FilePage.DeleteButton", "删除"), localizer.GetString("Dialog.Cancel", "取消"));
        if (result != FluentAvalonia.UI.Controls.FAContentDialogResult.Primary) return;
        try
        {
            foreach (var entry in entries)
            {
                bool success;
                if (source.IsLocal)
                {
                    await Task.Run(() => { if (entry.IsDirectory) Directory.Delete(entry.Path, true); else File.Delete(entry.Path); });
                    success = true;
                }
                else success = source.IsRemoteAgent ? await source.AgentFileManager!.DeleteFileAsync(entry.Path)
                    : await source.Device!.FileManager.DeleteFileAsync(entry.Path);
                if (!success)
                {
                    NotificationService.Instance.ShowError(localizer.GetString("FilePage.DeleteFailedTitle", "删除失败"),
                        string.Format(localizer.GetString("FilePage.DeleteFailedMessage", "无法删除 {0}"), entry.Name));
                    return;
                }
            }
            var completed = entries.Length == 1
                ? string.Format(localizer.GetString("FilePage.DeleteSuccessMessage", "{0} 已被删除"), entries[0].Name)
                : string.Format(localizer.GetString("FilePage.DeleteSelectedSuccess", "{0} 个项目已被删除"), entries.Length);
            NotificationService.Instance.ShowSuccess(localizer.GetString("FilePage.DeleteSuccessTitle", "删除成功"), completed);
        }
        catch (Exception ex) { NotificationService.Instance.ShowError(localizer.GetString("FilePage.DeleteErrorTitle", "删除出错"), ex.Message); }
        finally
        {
            if (ReferenceEquals(source, SelectedSource) && CurrentPath == directory) await LoadFilesAsync();
        }
    }

    [RelayCommand]
    public Task Delete(FileSystemModel? file) => DeleteFileAsync(file);
}
