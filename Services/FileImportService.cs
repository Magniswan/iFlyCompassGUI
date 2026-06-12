using System.Diagnostics;
using System.Text.RegularExpressions;
using iFlyCompassGUI.Helpers;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Services;

public class FileImportService : IFileImportService
{
    private readonly string _baseDir;
    private readonly string _novelsDir;
    private readonly string _videosDir;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private bool? _gpuAccelAvailable;

    public FileImportService()
    {
        _baseDir = PathHelper.DataDirectory;
        _novelsDir = Path.Combine(_baseDir, "iFlyCompass", "instance", "novels");
        _videosDir = Path.Combine(_baseDir, "iFlyCompass", "instance", "videos");
        _ffmpegPath = Path.Combine(_baseDir, "iFlyCompass", "tools", "ffmpeg", "ffmpeg.exe");
        _ffprobePath = Path.Combine(_baseDir, "iFlyCompass", "tools", "ffmpeg", "ffprobe.exe");
        Directory.CreateDirectory(_novelsDir);
        Directory.CreateDirectory(_videosDir);
    }
    
    public async Task<ImportResult> ImportNovelAsync(string sourcePath)
    {
        try
        {
            var (isUtf8, detectedEnc) = EncodingHelper.DetectEncoding(sourcePath);
            var destPath = Path.Combine(_novelsDir, Path.GetFileName(sourcePath));
            
            if (isUtf8)
            {
                File.Copy(sourcePath, destPath, true);
                return new ImportResult { Success = true, Message = "导入成功（已是 UTF-8）", SourceEncoding = "UTF-8", DestinationPath = destPath };
            }
            
            await EncodingHelper.ConvertToUtf8Async(sourcePath, destPath);
            return new ImportResult { Success = true, Message = $"导入成功，已自动转换为 UTF-8（原编码: {detectedEnc?.WebName ?? "未知"}）", SourceEncoding = detectedEnc?.WebName ?? "未知", DestinationPath = destPath };
        }
        catch (Exception ex)
        {
            return new ImportResult { Success = false, Message = $"导入失败: {ex.Message}" };
        }
    }
    
    // 支持的视频格式
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"
    };

    public async Task<ImportResult> ImportVideoAsync(string sourcePath, string? targetDirectory = null)
    {
        var destDir = string.IsNullOrEmpty(targetDirectory) ? _videosDir : targetDirectory;

        var ext = Path.GetExtension(sourcePath).ToLower();
        if (!SupportedVideoExtensions.Contains(ext))
            return new ImportResult { Success = false, Message = $"不支持的格式: {ext}，支持的格式: {string.Join(", ", SupportedVideoExtensions)}" };

        var destPath = Path.Combine(destDir, Path.GetFileName(sourcePath));

        // 如果 ffprobe 不存在，直接复制文件
        if (!File.Exists(_ffprobePath))
        {
            File.Copy(sourcePath, destPath, true);
            return new ImportResult { Success = true, Message = "导入成功（未安装 ffprobe，跳过编码检测）", DestinationPath = destPath };
        }

        try
        {
            var codec = await GetVideoCodecAsync(sourcePath);

            if (codec.Contains("hevc", StringComparison.OrdinalIgnoreCase) || codec.Contains("h265", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destPath, true);
                return new ImportResult { Success = true, Message = "导入成功（已是 H.265）", DestinationPath = destPath };
            }

            return new ImportResult { Success = true, Message = $"检测到编码: {codec}，需要转换", SourceEncoding = codec, DestinationPath = destPath };
        }
        catch (Exception ex)
        {
            // 编码检测失败时，复制文件并标记需要转换
            File.Copy(sourcePath, destPath, true);
            return new ImportResult { Success = true, Message = $"导入成功（编码检测失败: {ex.Message}）", DestinationPath = destPath };
        }
    }
    
    public async Task<ConversionResult> ConvertVideoToH265Async(string sourcePath, string destPath, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffmpegPath))
            return new ConversionResult { Success = false, Message = "未找到 FFmpeg" };
        
        try
        {
            var useGpu = await IsGpuAccelAvailableAsync();
            
            var arguments = useGpu
                ? $"-hwaccel cuda -i \"{sourcePath}\" -c:v hevc_nvenc -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y"
                : $"-i \"{sourcePath}\" -c:v libx265 -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y";
            
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var duration = await GetVideoDurationAsync(sourcePath);
            var lastProgressTime = 0L;
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                var match = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2}\.\d+)");
                if (match.Success && duration > 0 && progress != null)
                {
                    var now = Environment.TickCount64;
                    if (now - lastProgressTime < 200) return;
                    lastProgressTime = now;
                    var hours = double.Parse(match.Groups[1].Value);
                    var minutes = double.Parse(match.Groups[2].Value);
                    var seconds = double.Parse(match.Groups[3].Value);
                    var currentTime = hours * 3600 + minutes * 60 + seconds;
                    progress.Report(currentTime / duration);
                }
            };
            
            process.Start();
            process.BeginErrorReadLine();
            
            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });
            
            await process.WaitForExitAsync(cancellationToken);
            
            if (cancellationToken.IsCancellationRequested)
            {
                if (File.Exists(destPath)) try { File.Delete(destPath); } catch { }
                return new ConversionResult { Success = false, Message = "已取消转换" };
            }
            
            if (process.ExitCode != 0 && useGpu)
            {
                if (File.Exists(destPath)) File.Delete(destPath);
                return await ConvertWithCpuFallbackAsync(sourcePath, destPath, progress, duration, cancellationToken);
            }
            
            if (process.ExitCode != 0)
                return new ConversionResult { Success = false, Message = "转换失败" };
            
            return new ConversionResult { Success = true, Message = useGpu ? "转换成功（GPU 加速）" : "转换成功", DestinationPath = destPath, SourceCodec = useGpu ? "hevc_nvenc" : "libx265" };
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(destPath)) try { File.Delete(destPath); } catch { }
            return new ConversionResult { Success = false, Message = "已取消转换" };
        }
        catch (Exception ex)
        {
            return new ConversionResult { Success = false, Message = $"转换失败: {ex.Message}" };
        }
    }
    
    private async Task<ConversionResult> ConvertWithCpuFallbackAsync(string sourcePath, string destPath, IProgress<double>? progress, double duration, CancellationToken cancellationToken = default)
    {
        var arguments = $"-i \"{sourcePath}\" -c:v libx265 -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y";
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var lastProgressTime = 0L;
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var match = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2}\.\d+)");
            if (match.Success && duration > 0 && progress != null)
            {
                var now = Environment.TickCount64;
                if (now - lastProgressTime < 200) return;
                lastProgressTime = now;
                var hours = double.Parse(match.Groups[1].Value);
                var minutes = double.Parse(match.Groups[2].Value);
                var seconds = double.Parse(match.Groups[3].Value);
                var currentTime = hours * 3600 + minutes * 60 + seconds;
                progress.Report(currentTime / duration);
            }
        };
        
        process.Start();
        process.BeginErrorReadLine();
        
        using var registration = cancellationToken.Register(() =>
        {
            try { process.Kill(true); } catch { }
        });
        
        await process.WaitForExitAsync(cancellationToken);
        
        if (cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(destPath)) try { File.Delete(destPath); } catch { }
            return new ConversionResult { Success = false, Message = "已取消转换" };
        }
        
        if (process.ExitCode != 0)
            return new ConversionResult { Success = false, Message = "转换失败（GPU 和 CPU 均失败）" };
        
        return new ConversionResult { Success = true, Message = "转换成功（CPU 回退）", DestinationPath = destPath };
    }
    
    private async Task<bool> IsGpuAccelAvailableAsync()
    {
        if (_gpuAccelAvailable.HasValue) return _gpuAccelAvailable.Value;
        
        if (!File.Exists(_ffmpegPath))
        {
            _gpuAccelAvailable = false;
            return false;
        }
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = "-hwaccels",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null)
            {
                _gpuAccelAvailable = false;
                return false;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            _gpuAccelAvailable = output.Contains("cuda", StringComparison.OrdinalIgnoreCase) || 
                                 output.Contains("nvdec", StringComparison.OrdinalIgnoreCase);
            return _gpuAccelAvailable.Value;
        }
        catch
        {
            _gpuAccelAvailable = false;
            return false;
        }
    }
    
    private async Task<string> GetVideoCodecAsync(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            Arguments = $"-v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 \"{path}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Trim();
    }
    
    private async Task<double> GetVideoDurationAsync(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return double.TryParse(output.Trim(), out var d) ? d : 0;
    }
}
