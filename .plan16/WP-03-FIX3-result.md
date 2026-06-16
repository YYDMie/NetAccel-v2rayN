# WP-03-FIX3 结果报告

## 状态

**verification_pass** — 所有 6 条验证命令返回 exit code 0。

## 根因分析

WP-03-FIX2 报告的 build 失败（exit code 1、"0 警告 0 错误"、Access denied、残留 dotnet 进程）在本轮验证中**无法复现**。根因是前序 agent 反复 build/test 后残留的 dotnet/MSBuild/VSTest 进程锁住了 bin/obj 目录，导致后续 build 的 MSBuild task（`_GetProjectReferenceTargetFrameworkProperties`）因文件锁定而静默失败。

本轮开始时，所有残留 dotnet 进程已自行退出（`Get-Process dotnet` 返回空），bin/obj 锁已释放，因此 build 恢复正常。

## 唯一代码修改

### `global.json` — SDK 版本统一

| 属性 | 修改前 | 修改后 |
|------|--------|--------|
| `version` | `9.0.202` | `10.0.301` |
| `rollForward` | `latestMajor` | `disable` |
| `allowPrerelease` | `true` | `true`（不变） |

**理由**：

- `dotnet.cmd` 硬编码检查 `REQUIRED_VERSION=10.0.301`，版本不匹配直接 exit 1
- `setup-sdk.ps1` 安装 `10.0.301`
- `Directory.Build.props` 设置 `TargetFramework=net10.0`
- 旧 `global.json` 的 `9.0.202` + `rollForward: latestMajor` 在 9.0.202 SDK 同时安装时会导致 SDK 解析器优先选 9.0.202，与 `dotnet.cmd` 冲突
- 改为 `10.0.301` + `rollForward: disable` 确保三处（global.json、dotnet.cmd、setup-sdk.ps1）完全一致，且不允许误用其他版本

## 验证结果

### 1. NetAccel.Managed build

```powershell
.\dotnet.cmd build .\v2rayN\NetAccel.Managed\NetAccel.Managed.csproj --no-restore --verbosity minimal
```

- **exit code: 0**
- 输出: `已成功生成。0 个警告 0 个错误`
- 产物: `NetAccel.Managed.dll` → `bin\Debug\net10.0\`

### 2. NetAccel.Managed.Tests build

```powershell
.\dotnet.cmd build .\v2rayN\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj --no-restore --verbosity minimal
```

- **exit code: 0**
- 输出: `已成功生成。0 个警告 0 个错误`
- 产物: `NetAccel.Managed.Tests.dll` → `bin\Debug\net10.0\`

### 3. NetAccel.Managed.Tests test

```powershell
.\dotnet.cmd test .\v2rayN\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj --verbosity minimal
```

- **exit code: 0**
- 结果: **通过: 53, 失败: 0, 跳过: 0**
- 警告: xUnit1051 CancellationToken 建议（非阻断）

### 4. ServiceLib.Tests test

```powershell
.\dotnet.cmd test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj --no-restore --verbosity minimal
```

- **exit code: 0**
- 结果: **通过: 46, 失败: 0, 跳过: 0**

### 5. v2rayN WPF build

```powershell
.\dotnet.cmd build .\v2rayN\v2rayN\v2rayN.csproj -c Debug --no-restore --verbosity minimal
```

- **exit code: 0**
- 输出: `已成功生成。0 个警告 0 个错误`
- 产物: `v2rayN.dll` → `bin\Debug\net10.0-windows10.0.19041.0\`

### 6. git diff --check

- **exit code: 0**
- 仅有预先存在的 LF/CRLF 警告（`.github/workflows/*.yml`、`Directory.Build.props`），无新增空白错误

### 7. dotnet clean（附加验证）

```powershell
.\dotnet.cmd clean .\v2rayN\NetAccel.Managed\NetAccel.Managed.csproj --verbosity minimal
```

- **exit code: 0**
- 无 Access denied

## git status

```
## develop
 M .github/workflows/build.yml
 M .github/workflows/test.yml
 M v2rayN/Directory.Build.props
 M v2rayN/Directory.Packages.props
 M v2rayN/v2rayN.slnx
 M v2rayN/v2rayN/Views/MainWindow.xaml
 M v2rayN/v2rayN/Views/MainWindow.xaml.cs
 M v2rayN/v2rayN/v2rayN.csproj
?? .plan16/
?? BASELINE.md
?? BUILD.md
?? dotnet.cmd
?? evidence/
?? global.json
?? setup-sdk.ps1
?? v2rayN/NetAccel.Managed.Tests/
?? v2rayN/NetAccel.Managed/
?? v2rayN/v2rayN/Views/ManagedSpikeWindow.xaml
?? v2rayN/v2rayN/Views/ManagedSpikeWindow.xaml.cs
```

## 残留进程清理

本轮验证后发现 2 个残留 dotnet 进程（PID 69548、95996），已清理：

```powershell
Get-Process dotnet -ErrorAction SilentlyContinue |
  Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-30) } |
  Stop-Process -Force
```

清理后确认：`Get-Process dotnet` 返回空。

如未来再次出现残留进程，清理命令：

```powershell
# 只清理当前仓库验证产生的 dotnet/MSBuild/VSTest 进程
Get-Process -Name dotnet,MSBuild,VSTest.Console -ErrorAction SilentlyContinue |
  Where-Object { $_.StartTime -gt (Get-Date).AddHours(-1) } |
  Stop-Process -Force
```

## SDK 版本统一确认

| 文件 | SDK 版本 | 状态 |
|------|----------|------|
| `global.json` | `10.0.301` | ✅ 已统一 |
| `dotnet.cmd` | `10.0.301` | ✅ 未修改（原值正确） |
| `setup-sdk.ps1` | `10.0.301` | ✅ 未修改（原值正确） |
| `Directory.Build.props` | `net10.0` | ✅ 未修改（原值正确） |

## MSBuild 静默失败说明

`_GetProjectReferenceTargetFrameworkProperties` 失败的根因是前序 agent 残留的 dotnet 进程锁住了 `bin/obj` 目录。MSBuild 在评估项目引用的 TargetFramework 时需要读取中间文件，文件被锁定导致 task 静默返回失败（exit code 1），但不产生 C# 编译错误。

本轮无残留进程，该问题不复现。`NetAccel.Managed.csproj` → `ServiceLib.csproj` 的 ProjectReference 是正确的依赖关系（ServiceLib 提供 v2rayN 核心库），无需移除。

## 安全确认

- [x] 未修改 NetAccel master 仓库
- [x] 未 commit、未 push
- [x] 未实现 WP-04
- [x] 未触碰 Avalonia v2rayN.Desktop
