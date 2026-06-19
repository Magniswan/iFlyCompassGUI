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
    private readonly ILogAggregatorService _logAggregator;
    private bool? _gpuAccelAvailable;

    public FileImportService(ILogAggregatorService logAggregator)
    {
        _logAggregator = logAggregator;
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
                _logAggregator.AddLog("ffmpeg", "INFO", e.Data);
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
            JobObjectHelper.Assign(process);
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
                return await ConvertWithCpuFallbackAsync(sourcePath, destPath, "", progress, duration, cancellationToken);
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

    public async Task<ConversionResult> ConvertVideoWithResolutionAsync(string sourcePath, string destPath, int? width = null, int? height = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffmpegPath))
            return new ConversionResult { Success = false, Message = "未找到 FFmpeg" };

        try
        {
            var useGpu = await IsGpuAccelAvailableAsync();

            // 构建 scale 滤镜
            var scaleFilter = "";
            if (width.HasValue || height.HasValue)
            {
                var w = width.HasValue ? width.Value.ToString() : "-1";
                var h = height.HasValue ? height.Value.ToString() : "-1";
                scaleFilter = $" -vf scale={w}:{h}";
            }

            var arguments = useGpu
                ? $"-hwaccel cuda -i \"{sourcePath}\"{scaleFilter} -c:v hevc_nvenc -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y"
                : $"-i \"{sourcePath}\"{scaleFilter} -c:v libx265 -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y";

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
                _logAggregator.AddLog("ffmpeg", "INFO", e.Data);
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
            JobObjectHelper.Assign(process);
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
                return await ConvertWithCpuFallbackAsync(sourcePath, destPath, scaleFilter, progress, duration, cancellationToken);
            }

            if (process.ExitCode != 0)
                return new ConversionResult { Success = false, Message = "转换失败" };

            var resInfo = (width.HasValue || height.HasValue) ? $" ({width ?? -1}x{height ?? -1})" : "";
            return new ConversionResult { Success = true, Message = $"转换成功{resInfo}" + (useGpu ? "（GPU 加速）" : ""), DestinationPath = destPath };
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

    public async Task<ConversionResult> ConvertVideoAsync(string sourcePath, string destPath, string codec = "h265", int? width = null, int? height = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffmpegPath))
            return new ConversionResult { Success = false, Message = "未找到 FFmpeg" };

        try
        {
            var useGpu = await IsGpuAccelAvailableAsync();
            var isH265 = codec.Equals("h265", StringComparison.OrdinalIgnoreCase) ||
                         codec.Equals("hevc", StringComparison.OrdinalIgnoreCase);

            // 构建 scale 滤镜
            var scaleFilter = "";
            if (width.HasValue || height.HasValue)
            {
                var w = width.HasValue ? width.Value.ToString() : "-1";
                var h = height.HasValue ? height.Value.ToString() : "-1";
                scaleFilter = $" -vf scale={w}:{h}";
            }

            string arguments;
            if (isH265)
            {
                arguments = useGpu
                    ? $"-hwaccel cuda -i \"{sourcePath}\"{scaleFilter} -c:v hevc_nvenc -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y"
                    : $"-i \"{sourcePath}\"{scaleFilter} -c:v libx265 -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y";
            }
            else
            {
                arguments = useGpu
                    ? $"-hwaccel cuda -i \"{sourcePath}\"{scaleFilter} -c:v h264_nvenc -crf 23 -c:a copy \"{destPath}\" -y"
                    : $"-i \"{sourcePath}\"{scaleFilter} -c:v libx264 -crf 23 -preset medium -c:a copy \"{destPath}\" -y";
            }

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
                _logAggregator.AddLog("ffmpeg", "INFO", e.Data);
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
            JobObjectHelper.Assign(process);
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
                return await ConvertWithCpuFallbackAsync(sourcePath, destPath, scaleFilter, progress, duration, cancellationToken);
            }

            if (process.ExitCode != 0)
                return new ConversionResult { Success = false, Message = "转换失败" };

            var codecName = isH265 ? "H.265" : "H.264";
            var resInfo = (width.HasValue || height.HasValue) ? $" ({width ?? -1}x{height ?? -1})" : "";
            return new ConversionResult { Success = true, Message = $"转换成功 ({codecName}{resInfo})" + (useGpu ? "（GPU 加速）" : ""), DestinationPath = destPath };
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

    public async Task<ConversionResult> RemuxToMp4Async(string sourcePath, CancellationToken cancellationToken = default)
    {
        // 已是 .mp4：无需处理
        if (Path.GetExtension(sourcePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return new ConversionResult { Success = true, Message = "已是 mp4", DestinationPath = sourcePath };

        var destPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            Path.GetFileNameWithoutExtension(sourcePath) + ".mp4");

        // 避免与已有同名 mp4 冲突
        if (File.Exists(destPath))
        {
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            destPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, $"{name}_{Guid.NewGuid():N}"[..(name.Length + 9)] + ".mp4");
        }

        // 无 ffmpeg：只能直接重命名扩展名（容器可能不匹配，但保证文件可见）
        if (!File.Exists(_ffmpegPath))
        {
            try
            {
                File.Move(sourcePath, destPath, true);
                return new ConversionResult { Success = true, Message = "已重命名为 mp4（未安装 ffmpeg，未转封装）", DestinationPath = destPath };
            }
            catch (Exception ex)
            {
                return new ConversionResult { Success = false, Message = $"转封装失败: {ex.Message}" };
            }
        }

        // 优先 -c copy 快速转封装（不重编码）；失败则回退为音频重编码 aac
        var result = await RunRemuxAsync(sourcePath, destPath, "-c copy", cancellationToken);
        if (!result.Success && !cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(destPath)) try { File.Delete(destPath); } catch { }
            result = await RunRemuxAsync(sourcePath, destPath, "-c:v copy -c:a aac", cancellationToken);
        }

        if (result.Success)
        {
            // 转封装成功，删除原始非 mp4 文件
            try { File.Delete(sourcePath); } catch { }
        }
        else if (File.Exists(destPath))
        {
            try { File.Delete(destPath); } catch { }
        }

        return result;
    }

    private async Task<ConversionResult> RunRemuxAsync(string sourcePath, string destPath, string codecArgs, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = $"-i \"{sourcePath}\" -map 0:v:0 -map 0:a? {codecArgs} -movflags +faststart \"{destPath}\" -y",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) _logAggregator.AddLog("ffmpeg", "INFO", e.Data);
            };

            process.Start();
            JobObjectHelper.Assign(process);
            process.BeginErrorReadLine();

            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            await process.WaitForExitAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return new ConversionResult { Success = false, Message = "已取消" };

            return process.ExitCode == 0
                ? new ConversionResult { Success = true, Message = "转封装成功", DestinationPath = destPath }
                : new ConversionResult { Success = false, Message = $"转封装失败 (exit {process.ExitCode})" };
        }
        catch (OperationCanceledException)
        {
            return new ConversionResult { Success = false, Message = "已取消" };
        }
        catch (Exception ex)
        {
            return new ConversionResult { Success = false, Message = $"转封装失败: {ex.Message}" };
        }
    }

    private async Task<ConversionResult> ConvertWithCpuFallbackAsync(string sourcePath, string destPath, string scaleFilter, IProgress<double>? progress, double duration, CancellationToken cancellationToken = default)
    {
        var arguments = $"-i \"{sourcePath}\"{scaleFilter} -c:v libx265 -crf 28 -tag:v hvc1 -c:a copy \"{destPath}\" -y";
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
            _logAggregator.AddLog("ffmpeg", "INFO", e.Data);
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
            JobObjectHelper.Assign(process);

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
        JobObjectHelper.Assign(process);
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
        JobObjectHelper.Assign(process);
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return double.TryParse(output.Trim(), out var d) ? d : 0;
    }
}
