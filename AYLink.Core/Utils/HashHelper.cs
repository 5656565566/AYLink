using System;
using System.Security.Cryptography;
using System.Text;

namespace AYLink.Core.Utils;

public class HashHelper
{
    /// <summary>
    /// MD5 哈希（16 字符小写十六进制）
    /// </summary>
    public static string ToMd5Hash(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = MD5.HashData(bytes);

        var sb = new StringBuilder(16);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));

        return sb.ToString();
    }
}
