using System.Text;

namespace AYLink.Utils;

internal class DebugTool
{
    public static string BytesToHexString(byte[] bytes, int bytesPerLine = 16, bool showAscii = false)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return "[]";
        }

        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            // 每行开始显示偏移量
            if (i % bytesPerLine == 0)
            {
                if (i > 0)
                {
                    // 显示ASCII
                    if (showAscii)
                    {
                        sb.Append("  ");
                        for (int j = i - bytesPerLine; j < i; j++)
                        {
                            if (j < bytes.Length)
                            {
                                char c = (char)bytes[j];
                                sb.Append(char.IsControl(c) ? '.' : c);
                            }
                        }
                    }
                    sb.AppendLine();
                }
                sb.Append($"{i:X8}: ");
            }

            sb.Append($"0x{bytes[i]:X2} ");
        }

        // 处理最后一行可能不完整的ASCII显示
        if (showAscii && bytes.Length > 0)
        {
            int remaining = bytesPerLine - (bytes.Length % bytesPerLine);
            sb.Append(new string(' ', remaining * 5)); // 每个"0xXX "占5字符

            sb.Append("  ");
            int start = bytes.Length - (bytes.Length % bytesPerLine);
            for (int j = start; j < bytes.Length; j++)
            {
                char c = (char)bytes[j];
                sb.Append(char.IsControl(c) ? '.' : c);
            }
        }

        return sb.ToString();
    }
}