param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $repo
try {
    $allowedPaths = @(
        '.github/workflows/netaccel-security.yml',
        '.github/workflows/netaccel-release.yml',
        'THIRD_PARTY_NOTICES.md',
        'NetAccel.Managed.Wpf.Tests/',
        'NetAccel.Updater/',
        'tools/NetAccel.IconExporter/',
        'tools/NetAccel.WpfEvidence/',
        'tools/NetAccel.ReleaseSigner/',
        'v2rayN/ServiceLib.Tests/ManagedUpdateTests.cs',
        'v2rayN/ServiceLib.Tests/NetAccelIdentityCollection.cs',
        'v2rayN/ServiceLib.Tests/NetAccelIdentityTests.cs',
        'v2rayN/ServiceLib/Common/FileUtils.cs',
        'v2rayN/ServiceLib/Common/ManagedConnectionGuard.cs',
        'v2rayN/ServiceLib/Common/ManagedProfileGuard.cs',
        'v2rayN/ServiceLib/Common/NetAccelIdentity.cs',
        'v2rayN/ServiceLib/Common/Utils.cs',
        'v2rayN/ServiceLib/Common/WindowsIdentityHelper.cs',
        'v2rayN/ServiceLib/Handler/AutoStartupHandler.cs',
        'v2rayN/ServiceLib/Handler/ConfigHandler.cs',
        'v2rayN/ServiceLib/Handler/Fmt/FmtHandler.cs',
        'v2rayN/ServiceLib/Handler/Fmt/InnerFmt.cs',
        'v2rayN/ServiceLib/Handler/SysProxy/ProxySettingWindows.cs',
        'v2rayN/ServiceLib/Handler/SysProxy/SysProxyHandler.cs',
        'v2rayN/ServiceLib/Manager/AppManager.cs',
        'v2rayN/ServiceLib/Manager/CoreManager.cs',
        'v2rayN/ServiceLib/Services/UpdateService.cs',
        'v2rayN/ServiceLib/ViewModels/BackupAndRestoreViewModel.cs',
        'v2rayN/ServiceLib/ViewModels/CheckUpdateViewModel.cs',
        'v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs',
        'v2rayN/ServiceLib/ViewModels/ProfilesViewModel.cs',
        'v2rayN/ServiceLib/ViewModels/StatusBarViewModel.cs',
        'v2rayN/v2rayN.slnx',
        'v2rayN/v2rayN/App.xaml',
        'v2rayN/v2rayN/App.xaml.cs',
        'v2rayN/v2rayN/AssemblyInfo.cs',
        'v2rayN/v2rayN/GlobalUsings.cs',
        'v2rayN/v2rayN/Managed/',
        'v2rayN/v2rayN/Resources/NetAccel.ico',
        'v2rayN/v2rayN/Resources/NotifyIcon1.ico',
        'v2rayN/v2rayN/Resources/NotifyIcon2.ico',
        'v2rayN/v2rayN/Resources/NotifyIcon3.ico',
        'v2rayN/v2rayN/Resources/NotifyIcon4.ico',
        'v2rayN/v2rayN/release-trust/',
        'v2rayN/v2rayN/v2rayN.csproj',
        'v2rayN/v2rayN/Views/MainWindow.xaml',
        'v2rayN/v2rayN/Views/MainWindow.xaml.cs',
        'v2rayN/v2rayN/Views/StatusBarView.xaml',
        'v2rayN/v2rayN/Views/StatusBarView.xaml.cs'
    )
    & scripts\plan16\check-change-boundaries.ps1 -AllowedPathPrefixes $allowedPaths
    if ($LASTEXITCODE -ne 0) { throw 'Plan 16 boundary check failed.' }

    $secretPatterns = @(
        'BEGIN (RSA |EC )?PRIVATE KEY',
        'NETACCEL_AGENT_SECRET\s*=',
        'NETACCEL_RELEASE_SIGNING_KEY_PEM\s*='
    )
    $sourceFiles = Get-ChildItem NetAccel.Managed,v2rayN\v2rayN\Managed,scripts\plan16 -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    foreach ($pattern in $secretPatterns) {
        $matches = $sourceFiles | Select-String -Pattern $pattern
        if ($matches) { throw "Secret pattern found: $pattern" }
    }

    if (-not $SkipBuild) {
        & dotnet test NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Managed tests failed.' }
        & dotnet test v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'ServiceLib tests failed.' }
        & dotnet build v2rayN\v2rayN\v2rayN.csproj -c Release --no-restore --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'WPF Release build failed.' }
        & powershell -NoProfile -ExecutionPolicy Bypass -File scripts\plan16\test-updater-rollback.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Updater rollback injection failed.' }
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File scripts\plan16\generate-sbom.ps1
    if ($LASTEXITCODE -ne 0) { throw 'SBOM generation failed.' }
} finally {
    Pop-Location
}
