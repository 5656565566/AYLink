using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AYLink.Utils;

internal class HashHelper
{
    /// <summary>
    /// MD5 哈希（16 字符小写十六进制）
    /// </summary>
    public static string ToMd5Hash(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = md5.ComputeHash(bytes);

        var sb = new StringBuilder(16);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));

        return sb.ToString();
    }
}
