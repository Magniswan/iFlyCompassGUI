using System.Text;

namespace iFlyCompassGUI.Helpers;

public static class EncodingHelper
{
    public static (bool IsUtf8, Encoding? DetectedEncoding) DetectEncoding(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var bom = new byte[4];
        int read = fs.Read(bom, 0, 4);
        
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return (true, Encoding.UTF8);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return (true, Encoding.Unicode);
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return (true, Encoding.BigEndianUnicode);
        
        fs.Position = 0;
        var buffer = new byte[Math.Min(4096, fs.Length)];
        fs.Read(buffer, 0, buffer.Length);
        
        try
        {
            var utf8Text = Encoding.UTF8.GetString(buffer);
            var bytes = Encoding.UTF8.GetBytes(utf8Text);
            if (bytes.SequenceEqual(buffer))
                return (true, Encoding.UTF8);
        }
        catch { }
        
        foreach (var enc in new[] { Encoding.GetEncoding("GBK"), Encoding.GetEncoding("Big5"), Encoding.GetEncoding("Shift_JIS") })
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
