# 计划 16 - 许可证与第三方致谢

> 状态：基线（provisional）
> 创建日期：2026-06-12
> 上游版本：v2rayN `7.22.6+1`

## 一、主许可证

本仓库（`NetAccel-v2rayN`）继承上游 [2dust/v2rayN](https://github.com/2dust/v2rayN) 的
**GNU General Public License v3.0 (GPL-3.0)**。

- 许可证完整文本：仓库根目录 [`LICENSE`](../LICENSE)
- 上游版权：2dust/v2rayN contributors
- Fork 来源：`https://github.com/2dust/v2rayN.git`
- Fork commit：`1869a95700e17369f071ed23c8c485c2c3e83a1d`（基线 `7.22.6+1`）

**GPL-3.0 要求：**
- 修改后的派生作品必须同样以 GPL-3.0 发布
- 分发二进制时必须同时提供源代码或获取源代码的明确途径
- 必须保留原始版权声明和许可证文本

## 二、上游来源致谢

| 项目 | 来源 | 许可证 | 说明 |
|------|------|--------|------|
| v2rayN WPF 客户端 | [2dust/v2rayN](https://github.com/2dust/v2rayN) | GPL-3.0 | 主代码库 fork |
| GlobalHotKeys 子模块 | [2dust/GlobalHotKeys](https://github.com/2dust/GlobalHotKeys) | 见下方说明 | 全局热键功能 |

### 上游 commit 信息

| 字段 | 值 |
|------|-----|
| 标签 | `7.22.6` |
| 标签 commit | `f0ee79277853e886830ddd00ecf7b1f28f1d2035` |
| 冻结 commit | `1869a95700e17369f071ed23c8c485c2c3e83a1d` |
| 基线标记 | `7.22.6+1` |
| 子模块 GlobalHotKeys | `569a95bb0fd2280d8d5581250aae54ecc2122d10` |

### GlobalHotKeys 子模块许可证说明

- **仓库内 LICENSE 文件**（`v2rayN/GlobalHotKeys/LICENSE`）：WTFPL v2
  (DO WHAT THE FUCK YOU WANT TO PUBLIC LICENSE, Version 2)
- **上游 README**：声明 WTFPL v2 **OR** MIT
- **差异**：分发树中实际包含的 LICENSE 文件为 WTFPL v2，但上游 README 允许 WTFPL v2 或 MIT 双选。
- **发布前要求**：必须确认采用分发树中实际包含的 LICENSE 文件（WTFPL v2），
  或在分发 NOTICE 中明确声明采用 MIT。不得假设两者之一而未记录选择依据。

## 三、核心组件

以下为核心运行时组件，通过 GitHub Releases 运行时下载：

| 核心 | 上游 | 许可证 | 用途 |
|------|------|--------|------|
| Xray | [XTLS/Xray-core](https://github.com/XTLS/Xray-core) | MPL-2.0 (已确认) | VLESS/VMess/XTLS 代理核心 |
| sing-box | [SagerNet/sing-box](https://github.com/SagerNet/sing-box) | GNU GPL v3，具体 SPDX 变体按所固定 release 源码核实 | Hysteria2/多协议代理核心 |

核心版本在运行时动态获取，下载源定义于 `ServiceLib/Global.cs` 的 `CoreUrls` 字典。

**Xray 许可证说明：**
- GitHub API 确认 SPDX: `MPL-2.0`
- 仓库根目录 LICENSE 文件为 MPL-2.0 全文

**sing-box 许可证说明：**
- GitHub API 报告 SPDX: `NOASSERTION`
- 仓库内 LICENSE 文件为 GPL v3 变体
- 在核实具体固定 release 对应的源码 LICENSE 前，不断言具体 SPDX 标识符（如 GPL-3.0-only 或 GPL-3.0-or-later）
- 发布前必须核实所固定 release 版本的源码中 LICENSE 文件内容

## 四、NuGet 第三方依赖

以下为主要 NuGet 包依赖（完整列表见 `Directory.Packages.props`）。
**所有许可证列均为 provisional，待 R4B 阶段通过 SBOM/NuGet 元数据扫描验证。**

| 包名 | 版本 | 许可证（provisional） | 用途 |
|------|------|----------------------|------|
| ReactiveUI | 23.2.28 | 待 R4B SBOM/NuGet 元数据验证 | MVVM 框架 |
| ReactiveUI.WPF | 23.2.28 | 待 R4B SBOM/NuGet 元数据验证 | WPF ReactiveUI 集成 |
| ReactiveUI.Fody | 19.5.41 | 待 R4B SBOM/NuGet 元数据验证 | 属性变更注入 |
| MaterialDesignThemes | 5.3.2 | 待 R4B SBOM/NuGet 元数据验证 | WPF Material Design 主题 |
| H.NotifyIcon.Wpf | 2.4.1 | 待 R4B SBOM/NuGet 元数据验证 | 系统托盘图标 |
| NLog | 6.1.3 | 待 R4B SBOM/NuGet 元数据验证 | 日志框架 |
| sqlite-net-e | 1.11.0 | 待 R4B SBOM/NuGet 元数据验证 | SQLite 数据库访问 |
| Repobot.SQLite.Unofficial | 3.53.2 | 待 R4B SBOM/NuGet 元数据验证 | SQLite 原生库 |
| CliWrap | 3.10.1 | 待 R4B SBOM/NuGet 元数据验证 | 进程命令行封装 |
| Downloader | 5.7.0 | 待 R4B SBOM/NuGet 元数据验证 | 文件下载 |
| QRCoder | 1.8.0 | 待 R4B SBOM/NuGet 元数据验证 | 二维码生成 |
| YamlDotNet | 18.0.0 | 待 R4B SBOM/NuGet 元数据验证 | YAML 解析 |
| ZXing.Net.Bindings.SkiaSharp | 0.16.22 | 待 R4B SBOM/NuGet 元数据验证 | 二维码扫描 |
| IPNetwork2 | 4.3.0 | 待 R4B SBOM/NuGet 元数据验证 | IP 网络计算 |
| WebDav.Client | 2.9.0 | 待 R4B SBOM/NuGet 元数据验证 | WebDAV 备份 |
| TaskScheduler | 2.12.2 | 待 R4B SBOM/NuGet 元数据验证 | Windows 任务计划 |
| AwesomeAssertions | 9.4.0 | 待 R4B SBOM/NuGet 元数据验证 | 测试断言 |
| xunit.v3 | 3.2.2 | 待 R4B SBOM/NuGet 元数据验证 | 测试框架 |
| Microsoft.NET.Test.Sdk | 18.6.0 | 待 R4B SBOM/NuGet 元数据验证 | 测试 SDK |

## 五、Avalonia 项目依赖（不在 WP-01A 改造范围内）

`v2rayN.Desktop`（Avalonia 跨平台版本）有额外依赖，仅记录不在本次改造范围内：

| 包名 | 说明 |
|------|------|
| Avalonia.Desktop / Controls.DataGrid / Diagnostics | Avalonia UI 框架 |
| Semi.Avalonia / Semi.Avalonia.DataGrid / Semi.Avalonia.AvaloniaEdit | Semi Design Avalonia 主题 |
| DialogHost.Avalonia | Avalonia 对话框 |
| ReactiveUI.Avalonia | Avalonia ReactiveUI 集成 |
| SkiaSharp.NativeAssets.Linux | Linux SkiaSharp 原生库 |

## 六、许可证核查入口

| 核查项 | 入口 | 状态 |
|--------|------|------|
| GPL-3.0 主许可证 | `LICENSE` 文件 | ✅ 已保留 |
| 上游版权声明 | `README.md` / 代码头部注释 | ✅ 保留上游原始 |
| NuGet 包许可证 | `Directory.Packages.props` + NuGet.org 包元数据 | 待 R4B SBOM 扫描（provisional） |
| 核心二进制许可证 | Xray (MPL-2.0, 已确认) / sing-box (GPL v3, 待核实具体变体) | 待 R4B SBOM 扫描 |
| GlobalHotKeys 子模块 | `v2rayN/GlobalHotKeys/LICENSE` (WTFPL v2) / 上游 README (WTFPL v2 OR MIT) | 发布前确认采用哪个 |
| About 页面致谢 | WPF `MainWindow` / 设置 → 关于 | 待 R3 UI 实现 |

## 七、注意事项

1. **不得虚构许可证结论。** 上表中标注"待扫描"和"provisional"的条目，必须在 R4B 阶段
   通过正式 SBOM 工具扫描确认后才能标记为已确认。本文件中所有许可证推断均基于公开信息
   初步判断，不构成法律意见。
2. **分发时必须提供源代码。** GPL-3.0 要求二进制分发时提供完整源代码或书面获取途径。
3. **NetAccel 改造部分同样受 GPL-3.0 约束。** 任何在 fork 基础上新增的代码，
   包括托管模块 `NetAccel.Managed`，必须以 GPL-3.0 发布。
4. **核心许可证差异。** Xray 使用 MPL-2.0，sing-box 使用 GPL v3 变体。
   各核心分发时需确保各自许可证要求被满足。
   本文件不对 MPL-2.0 与 GPL-3.0 的兼容性做法律判断。
5. **GlobalHotKeys 许可证选择。** 分发树中 LICENSE 为 WTFPL v2，上游 README 允许
   WTFPL v2 OR MIT。发布前必须确认采用哪个许可证并在 NOTICE 文件中记录。
6. **所有许可证信息待正式核实。** 本文件不构成法律意见。发布前必须由法律顾问或
   经过正式 SBOM 扫描工具核实所有许可证结论。
