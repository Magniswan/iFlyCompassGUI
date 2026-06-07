using Windows.ApplicationModel;
using Windows.Storage;

namespace iFlyCompassGUI.Helpers;

/// <summary>
/// Provides path helpers for MSIX-packaged and unpackaged scenarios.
/// MSIX-packaged apps run from a read-only directory, so data must be stored elsewhere.
/// </summary>
public static class PathHelper
{
    private static readonly Lazy<bool> _isPackaged = new(() =>
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    });

    private static readonly Lazy<string> _installedLocation = new(() =>
    {
        if (IsPackaged)
        {
            return Package.Current.InstalledLocation.Path;
        }

        return AppContext.BaseDirectory;
    });

    private static readonly Lazy<string> _dataDirectory = new(() =>
    {
        if (IsPackaged)
        {
            // Use ApplicationData.Current.LocalFolder for MSIX-packaged apps
            // This resolves to %LocalAppData%\Packages\{PackageFamilyName}\LocalState\
            return ApplicationData.Current.LocalFolder.Path;
        }

        return AppContext.BaseDirectory;
    });

    /// <summary>
    /// Gets whether the app is running as an MSIX package.
    /// </summary>
    public static bool IsPackaged => _isPackaged.Value;

    /// <summary>
    /// Gets the writable data directory for the application.
    /// For MSIX-packaged apps, this is ApplicationData.Current.LocalFolder
    ///   (typically %LocalAppData%\Packages\{PackageFamilyName}\LocalState\).
    /// For unpackaged apps, this is the application base directory.
    /// </summary>
    public static string DataDirectory => _dataDirectory.Value;

    /// <summary>
    /// Gets the read-only installed location of the application.
    /// For MSIX-packaged apps, this is the package install directory.
    /// For unpackaged apps, this is the application base directory.
    /// </summary>
    public static string InstalledLocation => _installedLocation.Value;
}
