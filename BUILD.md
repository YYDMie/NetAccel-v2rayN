# NetAccel-v2rayN Build Guide

> Fork baseline: v2rayN 7.22.6+1 (7 commits after tag 7.22.6)
> Last verified: 2026-06-12

## Prerequisites

| Component | Version | Notes |
|-----------|---------|-------|
| .NET SDK | 10.0.301 | Pinned in `global.json`. See SDK Setup below. |
| Windows SDK | 10.0.19041.0 | Included with .NET 10 Windows targeting pack |
| Git | 2.x | For submodule initialization |
| OS | Windows 10 1607+ / Windows 11 | WPF requires Windows |

## SDK Setup

The project requires .NET 10.0.301 SDK. A `global.json` pins this version.
Windows can have an older machine-wide `dotnet.exe` ahead of user tools on
`PATH`, so repository commands use `dotnet.cmd` instead of relying on PATH order
or the PowerShell execution policy.

Install the pinned SDK once:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup-sdk.ps1
```

The script installs exactly 10.0.301 to `$env:USERPROFILE\.dotnet`. It does not
modify the user's permanent environment. `dotnet.cmd` always invokes that exact
installation and installs it automatically when missing.

Verify from any new PowerShell window:

```powershell
cd <repo-root>
.\dotnet.cmd --version
# Expected: 10.0.301
```

## Quick Start

```powershell
# 1. Clone
git clone --branch develop --recurse-submodules https://github.com/YYDMie/NetAccel-v2rayN.git
cd NetAccel-v2rayN

# 2. Restore packages (installs the pinned SDK automatically when needed)
.\dotnet.cmd restore v2rayN/v2rayN.sln

# 3. Run tests
.\dotnet.cmd test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj

# 4. Build Debug
.\dotnet.cmd build v2rayN/v2rayN/v2rayN.csproj -c Debug

# 5. Build Release
.\dotnet.cmd build v2rayN/v2rayN/v2rayN.csproj -c Release

# 6. Publish self-contained for Windows x64
.\dotnet.cmd publish v2rayN/v2rayN/v2rayN.csproj -c Release -r win-x64 --self-contained true
```

## Output Locations

| Build | Path |
|-------|------|
| Debug | `v2rayN/v2rayN/bin/Debug/net10.0-windows10.0.19041.0/` |
| Release | `v2rayN/v2rayN/bin/Release/net10.0-windows10.0.19041.0/` |
| Publish (win-x64) | `v2rayN/v2rayN/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/` |

## Submodules

| Submodule | Path | URL | Commit |
|-----------|------|-----|--------|
| GlobalHotKeys | `v2rayN/GlobalHotKeys` | https://github.com/2dust/GlobalHotKeys | `569a95bb0fd2280d8d5581250aae54ecc2122d10` |

## Version Baseline

| Field | Value |
|-------|-------|
| Upstream | 2dust/v2rayN |
| Tag | `7.22.6` |
| Tag commit | `f0ee79277853e886830ddd00ecf7b1f28f1d2035` |
| Freeze commit | `1869a95700e17369f071ed23c8c485c2c3e83a1d` |
| Baseline label | 7.22.6+1 |
| Commits between tag and freeze | 7 |
| .NET SDK | 10.0.301 |
| Target framework | net10.0-windows10.0.19041.0 |

### Commits Between Tag (7.22.6) and Freeze (7.22.6+1)

```
1869a957 fix: Desktop(Avalonia) 移除子对话框最小化按钮，仅禁 CanMinimize (#9526)
34359885 Update AppBuilderExtension.cs (#9514)
93832656 fix: disable .net9 CET (#9507)
b1a400b3 binConfigs only deletes test files periodically
879c7369 Update Utils.cs
8147b397 fix: 在窗口初始化前设置保存的尺寸，消除启动时偏右下偏移 (#9498)
5fed566a Add ReplaceLineBreaks extension and Fix bug
```

## Core Versions

**Status: NOT FIXED.** Xray and sing-box cores are downloaded at runtime from
GitHub releases. The source code does not pin specific core versions.

| Core | Download Source | Version Resolution |
|------|----------------|-------------------|
| Xray | https://github.com/XTLS/Xray-core/releases | Latest release tag via GitHub API |
| sing-box | https://github.com/SagerNet/sing-box/releases | Latest release tag via GitHub API |
| mihomo | https://github.com/MetaCubeX/mihomo/releases | Classic mode only |
| v2fly | https://github.com/v2fly/v2ray-core/releases | Classic mode only |

**Blocking impact:** Without pinned core versions, builds are not fully
reproducible. Different build times may download different core versions.
This must be addressed in WP-01 or a dedicated supply chain task.

**Required actions for WP-01+:**
1. Pin Xray and sing-box versions in a manifest file
2. Add SHA-256 verification for downloaded cores
3. Create a core download cache or vendor the binaries
4. Document the exact core versions used for each release

## License

This project is licensed under GPL-3.0. See [LICENSE](LICENSE) for details.

## Upstream Sync

```powershell
# Fetch latest from upstream
git fetch upstream

# View available tags
git tag -l "7.*"

# Merge a specific tag
git merge 7.xx.x

# Or rebase develop onto latest upstream
git rebase upstream/master
```

## Troubleshooting

### .NET SDK not found
Run `powershell -ExecutionPolicy Bypass -File .\setup-sdk.ps1`, then use
`.\dotnet.cmd` for build commands.

### Submodule not initialized
```powershell
git submodule update --init --recursive
```

### Build errors after upstream sync
```powershell
.\dotnet.cmd clean v2rayN/v2rayN.sln
.\dotnet.cmd restore v2rayN/v2rayN.sln
.\dotnet.cmd build v2rayN/v2rayN.sln
```
