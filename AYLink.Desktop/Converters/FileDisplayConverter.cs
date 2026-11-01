using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using AYLink.Core.Models;
using FluentAvalonia.UI.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AYLink.Desktop.Converters;

public sealed class FileDisplayConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0) return AvaloniaProperty.UnsetValue;
        if (parameter is "Count" && values.Count == 3 && values[0] is string format && values[1] is int visible && values[2] is int total)
            return string.Format(culture, format, visible, total);
        if (values[0] is not FileSystemModel file) return AvaloniaProperty.UnsetValue;
        if (parameter is "Icon")
        {
            if (file.IsDirectory) return FASymbol.Folder;
            return Path.GetExtension(file.Name).ToLowerInvariant() switch
            {
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" or ".heic" or ".avif" or ".ico" or ".tif" or ".tiff" => FASymbol.BrowsePhotos,
                ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" or ".opus" or ".aiff" => FASymbol.Audio,
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" or ".m4v" or ".3gp" => FASymbol.Video,
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".tgz" or ".zst" => FASymbol.ZipFolder,
                ".txt" or ".md" or ".log" or ".rtf" or ".pdf" or ".doc" or ".docx" or ".odt" or ".xls" or ".xlsx" or ".csv" or ".ods" or ".ppt" or ".pptx" or ".odp" => FASymbol.Page,
                ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".conf" or ".config" or
                ".cs" or ".js" or ".jsx" or ".ts" or ".tsx" or ".vue" or ".py" or ".lua" or ".java" or ".kt" or ".go" or ".rs" or
                ".c" or ".h" or ".cpp" or ".hpp" or ".html" or ".css" or ".scss" or ".sql" or ".sh" or ".ps1" or ".bat" or ".cmd" => FASymbol.Code,
                ".apk" or ".aab" or ".exe" or ".msi" or ".msix" or ".appx" or ".dmg" or ".pkg" or ".deb" or ".rpm" => FASymbol.AllApps,
                ".ttf" or ".otf" or ".woff" or ".woff2" => FASymbol.Font,
                _ => FASymbol.Document
            };
        }
        if (parameter is "Type" && values.Count == 3)
        {
            var extension = Path.GetExtension(file.Name);
            return file.IsDirectory ? values[1] : extension.Length > 1 ? extension[1..].ToUpperInvariant() : values[2];
        }
        if (parameter is "Size")
        {
            if (file.IsDirectory) return string.Empty;
            if (file.Size < 1024) return file.Size.ToString(culture) + " B";
            var size = (double)file.Size;
            var units = new[] { "KB", "MB", "GB" };
            var index = -1;
            do { size /= 1024; index++; } while (size >= 1024 && index < units.Length - 1);
            return size.ToString(size < 10 ? "0.#" : "0", culture) + " " + units[index];
        }
        return AvaloniaProperty.UnsetValue;
    }
}

public sealed class FilePaneWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
