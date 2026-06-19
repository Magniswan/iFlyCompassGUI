using System.Diagnostics;
using System.Text.RegularExpressions;
using iFlyCompassGUI.Helpers;

namespace iFlyCompassGUI.Services;

public class DownloadService : IDownloadService
{
    private readonly string _aria2cPath;
    private readonly string _aria2StateDir;
    private readonly HttpClient _httpClient;
    private readonly ILogAggregatorService _logAggregator;

    public DownloadService(HttpClient httpClient, ILogAggregatorService logAggregator)
    {
        _httpClient = httpClient;
        _logAggregator = logAggregator;
        _aria2cPath = Path.Combine(AppContext.BaseDirectory, "tools", "aria2c", "aria2c.exe");
        // 持久化 DHT 路由表 / tracker 列表缓存，避免每次下载都从零启动节点发现
        _aria2StateDir = Path.Combine(PathHelper.DataDirectory, "aria2");
        try { Directory.CreateDirectory(_aria2StateDir); } catch { }
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
    // 这些服务会被并发请求，谁先返回有效种子谁胜出，避免串行等待超时
    private static readonly string[] TorrentCacheUrls =
    [
        "https://itorrents.org/torrent/{0}.torrent",
        "https://btcache.me/torrent/{0}",
        "https://torrage.info/torrent/{0}.torrent",
        "https://torrentproject.se/torrent/{0}.torrent",
    ];

    // 在线维护的 tracker 列表（通过 jsDelivr CDN，国内可达），定期刷新以替换失效节点
    private static readonly string[] TrackerListUrls =
    [
        "https://cdn.jsdelivr.net/gh/ngosang/trackerslist@master/trackers_best.txt",
        "https://fastly.jsdelivr.net/gh/ngosang/trackerslist@master/trackers_best.txt",
        "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_best.txt",
    ];

    // tracker 列表本地缓存有效期
    private static readonly TimeSpan TrackerCacheTtl = TimeSpan.FromHours(12);

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

            var result = await DownloadWithAria2cAsync(downloadUrl, tempDir, progress, cancellationToken, isBt: true);

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
    /// 尝试通过种子缓存服务将磁力链接转为 .torrent 文件。
    /// 多个缓存服务并发请求，谁先返回有效种子谁胜出，其余请求立即取消，
    /// 避免串行逐个等待超时（旧实现最坏情况要等 4×15s）。
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

        // 统一的取消源：任一服务成功后取消其余请求
        using var winnerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        winnerCts.CancelAfter(TimeSpan.FromSeconds(12));

        var tasks = TorrentCacheUrls
            .Select(template => FetchTorrentFromCacheAsync(string.Format(template, infoHash), winnerCts.Token))
            .ToList();

        var pending = new List<Task<byte[]?>>(tasks);
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);

            var data = await finished;
            if (data == null) continue;

            // 命中：取消其余并发请求并落盘
            await winnerCts.CancelAsync();
            var torrentPath = Path.Combine(tempDir, $"{infoHash}.torrent");
            try
            {
                await File.WriteAllBytesAsync(torrentPath, data, cancellationToken);
                _logAggregator.AddLog("aria2c", "INFO", "已通过种子缓存获取到 .torrent 文件，跳过元数据交换");
                return torrentPath;
            }
            catch (Exception ex)
            {
                _logAggregator.AddLog("aria2c", "WARN", $"写入缓存种子失败: {ex.Message}");
                return null;
            }
        }

        _logAggregator.AddLog("aria2c", "INFO", "种子缓存未命中，将回退到 DHT/tracker 元数据交换");
        return null;
    }

    /// <summary>
    /// 从单个缓存服务获取并校验 .torrent 数据；失败/无效返回 null（不抛出，便于并发竞速）。
    /// </summary>
    private async Task<byte[]?> FetchTorrentFromCacheAsync(string cacheUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(cacheUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (data.Length < 100) return null;

            // 验证是否是有效的 .torrent 文件（以 "d" 开头，B编码字典）
            if (data[0] != 'd') return null;

            return data;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logAggregator.AddLog("aria2c", "WARN", $"种子缓存 {cacheUrl} 获取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取可用的 tracker 列表：优先使用在线维护的列表（带 12h 本地缓存），
    /// 与内置的国内可达 tracker 合并去重。失效 tracker 越少，磁力链接连上节点越快。
    /// </summary>
    private async Task<string> GetTrackerListAsync(CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(_aria2StateDir, "trackers.txt");
        var fresh = await GetCachedOrFetchTrackersAsync(cachePath, cancellationToken);

        // 国内可达 tracker 排在前面，再追加在线列表，去重后用逗号拼接
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in PublicTrackers.Concat(fresh))
        {
            var trimmed = t.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed)) merged.Add(trimmed);
        }
        return string.Join(",", merged);
    }

    private async Task<IEnumerable<string>> GetCachedOrFetchTrackersAsync(string cachePath, CancellationToken cancellationToken)
    {
        // 缓存仍在有效期内则直接复用
        try
        {
            if (File.Exists(cachePath) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TrackerCacheTtl)
            {
                var cached = await File.ReadAllLinesAsync(cachePath, cancellationToken);
                if (cached.Length > 0) return cached;
            }
        }
        catch { }

        // 尝试从在线源刷新（限时，避免拖慢下载启动）
        foreach (var url in TrackerListUrls)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(8));

                var text = await _httpClient.GetStringAsync(url, cts.Token);
                var trackers = text
                    .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(l => l.Contains("://"))
                    .ToArray();

                if (trackers.Length > 0)
                {
                    try { await File.WriteAllLinesAsync(cachePath, trackers, cancellationToken); } catch { }
                    _logAggregator.AddLog("aria2c", "INFO", $"已刷新 tracker 列表（{trackers.Length} 个）");
                    return trackers;
                }
            }
            catch { /* 换下一个源 */ }
        }

        // 在线刷新失败：回退到过期缓存（如有），否则只用内置列表
        try
        {
            if (File.Exists(cachePath))
            {
                var stale = await File.ReadAllLinesAsync(cachePath, cancellationToken);
                if (stale.Length > 0) return stale;
            }
        }
        catch { }

        return [];
    }

    private async Task<DownloadResult> DownloadWithAria2cAsync(string url, string destinationDirectory, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken, bool isBt = false)
    {
        // 通用参数：HTTP / BT 均适用
        var args = new List<string>
        {
            $"--dir=\"{destinationDirectory}\"",
            "--continue=true",
            "--max-connection-per-server=16",
            "--split=16",
            "--min-split-size=1M",
            "--file-allocation=none",
            "--summary-interval=1",
            "--console-log-level=warn",
            "--connect-timeout=30",
            "--timeout=600",
            "--max-tries=0",
            "--retry-wait=5",
            "--lowest-speed-limit=0",
            "--disk-cache=64M",
            "--async-dns-server=223.5.5.5,119.29.29.29,1.1.1.1",
        };

        if (isBt)
        {
            // DHT 路由表持久化：磁力链接的元数据/节点发现可跨次复用，避免每次冷启动
            var dhtFile = Path.Combine(_aria2StateDir, "dht.dat");
            var dht6File = Path.Combine(_aria2StateDir, "dht6.dat");

            // 获取在线维护的 tracker 列表（带缓存），失效 tracker 越少连得越快
            var trackerList = await GetTrackerListAsync(cancellationToken);

            args.AddRange(
            [
                "--bt-max-peers=0",                       // 不限制 peer 连接数，最大化拉取速度
                "--bt-request-peer-speed-limit=10M",      // 提前转入做种判定的速度阈值，加快首块到手
                "--max-overall-upload-limit=1K",          // 限制上传，避免占用上行影响下载
                "--seed-time=0",                          // 下完即停，不做种
                "--enable-dht=true",
                "--enable-dht6=true",
                "--enable-peer-exchange=true",
                "--bt-enable-lpd=true",
                "--listen-port=6881-6999",
                "--dht-listen-port=6881-6999",
                $"--dht-file-path=\"{dhtFile}\"",
                $"--dht-file-path6=\"{dht6File}\"",
                "--dht-entry-point=router.bittorrent.com:6881",
                "--dht-entry-point=router.utorrent.com:6881",
                "--dht-entry-point=dht.transmissionbt.com:6881",
                "--dht-entry-point6=router.bittorrent.com:6881",
                "--bt-tracker-connect-timeout=10",
                "--bt-tracker-timeout=60",
                "--bt-tracker-interval=20",
                "--bt-save-metadata=true",
                "--bt-load-saved-metadata=true",
                "--bt-prioritize-piece=head=1M,tail=1M",
                "--bt-metadata-only=false",
                "--follow-torrent=true",
            ]);

            if (!string.IsNullOrEmpty(trackerList))
            {
                args.Add($"--bt-tracker=\"{trackerList}\"");
            }
        }

        args.Add($"\"{url}\"");

        var psi = new ProcessStartInfo
        {
            FileName = _aria2cPath,
            Arguments = string.Join(" ", args),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var lastProgressTime = 0L;
        var downloadedFileName = "";
        var currentFile = "";
        var errorOutput = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logAggregator.AddLog("aria2c", "INFO", e.Data);
            ParseAria2cOutput(e.Data, progress, ref lastProgressTime, ref downloadedFileName, ref currentFile);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logAggregator.AddLog("aria2c", "ERROR", e.Data);
            errorOutput.Add(e.Data);
            ParseAria2cOutput(e.Data, progress, ref lastProgressTime, ref downloadedFileName, ref currentFile);
        };

        process.Start();
        JobObjectHelper.Assign(process);
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

    private static void ParseAria2cOutput(string line, IProgress<DownloadProgress>? progress, ref long lastProgressTime, ref string downloadedFileName, ref string currentFile)
    {
        var now = Environment.TickCount64;

        // 当前下载文件行: FILE: C:/path/to/Ne Zha 2 ... BONE.mkv
        // aria2 在每次进度汇总后单独打印，记下文件名供进度行展示
        var fileMatch = Regex.Match(line, @"FILE:\s*(.+)$");
        if (fileMatch.Success)
        {
            currentFile = Path.GetFileName(fileMatch.Groups[1].Value.Trim());
            return;
        }

        // 下载完成行: 03/13 12:34:56 [NOTICE] Download complete: /path/to/file.mp4
        var completeMatch = Regex.Match(line, @"Download complete:\s*(.+)$");
        if (completeMatch.Success)
        {
            downloadedFileName = Path.GetFileName(completeMatch.Groups[1].Value.Trim());
            return;
        }

        // 下载进度行（HTTP 与 BT 通用）:
        //   [#HASH 1.2GiB/3.4GiB(35%) CN:16 SD:2 DL:12MiB UL:0.5MiB ETA:3m20s]
        // 注意: aria2 的 DL/UL 速度没有 "/s" 后缀，且 ETA 在速度为 0 时会缺失，
        //       旧正则强制要求 "/s" 和 ETA，导致进度行永远匹配不上而误判为"正在连接"。
        var pctMatch = Regex.Match(line, @"\[#\w+\s+([\d.]+[KMGTP]?i?B)/([\d.]+[KMGTP]?i?B)\((\d+(?:\.\d+)?)%\)");
        if (pctMatch.Success && progress != null)
        {
            lastProgressTime = now;
            var downloaded = (long)ParseAria2Size(pctMatch.Groups[1].Value);
            var total = (long)ParseAria2Size(pctMatch.Groups[2].Value);
            var percent = double.Parse(pctMatch.Groups[3].Value);

            // 速度: DL:12MiB（无 /s 后缀）
            double speed = 0;
            var dlMatch = Regex.Match(line, @"DL:([\d.]+[KMGTP]?i?B)");
            if (dlMatch.Success) speed = ParseAria2Size(dlMatch.Groups[1].Value);

            // ETA 可能缺失
            TimeSpan? eta = null;
            var etaMatch = Regex.Match(line, @"ETA:(\S+?)\]");
            if (etaMatch.Success) eta = ParseEta(etaMatch.Groups[1].Value.Trim());

            // 连接数 / 做种数（BT 才有 SD）
            var cn = Regex.Match(line, @"CN:(\d+)");
            var sd = Regex.Match(line, @"SD:(\d+)");

            progress.Report(new DownloadProgress
            {
                Progress = percent,
                DownloadedBytes = downloaded,
                TotalBytes = total,
                SpeedBytesPerSecond = speed,
                Eta = eta,
                Connections = cn.Success ? int.Parse(cn.Groups[1].Value) : 0,
                Seeders = sd.Success ? int.Parse(sd.Groups[1].Value) : -1,
                FileName = string.IsNullOrEmpty(currentFile) ? null : currentFile,
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
            var cnVal = cnMatch.Success ? int.Parse(cnMatch.Groups[1].Value) : 0;
            var cnText = cnMatch.Success ? $" (已连接 {cnVal} 个节点)" : "";
            progress.Report(new DownloadProgress
            {
                Progress = 0,
                SpeedBytesPerSecond = 0,
                Eta = null,
                Connections = cnVal,
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
                statusText = "正在连接节点... (等待 tracker 响应)";
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
                Connections = cn,
                Seeders = sd,
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

    /// <summary>
    /// 解析 aria2 的体积/速度 token，如 "1.2GiB"、"12MiB"、"500B"、"3.4G" 等，返回字节数。
    /// </summary>
    private static double ParseAria2Size(string token)
    {
        var m = Regex.Match(token, @"([\d.]+)\s*([KMGTP]?)i?B?", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, out var num)) return 0;

        var multiplier = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "K" => 1024.0,
            "M" => 1024.0 * 1024,
            "G" => 1024.0 * 1024 * 1024,
            "T" => 1024.0 * 1024 * 1024 * 1024,
            "P" => 1024.0 * 1024 * 1024 * 1024 * 1024,
            _ => 1.0
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
