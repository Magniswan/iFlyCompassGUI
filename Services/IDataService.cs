namespace iFlyCompassGUI.Services;

public interface IDataService
{
    Task<DataTransferResult> ExportInstanceAsync(string destinationFolder, IProgress<DataTransferProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<DataTransferResult> ImportInstanceAsync(string sourceFolder, IProgress<DataTransferProgress>? progress = null, CancellationToken cancellationToken = default);
}

public class DataTransferProgress
{
    public int CurrentFile { get; set; }
    public int TotalFiles { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public double Progress => TotalFiles > 0 ? (double)CurrentFile / TotalFiles * 100 : 0;
}

public class DataTransferResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int FilesTransferred { get; set; }
}
