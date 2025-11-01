using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AYLink.Utils;

internal class FileSystemHelper
{
    /// <summary>
    /// 打开一个文件或目录
    /// </summary>
    /// <param name="path">要打开的文件或目录的完整路径。</param>
    public static void Open(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch
        {

        }
    }
}
