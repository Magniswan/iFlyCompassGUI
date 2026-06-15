using System.Diagnostics;
using System.Text.RegularExpressions;

namespace iFlyCompassGUI.Services;

public class DownloadService : IDownloadService
{
    private readonly string _aria2cPath;
    private readonly HttpClient _httpClient;
    private readonly ILogAggregatorService _logAggregator;

    public DownloadService(HttpClient httpClient, ILogAggregatorService logAggregator)
    {
        _httpClient = httpClient;
        _logAggregator = logAggregator;
        _aria2cPath = Path.Combine(AppContext.BaseDirectory, "tools", "aria2c", "aria2c.exe");
    }

    public Task<bool> IsAria2cAvailableAsync()
    {
        return Task.FromResult(File.Exists(_aria2cPath));
    }

    public async Task<string?> GetAria2cPathAsync()
    {
        return await IsAria2cAvailableAsync() ? _aria2cPath : null;
    }

    public async Task<DownloadResult> DownloadHttpAsync(string url, string destinationDirectory, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);

            if (await IsAria2cAvailableAsync())
            {
                return await DownloadWithAria2cAsync(url, destinationDirectory, progress, cancellationToken);
            }

            return await DownloadWithHttpClientAsync(url, destinationDirectory, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult { Success = false, Message = "下载已取消" };
        }
        catch (Exception ex)
        {
            return new DownloadResult { Success = false, Message = $"下载失败: {ex.Message}" };
        }
    }

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts"
    };

    // 种子缓存服务，用于将磁力链接转为 .torrent 文件（绕过国内 tracker/DHT 不通的问题）
    private static readonly string[] TorrentCacheUrls =
    [
        "https://itorrents.org/torrent/{0}.torrent",
        "https://btcache.me/torrent/{0}",
    ];

    // 国内可达的 tracker 优先排在前面
    private static readonly string[] PublicTrackers =
    [
        // 国内/亚洲可达
        "http://tracker.renfei.net:8080/announce",
        "http://share.camoe.cn:8080/announce",
        "http://t.acg.rip:6699/announce",
        "http://open.acgnxtracker.com:80/announce",
        "http://tracker.bt4g.com:2095/announce",
        "udp://tracker4.itzmx.com:2710/announce",
        "http://tracker3.itzmx.com:8080/announce",
        "udp://tracker.dler.org:6969/announce",
        "udp://tracker2.dler.org:80/announce",
        "http://tracker.gbitt.info:80/announce",
        "udp://tracker-udp.gbitt.info:80/announce",
        "http://p2p.0q0.xyz:80/announce",
        // HTTP trackers
        "http://tracker.opentrackr.org:1337/announce",
        "http://open.tracker.cl:1337/announce",
        "http://tracker.openbittorrent.com:6969/announce",
        "http://tracker.files.fm:6969/announce",
        "http://tracker.kamigami.org:2710/announce",
        "http://nyaa.tracker.wf:7777/announce",
        "http://anidex.moe:6969/announce",
        "http://t.nyaatracker.com:80/announce",
        // UDP trackers
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.tracker.cl:1337/announce",
        "udp://tracker.openbittorrent.com:6969/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce",
        "udp://explodie.org:6969/announce",
        "udp://p4p.arenabg.com:1337/announce",
        "udp://retracker.lanta-net.ru:2710/announce",
    ];

    public async Task<DownloadResult> DownloadBtAsync(string magnetOrTorrent, string destinationDirectory, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await IsAria2cAvailableAsync())
        {
            return new DownloadResult { Success = false, Message = "未找到 aria2c，请确认 tools/aria2c/ 目录中包含 aria2c.exe" };
        }

        try
        {
            // BT 下载到临时目录，避免污染目标文件夹
            var tempDir = Path.Combine(Path.GetTempPath(), $"iFlyCompass_bt_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            // 如果是磁力链接，先尝试通过种子缓存服务获取 .torrent 文件
            var downloadUrl = magnetOrTorrent;
            if (magnetOrTorrent.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                var torrentFile = await TryGetTorrentFromCacheAsync(magnetOrTorrent, tempDir, progress, cancellationToken);
                if (torrentFile != null)
                {
                    downloadUrl = torrentFile;
                }
            }

            var result = await DownloadWithAria2cAsync(downloadUrl, tempDir, progress, cancellationToken);

            if (!result.Success)
            {
                // 下载失败，清理临时目录
                try { Directory.Delete(tempDir, true); } catch { }
                return result;
            }

            // 扫描临时目录中的视频文件
            var videoFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            if (videoFiles.Count == 0)
            {
                try { Directory.Delete(tempDir, true); } catch { }
                return new DownloadResult { Success = false, Message = "种子中没有找到视频文件" };
            }

            result.DownloadedFilePaths = videoFiles;
            result.DownloadedFilePath = videoFiles[0];
            result.TempDirectory = tempDir;
            return result;
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult { Success = false, Message = "下载已取消" };
        }
        catch (Exception ex)
        {
            return new DownloadResult { Success = false, Message = $"BT 下载失败: {ex.Message}" };
        }
    }

    /// <summary>
    /// 从磁力链接中提取 info hash
    /// </summary>
    private static string? ExtractInfoHash(string magnetUrl)
    {
        var match = Regex.Match(magnetUrl, @"btih:([a-fA-F0-9]{40})", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.ToLowerInvariant();

        // 支持 Base32 编码的 hash
        var base32Match = Regex.Match(magnetUrl, @"btih:([A-Z2-7]{32})", RegexOptions.IgnoreCase);
        if (base32Match.Success)
        {
            return Base32ToHex(base32Match.Groups[1].Value.ToUpperInvariant());
        }

        return null;
    }

    private static string? Base32ToHex(string base32)
    {
        var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = "";
        foreach (var c in base32)
        {
            var idx = alphabet.IndexOf(c);
            if (idx < 0) return null;
            bits += Convert.ToString(idx, 2).PadLeft(5, '0');
        }

        var hex = "";
        for (var i = 0; i + 4 <= bits.Length; i += 4)
        {
            hex += Convert.ToInt32(bits.Substring(i, 4), 2).ToString("x");
        }
        return hex.Length >= 40 ? hex[..40] : null;
    }

    /// <summary>
    /// 尝试通过种子缓存服务将磁力链接转为 .torrent 文件
    /// </summary>
    private async Task<string?> TryGetTorrentFromCacheAsync(string magnetUrl, string tempDir, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var infoHash = ExtractInfoHash(magnetUrl);
        if (infoHash == null) return null;

        progress?.Report(new DownloadProgress
        {
            Progress = 0,
            SpeedBytesPerSecond = 0,
            Eta = null,
            StatusText = "正在通过种子缓存获取元数据..."
        });

        foreach (var cacheUrlTemplate in TorrentCacheUrls)
        {
            var cacheUrl = string.Format(cacheUrlTemplate, infoHash);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var response = await _httpClient.GetAsync(cacheUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode) continue;

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                var data = await response.Content.ReadAsByteArrayAsync(cts.Token);
                if (data.Length < 100) continue;

                // 验证是否是有效的 .torrent 文件（以 "d" 开头，B编码字典）
                if (data[0] != 'd') continue;

                var torrentPath = Path.Combine(tempDir, $"{infoHash}.torrent");
                await File.WriteAllBytesAsync(torrentPath, data, cts.Token);

                _logAggregator.AddLog("aria2c", "INFO", $"通过种子缓存获取到 .torrent 文件: {cacheUrl}");
                return torrentPath;
            }
            catch (OperationCanceledException) { continue; }
            catch (Exception ex)
            {
                _logAggregator.AddLog("aria2c", "WARN", $"种子缓存 {cacheUrl} 获取失败: {ex.Message}");
                continue;
            }
        }

        return null;
    }

    private async Task<DownloadResult> DownloadWithAria2cAsync(string url, string destinationDirectory, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var trackerList = string.Join(",", PublicTrackers);
        var arguments = $"--dir=\"{destinationDirectory}\" --continue=true --max-connection-per-server=16 --split=16 --min-split-size=1M --file-allocation=none --summary-interval=1 --console-log-level=warn --timeout=600 --connect-timeout=120 --max-tries=0 --retry-wait=10 --lowest-speed-limit=0 --disk-cache=64M --bt-max-peers=200 --bt-request-peer-speed-limit=0 --seed-time=0 --enable-dht --enable-dht6 --enable-peer-exchange --bt-enable-lpd --listen-port=6881-6999 --dht-listen-port=6881-6999 --dht-entry-point=router.bittorrent.com:6881 --dht-entry-point=router.utorrent.com:6881 --dht-entry-point=dht.transmissionbt.com:6881 --bt-tracker-connect-timeout=10 --bt-tracker-timeout=60 --bt-tracker-interval=30 --bt-save-metadata --bt-load-saved-metadata --bt-prioritize-piece=head=1M,tail=1M --async-dns-server=223.5.5.5,119.29.29.29 --bt-tracker=\"{trackerList}\" \"{url}\"";

        var psi = new ProcessStartInfo
        {
            FileName = _aria2cPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var lastProgressTime = 0L;
        var downloadedFileName = "";
        var errorOutput = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logAggregator.AddLog("aria2c", "INFO", e.Data);
            ParseAria2cOutput(e.Data, progress, ref lastProgressTime, ref downloadedFileName);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logAggregator.AddLog("aria2c", "ERROR", e.Data);
            errorOutput.Add(e.Data);
            ParseAria2cOutput(e.Data, progress, ref lastProgressTime, ref downloadedFileName);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try { process.Kill(true); } catch { }
        });

        await process.WaitForExitAsync(cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return new DownloadResult { Success = false, Message = "下载已取消" };
        }

        if (process.ExitCode != 0)
        {
            var errorMsg = errorOutput.Count > 0
                ? string.Join("\n", errorOutput.Take(5))
                : "";
            return new DownloadResult { Success = false, Message = $"aria2c 退出码: {process.ExitCode}\n{errorMsg}" };
        }

        // 优先使用从输出中解析到的文件名
        string? resultPath = null;
        if (!string.IsNullOrEmpty(downloadedFileName))
        {
            var candidate = Path.Combine(destinationDirectory, downloadedFileName);
            if (File.Exists(candidate))
                resultPath = candidate;
        }

        // 回退：查找目录中最新的非 .aria2 文件
        resultPath ??= Directory.GetFiles(destinationDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".aria2", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        return new DownloadResult
        {
            Success = true,
            Message = "下载完成",
            DownloadedFilePath = resultPath
        };
    }

    private static void ParseAria2cOutput(string line, IProgress<DownloadProgress>? progress, ref long lastProgressTime, ref string downloadedFileName)
    {
        var now = Environment.TickCount64;

        // 下载完成行: 03/13 12:34:56 [NOTICE] Download complete: /path/to/file.mp4
        var completeMatch = Regex.Match(line, @"Download complete:\s*(.+)$");
        if (completeMatch.Success)
        {
            downloadedFileName = Path.GetFileName(completeMatch.Groups[1].Value.Trim());
            return;
        }

        // 下载中: [#HASH 1.2GiB/3.4GiB(35%) CN:16 DL:12.3MiB ETA:3m20s]
        var dlMatch = Regex.Match(line, @"\[[#]\w+\s+[\d.]+\w*/[\d.]+\w*\((\d+)%\).*?DL:([\d.]+)([KMGT]?)(i?B)/s\s+ETA:([^\]]+)\]");
        if (dlMatch.Success && progress != null)
        {
            lastProgressTime = now;
            var percent = double.Parse(dlMatch.Groups[1].Value);
            var speed = ParseSize(dlMatch.Groups[2].Value, dlMatch.Groups[3].Value, dlMatch.Groups[4].Value);
            var eta = ParseEta(dlMatch.Groups[5].Value.Trim());

            progress.Report(new DownloadProgress
            {
                Progress = percent,
                SpeedBytesPerSecond = speed,
                Eta = eta,
                StatusText = FormatProgressText(percent, speed, eta)
            });
            return;
        }

        // 种子元数据获取: [MetaDL:0.5B ...]
        var metaMatch = Regex.Match(line, @"\[MetaDL:([^\]]+)\]");
        if (metaMatch.Success && progress != null)
        {
            lastProgressTime = now;
            // 提取已连接数
            var cnMatch = Regex.Match(line, @"CN:(\d+)");
            var cnText = cnMatch.Success ? $" (已连接 {cnMatch.Groups[1].Value} 个节点)" : "";
            progress.Report(new DownloadProgress
            {
                Progress = 0,
                SpeedBytesPerSecond = 0,
                Eta = null,
                StatusText = $"正在获取种子元数据...{cnText}"
            });
            return;
        }

        // 等待中/连接中: 提取连接数和种子数
        if (progress != null && (line.Contains("CN:") || line.Contains("SD:")))
        {
            var cnMatch = Regex.Match(line, @"CN:(\d+)");
            var sdMatch = Regex.Match(line, @"SD:(\d+)");
            var cn = cnMatch.Success ? int.Parse(cnMatch.Groups[1].Value) : 0;
            var sd = sdMatch.Success ? int.Parse(sdMatch.Groups[1].Value) : 0;

            if (now - lastProgressTime < 1000) return;
            lastProgressTime = now;

            string statusText;
            if (cn == 0 && sd == 0)
            {
                statusText = "正在连接节点... (等待tracker响应)";
            }
            else if (cn == 0 && sd > 0)
            {
                statusText = $"正在连接节点... (已发现 {sd} 个种子，等待握手)";
            }
            else
            {
                statusText = $"正在连接节点... (已连接 {cn} 个节点, {sd} 个种子)";
            }

            progress.Report(new DownloadProgress
            {
                Progress = 0,
                SpeedBytesPerSecond = 0,
                Eta = null,
                StatusText = statusText
            });
        }
    }

    private static TimeSpan? ParseEta(string eta)
    {
        if (string.IsNullOrEmpty(eta) || eta == "0s" || eta == "?") return null;

        try
        {
            var totalSeconds = 0.0;
            // 格式: 1h2m3s 或 3m20s 或 20s
            var hMatch = Regex.Match(eta, @"(\d+)h");
            var mMatch = Regex.Match(eta, @"(\d+)m");
            var sMatch = Regex.Match(eta, @"(\d+)s");
            if (hMatch.Success) totalSeconds += int.Parse(hMatch.Groups[1].Value) * 3600;
            if (mMatch.Success) totalSeconds += int.Parse(mMatch.Groups[1].Value) * 60;
            if (sMatch.Success) totalSeconds += int.Parse(sMatch.Groups[1].Value);
            return totalSeconds > 0 ? TimeSpan.FromSeconds(totalSeconds) : null;
        }
        catch { return null; }
    }

    private static string FormatProgressText(double percent, double speed, TimeSpan? eta)
    {
        var text = $"{percent:F1}%";
        if (speed > 0) text += $" · {FormatSpeedStatic(speed)}";
        if (eta.HasValue) text += $" · 剩余 {FormatEta(eta.Value)}";
        return text;
    }

    private static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalHours >= 1) return $"{(int)eta.TotalHours}h{eta.Minutes}m";
        if (eta.TotalMinutes >= 1) return $"{eta.Minutes}m{eta.Seconds}s";
        return $"{eta.Seconds}s";
    }

    private static double ParseSize(string value, string prefix, string unit)
    {
        var num = double.Parse(value);
        var multiplier = prefix.ToUpperInvariant() switch
        {
            "K" or "KI" => 1024,
            "M" or "MI" => 1024 * 1024,
            "G" or "GI" => 1024 * 1024 * 1024,
            "T" or "TI" => 1024L * 1024 * 1024 * 1024,
            _ => 1
        };
        return num * multiplier;
    }

    private static string FormatSpeedStatic(double bytesPerSecond)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var speed = bytesPerSecond;
        var i = 0;
        while (speed >= 1024 && i < units.Length - 1) { speed /= 1024; i++; }
        return $"{speed:0.##} {units[i]}";
    }

    private async Task<DownloadResult> DownloadWithHttpClientAsync(string url, string destinationDirectory, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var fileName = GetFileNameFromResponse(response, url);
        var destPath = Path.Combine(destinationDirectory, fileName);

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

        var buffer = new byte[65536];
        long downloadedBytes = 0;
        var lastProgressTime = 0L;
        var lastSpeedCalcTime = Environment.TickCount64;
        long lastSpeedCalcBytes = 0;
        double currentSpeed = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, cts.Token)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
            downloadedBytes += bytesRead;

            var now = Environment.TickCount64;
            if (now - lastProgressTime < 300) continue;
            lastProgressTime = now;

            // 计算速度（每秒更新）
            var elapsed = (now - lastSpeedCalcTime) / 1000.0;
            if (elapsed >= 1.0)
            {
                currentSpeed = (downloadedBytes - lastSpeedCalcBytes) / elapsed;
                lastSpeedCalcTime = now;
                lastSpeedCalcBytes = downloadedBytes;
            }

            var percent = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
            TimeSpan? eta = null;
            if (currentSpeed > 0 && totalBytes > 0)
            {
                var remaining = totalBytes - downloadedBytes;
                eta = TimeSpan.FromSeconds(remaining / currentSpeed);
            }

            progress?.Report(new DownloadProgress
            {
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes,
                Progress = percent,
                SpeedBytesPerSecond = currentSpeed,
                Eta = eta,
                StatusText = FormatProgressText(percent, currentSpeed, eta)
            });
        }

        return new DownloadResult
        {
            Success = true,
            Message = "下载完成",
            DownloadedFilePath = destPath
        };
    }

    private static string GetFileNameFromResponse(HttpResponseMessage response, string url)
    {
        if (response.Content.Headers.ContentDisposition?.FileNameStar != null)
        {
            var name = response.Content.Headers.ContentDisposition.FileNameStar.Trim('"');
            return SanitizeFileName(name);
        }
        if (response.Content.Headers.ContentDisposition?.FileName != null)
        {
            var name = response.Content.Headers.ContentDisposition.FileName.Trim('"');
            return SanitizeFileName(name);
        }

        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var fileName = Path.GetFileName(Uri.UnescapeDataString(path));
            if (!string.IsNullOrEmpty(fileName) && fileName.Contains('.'))
                return SanitizeFileName(fileName);
        }
        catch { }

        return $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid));
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
}
