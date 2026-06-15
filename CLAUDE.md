# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

iFlyCompassGUI is a WinUI 3 desktop app (.NET 10, Windows App SDK 2.1.3) that installs, configures, and manages the [iFlyCompass](https://github.com/MoyuZJ912/iFlyCompass) Python service. The GUI downloads the iFlyCompass release, sets up an embedded Python 3.12.10 runtime, installs pip dependencies, and runs/monitors the Python `app.py` server (listens on port 5002). Most user-facing text and code comments are in Chinese.

## Build & run

```bash
dotnet restore iFlyCompassGUI.csproj
dotnet build iFlyCompassGUI.csproj -c Debug -r win-x64
```

- Target is `net10.0-windows10.0.19041.0`, x64 only. The .NET SDK is pinned to `10.0.100` via `global.json` (rollForward: latestFeature).
- For local debugging, run from Visual Studio with the `iFlyCompassGUI (Unpackaged)` debug profile, or `dotnet build` then launch the produced exe under `bin/x64/Debug/`.
- The csproj produces an MSIX package on build (`WindowsPackageType=MSIX`, `GenerateAppxPackageOnBuild=true`). Package signing uses a thumbprint from `cert/cert-password.props`, which is gitignored — builds work without it on a dev machine but signing/packaging steps need the cert.
- There is **no test project** — no test command exists.

### Full signed installer

`./build-installer.ps1` builds the MSIX, exports the `.cer`, resolves the .NET 10 Desktop Runtime download URL, and builds the `installer/Bootstrapper` project into a self-contained `iFlyCompassGUI-Setup.exe`. The `installer/` directory is excluded from the main project's compilation (see the `Compile Remove` item group in the csproj). CI (`.github/workflows/build.yml`) runs this same flow on `v*` tags, producing the MSIX, `.cer`, and Setup.exe as release assets.

## Architecture

Single-project MVVM app. The big picture spans three layers wired together by DI.

**Composition root — `App.xaml.cs`.** `ConfigureServices()` registers every service and ViewModel in an `IServiceProvider`. Services are singletons; ViewModels are mostly singletons except `MainViewModel`, `WelcomeViewModel`, and `InstallViewModel` (transient). There is no constructor-injected resolution in views — `MainWindow` and pages pull dependencies manually via `((App)App.Current).Services.GetService(...)`. On launch, `App` loads config, then either attaches to an already-running Python process or auto-starts it if `AutoStartApp` and `IsInstalled` are set.

**Shell & navigation — `MainWindow.xaml(.cs)`.** Two `Frame`s drive the whole UX:
- `WelcomeFrame` — shown when iFlyCompass is *not* installed. Routes to `WelcomePage`, or directly to `InstallPage` if a previous install was interrupted (`IsPartiallyInstalled`).
- `ContentFrame` — shown when installed, inside a `NavigationView` sidebar. Page selection is a `tag`→`Type` switch (`Home`/`Novel`/`Video`/`AI`/`Users`/`Log`/`About`/`Settings`). The last-selected page and window position/size are persisted to settings on navigation and on window close, and restored on next launch.

`NavigateToHome()` / `NavigateToWelcome()` flip the installed state and switch frames — these are how the install flow transitions into the main app.

**Services (`Services/`, interface + impl per service).**

| Service | Role |
|---|---|
| `InstallService` | Orchestrates install: download iFlyCompass release, unzip embedded Python 3.12.10, pip-install requirements |
| `ProcessService` | Start/stop/restart/attach the Python `app.py` process on port 5002; checks port-in-use; streams output via `LogOutputReceived` |
| `ConfigService` | Loads/saves `AppSettings` as JSON (`config/settings.json`, gitignored, auto-created) |
| `UpdateService` | Updates the iFlyCompass Python app from GitHub releases with backup/rollback (preserves `instance`/`temp` folders) |
| `AppUpdateService` | Checks for new iFlyCompassGUI desktop releases on GitHub (`Services/appupdateservice.cs`) |
| `UserDbService` | SQLite-backed user CRUD via `Microsoft.Data.Sqlite` |
| `FileImportService` | Novel import with non-UTF8→UTF-8 conversion; video import (H.265) |
| `DownloadService` | HTTP and BitTorrent downloads (BT via bundled `tools/aria2c`); reports speed/ETA/progress |
| `DataService` | Export/import the iFlyCompass instance folder with per-file progress |
| `LogAggregatorService` | Central log bus — services call `AddLog(source, level, message)`, UI subscribes to `LogReceived` |
| `DialogService` | ContentDialog wrappers + file/folder pickers |

**Logging flow.** Components publish to `ILogAggregatorService` rather than logging directly; `LogViewModel` (a singleton) and the Log page consume the `LogReceived` event. `ProcessService` raises `LogOutputReceived` for raw Python stdout/stderr.

**Cross-thread UI.** Background services marshal to the UI thread via `DispatcherHelper` (registered singleton). Use it when updating bound state from non-UI threads.

## Runtime layout (gitignored, created at runtime)

- `./iFlyCompass/` — the Python service, downloaded from GitHub
- `./python/` — embedded Python 3.12.10
- `config/settings.json` — persisted `AppSettings`

## Conventions & gotchas

- `Directory.Build.props` sets `ImplicitUsings=disable` and `LangVersion=13.0`, but the csproj **re-enables** `ImplicitUsings`. New `.cs` files can rely on implicit usings.
- `Nullable` is enabled; `TreatWarningsAsErrors` is false.
- One interface + one implementation per service, both in `Services/`. Add new services by registering them in `ConfigureServices()`.
- `AGENTS.md`, `docs/`, `cert/`, and `scripts/` are gitignored — edits there are local-only. (`AGENTS.md` predates the MSIX migration and describes the app as "unpackaged"; that is stale.)
- The `README.md` mentions `H.NotifyIcon.WinUI` tray support — that package is **not** referenced and no tray code exists. Treat it as aspirational, not current.
