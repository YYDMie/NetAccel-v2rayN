# NetAccel-v2rayN 构建基线

> WP-00 (T16-R0-02/03) 产出
> 最后更新：2026-06-12（WP-00 收口返工）

## 一、仓库身份

| 项目 | 值 |
|------|-----|
| origin | `https://github.com/YYDMie/v2rayN.git` |
| upstream | `https://github.com/2dust/v2rayN.git` |
| 当前分支 | `codex/plan16` |
| GPL 许可证 | GPL-3.0（完整文本见仓库根目录 `LICENSE`） |

## 二、版本基线

| 字段 | 值 |
|------|-----|
| 上游标签 | `7.22.6` |
| 标签 commit | `f0ee79277853e886830ddd00ecf7b1f28f1d2035` |
| 冻结 commit | `1869a95700e17369f071ed23c8c485c2c3e83a1d` |
| 基线标记 | `7.22.6+1`（比 `7.22.6` 标签多 7 个提交，含 Avalonia 修复） |
| `Directory.Build.props` Version | `7.22.6` |

### 冻结 commit 相对于 7.22.6 标签的额外提交

```
1869a957 fix: Desktop(Avalonia) 移除子对话框最小化按钮，仅禁 CanMinimize (#9526)
34359885 Update AppBuilderExtension.cs (#9514)
93832656 fix: disable .net9 CET (#9507)
b1a400b3 binConfigs only deletes test files periodically
879c7369 Update Utils.cs
8147b397 fix: 在窗口初始化前设置保存的尺寸，消除启动时偏右下偏移 (#9498)
5fed566a Add ReplaceLineBreaks extension and Fix bug
```

## 三、工具链要求

| 工具 | 版本 | 说明 |
|------|------|------|
| .NET SDK | `10.0.301` | 通过 `global.json` 锁定，`rollForward: latestPatch` |
| TargetFramework | `net10.0` (通用) / `net10.0-windows10.0.19041.0` (WPF) | |
| Windows SDK | `10.0.19041.0` | WPF 项目最低 Windows 版本 |
| SupportedOSPlatformVersion | `7.0` | |
| 操作系统 | Windows 11 x64 | 主基线 |
| 架构 | `win-x64` | |

### SDK 安装

便携 SDK 位置：`C:\tmp\dotnet10\dotnet.exe`

> **注意：** 便携目录 `C:\tmp\dotnet10` 仅为当前机器的复现路径。clean checkout 只需安装与
> `global.json` 匹配的 .NET SDK `10.0.3xx`，`dotnet` 在 PATH 中即可。

或通过 `global.json` 自动解析已安装的 10.0.3xx SDK。

## 四、子模块

| 子模块 | 路径 | 上游 | 当前 commit |
|--------|------|------|-------------|
| GlobalHotKeys | `v2rayN/GlobalHotKeys` | `https://github.com/2dust/GlobalHotKeys` | `569a95bb0fd2280d8d5581250aae54ecc2122d10` |

初始化命令：

```powershell
git submodule init
git submodule update
```

## 五、核心版本

Xray 和 sing-box 版本为运行时动态获取（通过 GitHub Releases 下载），不静态定义于源码中。
核心下载源定义于 `ServiceLib/Global.cs` 的 `CoreUrls` 字典。

> **说明：** 首次运行时的核心下载不属于 WP-00 构建基线范围。

## 六、可重复构建命令

以下命令均在仓库根目录执行。如使用便携 SDK，需先设置环境变量：

```powershell
# 设置 SDK 路径（如使用便携 SDK）
$env:DOTNET_ROOT = 'C:\tmp\dotnet10'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

# 1. 初始化子模块
git submodule init; git submodule update

# 2. 还原依赖
cd v2rayN
dotnet restore v2rayN.sln

# 3. 运行测试
dotnet test ServiceLib.Tests/ServiceLib.Tests.csproj --no-restore

# 4. 发布 WPF Debug x64（self-contained，含 .NET 运行时）
dotnet publish v2rayN/v2rayN.csproj -c Debug -r win-x64 -p:SelfContained=true

# 5. 发布 WPF Release x64（self-contained 单文件，含 .NET 运行时）
dotnet publish v2rayN/v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true
```

### 发布产物路径

| 配置 | 路径 | 特点 |
|------|------|------|
| Debug | `v2rayN/v2rayN/bin/Debug/net10.0-windows10.0.19041.0/win-x64/publish/` | self-contained，运行时为独立 DLL |
| Release | `v2rayN/v2rayN/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/` | self-contained 单文件，~211MB（运行时嵌入 exe） |

主可执行文件：`v2rayN.exe`

> **必须使用 `-p:SelfContained=true` 发布。** 使用 `--self-contained false` 发布的 exe 在未安装
> .NET Desktop Runtime 的机器上会弹出 "You must install .NET Desktop Runtime to run this application"
> 错误对话框，无法启动。

## 七、构建验证记录 (2026-06-12，含返工)

### 7.1 基础构建

| 步骤 | 结果 |
|------|------|
| `dotnet restore v2rayN.sln` | ✅ 7 个项目全部还原或为最新 |
| `dotnet test ServiceLib.Tests --no-restore` | ✅ 46/46 通过，0 失败，0 跳过 |
| `dotnet publish Debug -r win-x64 -p:SelfContained=true` | ✅ 输出 `v2rayN.exe`（apphost 224,768 字节），运行时 DLL 在同目录 |
| `dotnet publish Release -r win-x64 -p:SelfContained=true` | ✅ 输出 `v2rayN.exe` 单文件（211,674,864 字节） |

### 7.2 Release 原生启动冒烟（Start-Process -UseNewEnvironment）

在隔离环境中启动 Release exe，确保不使用 `C:\tmp\dotnet10`：

| 检查项 | 结果 |
|--------|------|
| 首次启动 PID | 33368 |
| 8 秒后进程状态 | FirstExited=False（进程仍在运行） |
| 同名进程计数 | CountAfterFirst=1（仅 1 个实例） |
| 主窗口标题 | `v2rayN - V7.22.6 - X64 - 以管理员身份运行` |
| .NET Runtime 错误框 | 未出现 |

### 7.3 单实例互斥验证

| 检查项 | 结果 |
|--------|------|
| 第二次启动 PID | 29668 |
| 进程退出状态 | SecondExited=True（第二个实例自动退出） |
| 同名进程计数 | CountAfterSecond=1（仍仅 1 个实例） |

> 第二个实例检测到已有实例后自动退出，单实例互斥机制验证通过。

### 7.4 进程清理

| 检查项 | 结果 |
|--------|------|
| 清理方式 | `Stop-Process` 强制终止 |
| 清理后残留进程 | Residual=0 |

> **注意：** 此处为 `Stop-Process` 强制清理，非应用通过托盘菜单正常退出。托盘正常退出的行为
> 尚未自动验证，见第八节。

### 7.5 仓库状态

| 检查项 | 结果 |
|--------|------|
| `git submodule status` | `569a95bb0fd2280d8d5581250aae54ecc2122d10 v2rayN/GlobalHotKeys` |
| `git diff --check` | 无输出（无 whitespace 错误） |
| `git status --short` | `?? .plan16/` `?? docs/` `?? global.json`（均为本次新增的未跟踪文件） |

## 八、已知限制与待人工验证

### 待人工验证项

| 项目 | 原因 | 人工验证步骤 |
|------|------|-------------|
| 托盘图标真实渲染 | CLI 环境无法验证 GUI 渲染 | 启动后检查通知区域是否出现 v2rayN 图标 |
| 托盘菜单正常退出 | CLI 自动化仅验证了 `Stop-Process` 强制清理 | 右键托盘 → 退出，确认进程全部终止 |
| 系统代理恢复 | 依赖托盘正常退出流程 | 退出后检查系统代理设置是否恢复为退出前状态 |

### 已知限制

- 核心二进制（Xray、sing-box）需运行时下载，首次构建不含核心，首次运行下载不属于 WP-00 基线范围。
- 便携 SDK 目录 `C:\tmp\dotnet10` 仅为当前机器复现路径；clean checkout 只需安装与 `global.json` 匹配的 .NET SDK `10.0.3xx`。
- `global.json` 锁定 `10.0.301`，`rollForward: latestPatch` 允许补丁版本升级。

> **返工说明 (2026-06-12)：** 初版使用 `--self-contained false`，导致发布产物在未安装 .NET
> Runtime 的机器上无法启动（Windows 弹出 "You must install .NET Desktop Runtime" 对话框）。
> 已修正为 `-p:SelfContained=true`，与 `.github/workflows/build.yml` 一致。
