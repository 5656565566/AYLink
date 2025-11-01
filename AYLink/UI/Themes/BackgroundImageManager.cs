using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AYLink.UI.Themes;

/// <summary>
/// 一个用于管理背景图片的单例类。
/// </summary>
public sealed class BackgroundImageManager
{
    private static readonly Lazy<BackgroundImageManager> lazy =
        new(() => new BackgroundImageManager());

    public static BackgroundImageManager Instance => lazy.Value;

    private readonly List<Image> _registeredImages = [];
    private readonly string _bgDirectory;
    private readonly Random _random = new();

    /// <summary>
    /// 私有变量，用于存储当前正在显示的图片路径。
    /// </summary>
    private string? _currentImagePath;

    private BackgroundImageManager()
    {
        var executablePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _bgDirectory = Path.Combine(executablePath ?? string.Empty, "bg");

        if (!Directory.Exists(_bgDirectory))
        {
            Directory.CreateDirectory(_bgDirectory);
        }
    }

    /// <summary>
    /// 注册一个 Avalonia Image 控件以进行背景管理。
    /// 如果已有背景图正在显示，则会立即应用到此控件上。
    /// </summary>
    /// <param name="imageControl">要注册的 Image 控件。</param>
    public void RegisterImageComponent(Image imageControl)
    {
        if (imageControl == null || _registeredImages.Contains(imageControl))
        {
            return;
        }

        _registeredImages.Add(imageControl);

        // 如果当前有背景图正在显示，则立即为新注册的控件设置它
        if (!string.IsNullOrEmpty(_currentImagePath) && File.Exists(_currentImagePath))
        {
            // 使用 Dispatcher 确保在 UI 线程上操作
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    imageControl.Source = new Bitmap(_currentImagePath);
                    imageControl.IsVisible = true;
                }
                catch
                {
                    // 如果加载失败，确保控件不可见
                    imageControl.Source = null;
                    imageControl.IsVisible = false;
                }
            });
        }
    }

    /// <summary>
    /// 取消一个 Image 控件的注册，使其不再受管理器控制。
    /// </summary>
    /// <param name="imageControl">要取消注册的 Image 控件。</param>
    public void UnregisterImageComponent(Image imageControl)
    {
        if (imageControl != null)
        {
            _registeredImages.Remove(imageControl);
        }
    }

    /// <summary>
    /// 新增一张背景图片。
    /// 图片将被复制到 "bg" 目录下，并以数字序号重命名。
    /// </summary>
    /// <param name="sourceImagePath">源图片的文件路径。</param>
    public void AddBackgroundImage(string sourceImagePath)
    {
        if (!File.Exists(sourceImagePath)) return;

        try
        {
            var existingFileNumbers = Directory.GetFiles(_bgDirectory)
                                         .Select(Path.GetFileNameWithoutExtension)
                                         .Select(fileName => int.TryParse(fileName, out var number) ? (int?)number : null)
                                         .OfType<int>()
                                         .ToList();

            int newIndex = existingFileNumbers.Count != 0 ? existingFileNumbers.Max() + 1 : 1;
            string fileExtension = Path.GetExtension(sourceImagePath);
            string newFileName = $"{newIndex}{fileExtension}";
            string destinationPath = Path.Combine(_bgDirectory, newFileName);

            File.Copy(sourceImagePath, destinationPath);
        }
        catch
        {

        }
    }

    /// <summary>
    /// 列出 "bg" 目录中所有的背景图片文件路径。
    /// </summary>
    /// <returns>一个包含所有背景图片完整路径的列表。</returns>
    public List<string> ListBackgroundImages()
    {
        return [.. Directory.GetFiles(_bgDirectory)];
    }

    /// <summary>
    /// 为所有已注册的 Image 控件设置指定的背景图片。
    /// </summary>
    /// <param name="imagePath">要设置为背景的图片文件路径。</param>
    public void SetBackgroundImage(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            ClearBackgroundImage();
            return;
        }

        // 更新当前图片路径
        _currentImagePath = imagePath;

        Dispatcher.UIThread.Invoke(() => {
            foreach (var image in _registeredImages)
            {
                try
                {
                    image.Source = new Bitmap(_currentImagePath);
                    image.IsVisible = true;
                }
                catch
                {
                    image.Source = null;
                    image.IsVisible = false;
                }
            }
        });
    }

    /// <summary>
    /// 从 "bg" 目录中随机选择一张图片，并设置为所有已注册控件的背景。
    /// </summary>
    public void SetRandomBackgroundImage()
    {
        var images = ListBackgroundImages();
        if (images.Count > 0)
        {
            int randomIndex = _random.Next(images.Count);
            SetBackgroundImage(images[randomIndex]);
        }
        else
        {
            ClearBackgroundImage();
        }
    }

    /// <summary>
    /// 从 "bg" 目录中删除指定的背景图片。
    /// 如果删除的是当前显示的图片，则清除所有背景。
    /// </summary>
    /// <param name="imagePath">要删除的背景图片的文件路径。</param>
    public void DeleteBackgroundImage(string imagePath)
    {
        if (!File.Exists(imagePath)) return;

        try
        {
            File.Delete(imagePath);

            // 如果删除的是当前正在显示的图片，则清除背景
            if (imagePath == _currentImagePath)
            {
                ClearBackgroundImage();
            }
        }
        catch
        {

        }
    }

    /// <summary>
    /// 为所有已注册的 Image 控件移除背景图片，并设置其为不可见。
    /// </summary>
    public void ClearBackgroundImage()
    {
        // 清空当前图片路径
        _currentImagePath = null;

        Dispatcher.UIThread.Invoke(() => {
            foreach (var image in _registeredImages)
            {
                image.Source = null;
                image.IsVisible = false;
            }
        });
    }
}