# NetAccel-v2rayN Version Baseline

> Generated: 2026-06-12
> Purpose: Record exact versions for reproducible builds and upstream sync

## Git Remotes

| Remote | URL | Purpose |
|--------|-----|---------|
| origin | https://github.com/YYDMie/NetAccel-v2rayN.git | NetAccel fork |
| upstream | https://github.com/2dust/v2rayN.git | Original v2rayN |

## Fork Verification

- Fork: `YYDMie/NetAccel-v2rayN`
- Parent: `2dust/v2rayN`
- `isFork: true`

## Commits

| Label | Hash | Message |
|-------|------|---------|
| Tag 7.22.6 | `f0ee79277853e886830ddd00ecf7b1f28f1d2035` | up 7.22.6 |
| Freeze (7.22.6+1) | `1869a95700e17369f071ed23c8c485c2c3e83a1d` | fix: Desktop(Avalonia) 移除子对话框最小化按钮，仅禁 CanMinimize (#9526) |

### All 7 Commits Between Tag and Freeze

```
1869a957 fix: Desktop(Avalonia) 移除子对话框最小化按钮，仅禁 CanMinimize (#9526)
34359885 Update AppBuilderExtension.cs (#9514)
93832656 fix: disable .net9 CET (#9507)
b1a400b3 binConfigs only deletes test files periodically
879c7369 Update Utils.cs
8147b397 fix: 在窗口初始化前设置保存的尺寸，消除启动时偏右下偏移 (#9498)
5fed566a Add ReplaceLineBreaks extension and Fix bug
```

## .NET SDK

| Component | Version |
|-----------|---------|
| SDK (pinned in global.json) | 10.0.301 |
| Target framework | net10.0 |
| WPF target | net10.0-windows10.0.19041.0 |
| Supported OS version | 7.0 |
| global.json rollForward | latestPatch |

## Submodules

| Name | Path | Commit | URL |
|------|------|--------|-----|
| GlobalHotKeys | v2rayN/GlobalHotKeys | `569a95bb0fd2280d8d5581250aae54ecc2122d10` | https://github.com/2dust/GlobalHotKeys |

## Projects in Solution

| Project | Type | Target |
|---------|------|--------|
| v2rayN | WPF WinExe | net10.0-windows10.0.19041.0 |
| v2rayN.Desktop | Avalonia | net10.0 |
| ServiceLib | Library | net10.0 |
| ServiceLib.Tests | xUnit | net10.0 |
| ServiceLib.UdpTest | Library | net10.0 |
| AmazTool | Console | net10.0 |
| GlobalHotKeys | Library | net10.0 |

## Core Versions

**Status: NOT FIXED in source code.**

Cores are downloaded at runtime. The version resolution logic is in
`v2rayN/ServiceLib/Manager/CoreInfoManager.cs`. For each core type, the
application queries the GitHub Releases API for the latest release tag.

| Core | Repository | Version Rule |
|------|------------|-------------|
| Xray | XTLS/Xray-core | Latest GitHub release |
| sing-box | SagerNet/sing-box | Latest GitHub release |
| mihomo | MetaCubeX/mihomo | Latest GitHub release (classic) |
| v2fly | v2fly/v2ray-core | Latest GitHub release (classic) |
| hysteria | apernet/hysteria | Latest GitHub release (classic) |
| tuic | EAimTY/tuic | Latest GitHub release (classic) |
| naive | klzgrad/naiveproxy | Latest GitHub release (classic) |

**No pinned versions.** The source code does not contain hardcoded core
version numbers. Different runs will download whatever is latest.

**Required for WP-01+:**
1. Create a `cores.manifest.json` with pinned versions and SHA-256 hashes
2. Vendor or cache core binaries for reproducible builds
3. Add signature verification for downloaded cores

## Test Results (2026-06-12)

| Test | Result |
|------|--------|
| dotnet restore | ✅ All 7 projects restored |
| ServiceLib.Tests | ✅ 46 passed, 0 failed, 0 skipped |
| WPF Debug build | ✅ 0 warnings, 0 errors |
| WPF Release build | ✅ 0 warnings, 0 errors |
| WPF publish win-x64 | ✅ Self-contained ~211MB |
| Single-instance | ✅ Verified (see smoke evidence) |

## Environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.26100 |
| .NET SDK (system) | 9.0.202 [C:\Program Files\dotnet\sdk] |
| .NET SDK (user) | 10.0.301 [C:\Users\huangtingyang\.dotnet\sdk] |
| Repository launcher | `dotnet.cmd` invokes the pinned user-local SDK without relying on PATH or PowerShell execution policy |
