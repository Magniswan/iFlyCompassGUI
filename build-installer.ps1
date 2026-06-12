<#
.SYNOPSIS
    构建 iFlyCompassGUI 安装程序（自包含 MSIX + CER 的 Bootstrapper EXE）。

.DESCRIPTION
    1. 构建主项目，生成 MSIX 包
    2. 从 PFX 导出 CER 公钥证书
    3. 解析 .NET 10 Desktop Runtime 下载地址并写入源码
    4. 将 MSIX 和 CER 复制到 Bootstrapper 的 Resources 目录
    5. 构建 Bootstrapper 项目
    6. 输出最终的 iFlyCompassGUI-Setup.exe

.PARAMETER Configuration
    构建配置，默认 Release。

.PARAMETER PfxPath
    PFX 证书文件路径。默认 cert\iFlyCompassGUI.pfx。

.PARAMETER PfxPassword
    PFX 证书密码。若未提供，尝试从 cert\cert-password.props 读取。

.PARAMETER DotNetRuntimeUrl
    .NET 10 Desktop Runtime 下载地址。若未提供，自动从 dotnet release API 获取。

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -DotNetRuntimeUrl "https://download.visualstudio.microsoft.com/.../windowsdesktop-runtime-10.0.0-win-x64.exe"
#>

param(
    [string]$Configuration = "Release",
    [string]$PfxPath = "cert\iFlyCompassGUI.pfx",
    [string]$PfxPassword,
    [string]$DotNetRuntimeUrl
)

$ErrorActionPreference = "Stop"
$RootDir = $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  iFlyCompassGUI 安装程序构建脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# ── 步骤 1: 构建主项目（生成 MSIX） ──────────────────────────────
Write-Host "`n[1/6] 构建主项目..." -ForegroundColor Yellow

dotnet build "$RootDir\iFlyCompassGUI.csproj" -c $Configuration -r win-x64
if ($LASTEXITCODE -ne 0) {
    Write-Error "主项目构建失败"
    exit 1
}

# 查找生成的 MSIX 文件
$msixFile = Get-ChildItem -Path "$RootDir\bin\$Configuration" -Filter "*.msix" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msixFile) {
    Write-Error "未找到 MSIX 文件。请确认项目已启用 MSIX 打包且构建成功。"
    exit 1
}

Write-Host "  找到 MSIX: $($msixFile.FullName)" -ForegroundColor Green

# ── 步骤 2: 导出 CER 公钥证书 ────────────────────────────────────
Write-Host "`n[2/6] 导出证书..." -ForegroundColor Yellow

if (-not $PfxPassword) {
    # 尝试从 cert-password.props 读取密码
    $propsFile = "$RootDir\cert\cert-password.props"
    if (Test-Path $propsFile) {
        $propsContent = Get-Content $propsFile -Raw
        $match = [regex]::Match($propsContent, '<PackageCertificatePassword>([^<]+)</PackageCertificatePassword>')
        if ($match.Success) {
            $PfxPassword = $match.Groups[1].Value
        }
    }
}

if (-not (Test-Path $PfxPath)) {
    Write-Error "PFX 文件不存在: $PfxPath"
    exit 1
}

$resourcesDir = "$RootDir\installer\Bootstrapper\Resources"
New-Item -Path $resourcesDir -ItemType Directory -Force | Out-Null

$cerPath = "$resourcesDir\iFlyCompassGUI.cer"
try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword)
    [IO.File]::WriteAllBytes($cerPath, $cert.Export("Cert"))
    Write-Host "  证书已导出: $cerPath" -ForegroundColor Green
}
catch {
    Write-Error "证书导出失败: $_"
    exit 1
}

# ── 步骤 3: 复制 MSIX 到 Resources ──────────────────────────────
Write-Host "`n[3/6] 复制 MSIX 到 Resources..." -ForegroundColor Yellow

$destMsix = "$resourcesDir\iFlyCompassGUI.msix"
Copy-Item $msixFile.FullName $destMsix -Force
Write-Host "  MSIX 已复制: $destMsix" -ForegroundColor Green

# ── 步骤 4: 解析并写入 .NET Runtime URL ──────────────────────────
Write-Host "`n[4/6] 解析 .NET 10 Desktop Runtime 下载地址..." -ForegroundColor Yellow

if (-not $DotNetRuntimeUrl) {
    try {
        $releasesJson = Invoke-RestMethod "https://dotnetcli.azureedge.net/dotnet/release-metadata/10.0/releases.json"
        $latestRelease = $releasesJson.releases | Select-Object -First 1
        $runtimeAsset = $latestRelease."windowsdesktop".files |
            Where-Object { $_.rid -eq "win-x64" -and $_.name -like "windowsdesktop-runtime-*" } |
            Select-Object -First 1

        if ($runtimeAsset) {
            $DotNetRuntimeUrl = $runtimeAsset.url
            Write-Host "  自动获取到 URL: $DotNetRuntimeUrl" -ForegroundColor Green
        }
        else {
            # 回退：尝试从 files 列表中匹配
            $allFiles = $latestRelease."windowsdesktop".files
            $runtimeAsset = $allFiles |
                Where-Object { $_.name -like "*windowsdesktop-runtime*win-x64*" } |
                Select-Object -First 1
            if ($runtimeAsset) {
                $DotNetRuntimeUrl = $runtimeAsset.url
                Write-Host "  自动获取到 URL (回退): $DotNetRuntimeUrl" -ForegroundColor Green
            }
        }
    }
    catch {
        Write-Warning "无法自动获取 .NET Runtime URL: $_"
    }
}

if ($DotNetRuntimeUrl -and $DotNetRuntimeUrl -ne "PLACEHOLDER_DOTNET_RUNTIME_URL") {
    $urlsFile = "$RootDir\installer\Bootstrapper\RuntimeUrls.cs"
    $content = Get-Content $urlsFile -Raw
    $content = $content -replace 'PLACEHOLDER_DOTNET_RUNTIME_URL', $DotNetRuntimeUrl
    Set-Content $urlsFile $content -NoNewline
    Write-Host "  已写入 .NET Runtime URL" -ForegroundColor Green
}
else {
    Write-Warning "未获取到 .NET Runtime URL，安装程序将尝试在运行时自动解析"
}

# ── 步骤 5: 构建 Bootstrapper ────────────────────────────────────
Write-Host "`n[5/6] 构建 Bootstrapper..." -ForegroundColor Yellow

$bootstrapperProj = "$RootDir\installer\Bootstrapper\Bootstrapper.csproj"

# 使用 msbuild 构建 .NET Framework 项目
$msbuild = Get-Command "msbuild" -ErrorAction SilentlyContinue
if (-not $msbuild) {
    # 尝试从 VS 安装路径查找
    $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vsWhere) {
        $vsInstallPath = & $vsWhere -latest -property installationPath 2>$null
        if ($vsInstallPath) {
            $msbuildPath = Join-Path $vsInstallPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $msbuildPath) {
                $msbuild = Get-Command $msbuildPath
            }
        }
    }
}

if ($msbuild) {
    & $msbuild.Source $bootstrapperProj -p:Configuration=$Configuration -verbosity:minimal
}
else {
    # 回退到 dotnet build（SDK-style 项目可能支持）
    dotnet build $bootstrapperProj -c $Configuration
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Bootstrapper 构建失败"
    exit 1
}

# ── 步骤 6: 收集输出 ─────────────────────────────────────────────
Write-Host "`n[6/6] 收集输出..." -ForegroundColor Yellow

$artifactsDir = "$RootDir\artifacts"
New-Item -Path $artifactsDir -ItemType Directory -Force | Out-Null

$setupExe = Get-ChildItem -Path "$RootDir\installer\Bootstrapper\bin\$Configuration" -Filter "iFlyCompassGUI-Setup.exe" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $setupExe) {
    Write-Error "未找到 iFlyCompassGUI-Setup.exe"
    exit 1
}

$destSetup = "$artifactsDir\iFlyCompassGUI-Setup.exe"
Copy-Item $setupExe.FullName $destSetup -Force

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  构建完成！" -ForegroundColor Green
Write-Host "  输出: $destSetup" -ForegroundColor Green
Write-Host "  大小: $([math]::Round((Get-Item $destSetup).Length / 1MB, 1)) MB" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

# 清理 Resources 目录中的构建时文件
Remove-Item $cerPath -Force -ErrorAction SilentlyContinue
Remove-Item $destMsix -Force -ErrorAction SilentlyContinue
