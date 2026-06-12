# WP-00 结果报告

> 任务包：WP-00 (T16-R0-01 至 R0-04)
> 状态：code_complete
> 日期：2026-06-12（含返工收口）
> 冻结 commit：1869a95700e17369f071ed23c8c485c2c3e83a1d

## 任务完成情况

### T16-R0-01：建立独立 NetAccel-v2rayN fork ✅

- origin → `https://github.com/YYDMie/v2rayN.git`
- upstream → `https://github.com/2dust/v2rayN.git`
- 当前分支：`codex/plan16`
- GPL-3.0 LICENSE 完整保留

### T16-R0-02：固定上游基线 ✅

- 标签 `7.22.6` → commit `f0ee79277853e886830ddd00ecf7b1f28f1d2035`
- 冻结 commit `1869a95700e17369f071ed23c8c485c2c3e83a1d`（`7.22.6+1`）
- .NET SDK `10.0.301`，通过 `global.json` 锁定
- 子模块 GlobalHotKeys → `569a95bb0fd2280d8d5581250aae54ecc2122d10`
- 核心版本（Xray/sing-box）为运行时动态获取，不静态定义
- 基线文档：`docs/BUILD_BASELINE.md`

### T16-R0-03：Windows WPF 可重复构建 ✅

- `dotnet restore v2rayN.sln`：7 个项目全部还原或为最新
- `dotnet test ServiceLib.Tests --no-restore`：46/46 通过，0 失败，0 跳过
- `dotnet publish -c Debug -r win-x64 -p:SelfContained=true`：成功，apphost 224,768 字节，运行时 DLL 在同目录
- `dotnet publish -c Release -r win-x64 -p:SelfContained=true`：成功，单文件 211,674,864 字节

### T16-R0-04：原生启动冒烟 ✅（部分自动 / 部分需人工）

**已自动验证：**

| 检查项 | 结果 |
|--------|------|
| Release self-contained 启动 | ✅ FirstPid 33368，8 秒后 FirstExited=False |
| .NET Runtime 缺失弹窗 | ✅ 未出现 |
| 主窗口标题 | ✅ `v2rayN - V7.22.6 - X64 - 以管理员身份运行` |
| 单实例互斥 | ✅ SecondPid 29668，SecondExited=True，CountAfterSecond=1 |
| 强制清理无残留 | ✅ Stop-Process 后 Residual=0 |

> 第二个实例检测到已有实例后自动退出，单实例互斥机制验证通过。
> 清理方式为 `Stop-Process` 强制终止，非应用通过托盘菜单正常退出。

**待人工验证：**

| 项目 | 原因 | 人工验证步骤 |
|------|------|-------------|
| 托盘图标真实渲染 | CLI 环境无法验证 GUI 渲染 | 启动后检查通知区域是否出现 v2rayN 图标 |
| 托盘菜单正常退出 | 自动化仅覆盖了 Stop-Process 强制清理 | 右键托盘 → 退出，确认进程全部终止 |
| 系统代理恢复 | 依赖托盘正常退出流程 | 退出后检查系统代理设置是否恢复为退出前状态 |

> **返工说明：** 初版使用 `--self-contained false`，导致 exe 在未安装 .NET Runtime 的机器上无法启动。
> 已修正为 `-p:SelfContained=true`，与 `.github/workflows/build.yml` 一致。

## 修改文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `global.json` | 新增 | 锁定 .NET SDK 10.0.301 |
| `docs/BUILD_BASELINE.md` | 新增 | 可重复构建文档（含返工收口） |
| `.plan16/WP-00-result.md` | 新增 | 本报告（含返工收口） |
| `.plan16/WP-00-complete.marker` | 新增 | 完成标记 |
| `.plan16/WP-00-rework-complete.marker` | 新增 | 返工收口完成标记 |

## 设计决策

1. **global.json**：使用 `rollForward: latestPatch` 允许补丁升级，避免每次 SDK 补丁都需修改。
2. **子模块初始化**：在构建文档中明确记录 `git submodule init && git submodule update` 步骤。
3. **核心版本**：Xray/sing-box 为运行时下载，不在基线中静态锁定。
4. **SelfContained**：必须使用 `-p:SelfContained=true`，与 CI workflow 一致。
5. **SDK 路径**：便携目录 `C:\tmp\dotnet10` 仅为当前机器复现路径，clean checkout 使用 PATH 中的 SDK 即可。

## 风险与限制

1. **托盘正常退出及系统代理恢复仍需人工验证。** 自动化冒烟仅覆盖了 `Stop-Process` 强制清理路径，
   未验证应用通过托盘菜单正常退出时的行为及系统代理恢复逻辑。
2. **首次运行核心下载不属于 WP-00 构建基线。** Xray/sing-box 二进制为运行时从 GitHub Releases 下载，
   构建产物本身不含核心。
3. **便携 SDK 目录仅为当前机器复现路径。** `C:\tmp\dotnet10` 不是通用要求；clean checkout 只需安装与
   `global.json` 匹配的 .NET SDK `10.0.3xx`。

## 仓库状态摘要

```
$ git diff --check
（无输出）

$ git status --short
?? .plan16/
?? docs/
?? global.json
```

- `git diff --check`：无 whitespace 错误。
- `git status --short`：仅 3 个未跟踪目录/文件，均为本次任务新增，无修改已有文件。
- `git submodule status`：`569a95bb0fd2280d8d5581250aae54ecc2122d10 v2rayN/GlobalHotKeys`（正常）。
