using System.Text;

namespace iFlyCompassGUI.Helpers;

public static class EncodingHelper
{
    // 支持的编码列表，按优先级排序
    private static readonly Encoding[] SupportedEncodings =
    [
        Encoding.GetEncoding("GB2312"),   // 简体中文 (简体字常用)
        Encoding.GetEncoding("GBK"),      // 简体中文 (扩展)
        Encoding.GetEncoding("Big5"),     // 繁体中文 (台湾)
        Encoding.GetEncoding("Shift_JIS"), // 日文
        Encoding.GetEncoding("EUC-JP"),    // 日文 (Unix)
        Encoding.GetEncoding("EUC-KR"),    // 韩文
        Encoding.GetEncoding("ISO-8859-1"), // 西欧语言
        Encoding.GetEncoding("Windows-1252"), // 西欧语言 (Windows)
        Encoding.GetEncoding("UTF-16"),    // Unicode
    ];

    private static readonly (byte[] Bom, Encoding Encoding)[] BomSignatures =
    [
        (new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8),
        (new byte[] { 0xFF, 0xFE }, Encoding.Unicode),
        (new byte[] { 0xFE, 0xFF }, Encoding.BigEndianUnicode),
    ];

    public static (bool IsUtf8, Encoding? DetectedEncoding) DetectEncoding(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var bom = new byte[4];
        fs.ReadExactly(bom, 0, 4);

        // 检查 BOM
        foreach (var (signature, encoding) in BomSignatures)
        {
            if (bom.Take(signature.Length).SequenceEqual(signature))
                return (true, encoding);
        }

        fs.Position = 0;
        var buffer = new byte[Math.Min(4096, fs.Length)];
        fs.ReadExactly(buffer, 0, buffer.Length);

        // 尝试 UTF-8 (无 BOM)
        try
        {
            var utf8Text = Encoding.UTF8.GetString(buffer);
            var bytes = Encoding.UTF8.GetBytes(utf8Text);
            if (bytes.SequenceEqual(buffer))
                return (true, Encoding.UTF8);
        }
        catch { }

        // 尝试其他编码
        foreach (var enc in SupportedEncodings)
        {
            try
            {
                var text = enc.GetString(buffer);
                if (!text.Contains('\uFFFD'))
                    return (false, enc);
            }
            catch { }
        }

        return (false, null);
    }
    
    public static async Task ConvertToUtf8Async(string sourcePath, string destPath)
    {
        var (isUtf8, detectedEnc) = DetectEncoding(sourcePath);
        var encoding = isUtf8 ? Encoding.UTF8 : (detectedEnc ?? Encoding.GetEncoding("GBK"));
        var text = await File.ReadAllTextAsync(sourcePath, encoding);
        await File.WriteAllTextAsync(destPath, text, Encoding.UTF8);
    }
}
