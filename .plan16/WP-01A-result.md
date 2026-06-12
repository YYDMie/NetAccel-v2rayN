# WP-01A 结果报告（rework v2）

> 任务包：WP-01A (T16-R0-05 至 R0-10)
> 状态：code_complete（GUI 截图项 blocked / 未 accepted）
> 最终 rework：已完成（边界检查两项缺陷修复 + 16 个测试全部通过）
> 日期：2026-06-12
> 基线 commit：`1869a95700e17369f071ed23c8c485c2c3e83a1d`（`7.22.6+1`）

## 任务完成情况

### T16-R0-05：原生功能回归脚本/清单 ✅

- 创建 `docs/CLASSIC_REGRESSION_MATRIX.md`
- 覆盖 16 个回归条目（REG-01 至 REG-16），对应 `16_01` 最低回归集合
- 每项包含前置条件、操作步骤、期望、证据路径和当前状态
- 当前无法真实执行的 GUI 项标为 `pending` 或 `blocked`
- 托盘和主窗口截图标为 `blocked`（需 GUI 环境）
- 未执行项均不冒充通过

### T16-R0-06：GPL 和第三方通知 ✅

- 创建 `docs/LICENSING_AND_ATTRIBUTION.md`
- 记录 GPL-3.0 主许可证（`LICENSE` 文件已保留）
- 记录 2dust/v2rayN 上游来源和 commit 信息
- 核心组件：Xray MPL-2.0（已确认），sing-box GNU GPL v3（具体 SPDX 变体待核实固定 release）
- NuGet 第三方依赖许可证统一标为"待 R4B SBOM/NuGet 元数据验证"（provisional）
- GlobalHotKeys 子模块：仓库内 LICENSE 为 WTFPL v2，上游 README 为 WTFPL v2 OR MIT，差异已记录
- 不虚构尚未扫描出的许可证结论，不做法律兼容性判断

### T16-R0-07：建立上游同步规范 ✅

- 创建 `docs/UPSTREAM_SYNC.md`
- 记录 origin/upstream remote 配置
- **Dry-run 已用 `git merge-tree --write-tree` 实际执行**（无副作用）：
  - HEAD: `1869a957...`，upstream/master: `da81101c...`
  - merge-base: `1869a957...`（HEAD 是 upstream/master 的祖先）
  - merge-tree exit code: 0，result tree: `cebd26add...`
  - 上游 6 个文件变更（版本号、VMess 解析、配置模型、示例、统计服务）
  - 工作区和分支在 dry-run 后未被修改
- `git merge-tree --write-tree` 作为首选无副作用预检命令
- 临时分支 rebase 流程改为"后续人工验证方式"
- 删除"无条件接受上游"措辞，共享 CoreConfig/Fmt/Models/Enums 必须经过 diff 审查、经典回归和托管隔离测试
- WPF 主窗口路径已修正为 `v2rayN/v2rayN/Views/MainWindow.xaml(.cs)`
- 版本记录表待首次正式同步后填写

### T16-R0-08：新客户端知识图谱基线 ✅

- 创建 `docs/ARCHITECTURE_MAP.md`
- 覆盖完整项目结构（WPF/ServiceLib/Tests）
- 定位 WPF App → MainWindow → Views → ViewModels 链路
- **连接链已修正**：
  - `MainWindowViewModel.Reload()` → `CoreConfigContextBuilder.BuildAll()` → `CoreManager.LoadCore()` → `SysProxyHandler.UpdateSysProxy()`
  - `SysProxyHandler.UpdateSysProxy` 由 `MainWindowViewModel` 在 `LoadCore` 返回后调用，不是由 `CoreManager` 内部调用
- **ERuleType.ts 已修正为 ERuleType.cs**
- WPF 特有 ViewModel 确认仅有 `v2rayN/v2rayN/ViewModels/ThemeSettingViewModel.cs`
- 所有文件路径均通过 rg/代码读取验证

### T16-R0-09：冻结经典模式基线截图 ⚠️（code_complete / 截图 blocked）

- 创建 `docs/evidence/classic-baseline/README.md`
- 列出 16 个所需截图及保存路径
- 定义命名规范（`REG-{编号}-{简述}.png`）
- 定义截图要求（分辨率、主题、语言）
- **所有截图当前标为 `blocked`（需 GUI 环境）**
- **截图采集未 accepted**：CLI 环境无法采集 GUI 截图，需人工在 GUI 环境下执行
- 提供人工采集步骤
- 证据清单 code_complete，截图采集 blocked / 未 accepted

### T16-R0-10：建立变更边界检查 ✅

- 创建 `scripts/plan16/check-change-boundaries.ps1`
- 检查 4 类边界违规：
  1. Avalonia 项目修改（永久禁止，即使被 allowlist 覆盖也 ERROR）
  2. 经典入口/解析器删除（永久禁止，即使被 allowlist 覆盖也 ERROR）
  3. 源码/根目录改动不在 allowlist 中（ERROR，非 INFO）
  4. 受保护根文件（global.json/LICENSE）删除
- **新增 `-AllowedPathPrefixes` 参数**，支持显式精确目录或文件
- **默认 R0 allowlist**：`.plan16/`、`docs/`、`scripts/`、`global.json`、`NetAccel.Managed/`、`NetAccel.Managed.Tests/`
- 不在默认/显式 allowlist 中的源码改动返回 ERROR
- Avalonia 即使被显式 allowlist 覆盖也必须 ERROR
- 经典文件删除即使被 allowlist 覆盖也必须 ERROR
- 正确处理 tracked、staged、unstaged 和 untracked 文件
- BaseRef 不可解析时必须明确失败（exit 2），除非显式 `-FallbackToWorkingTree`
- **缺陷修复（rework v3）**：
  1. **精确文件 allowlist 匹配**：以 `/` 结尾的条目为目录前缀（StartsWith），不以 `/` 结尾的条目为精确文件路径（Equals）。修复前 `global.json` 会错误匹配 `global.json.evil`，`ConfigHandler.cs` 会错误匹配 `ConfigHandler.cs.backup`。
  2. **永久删除检查覆盖 staged/unstaged**：删除文件集合从三个来源构建：BaseRef..HEAD 提交删除、staged 删除（`git diff --cached --diff-filter=D`）、unstaged 删除（`git diff --diff-filter=D`）。修复前仅检查已提交删除。
  3. **更新参数文档**：不再将所有条目描述为前缀，明确区分目录前缀和精确文件路径。
- 创建 `scripts/plan16/tests/test-boundary-checker.ps1` 自测
- 16 个 fixture 全部通过：
  - 4 个允许改动（docs/scripts/global.json/Managed）→ PASS
  - 1 个未知 ServiceLib 改动 → FAIL
  - 1 个显式 allowlist 允许 ServiceLib 文件 → PASS
  - 1 个 Avalonia + allowlist → FAIL
  - 2 个删除（Parser/CoreManager）→ FAIL
  - 1 个未知根目录文件 → FAIL
  - 1 个无改动 → PASS
  - 1 个 global.json.evil 被拒绝（缺陷 1 回归）→ FAIL
  - 1 个 ConfigHandler.cs.backup 被拒绝（缺陷 1 回归）→ FAIL
  - 1 个 unstaged 关键文件删除 + 显式 allowlist（缺陷 2 回归）→ FAIL
  - 1 个 staged 关键文件删除 + 显式 allowlist（缺陷 2 回归）→ FAIL
  - 1 个 unstaged 受保护根文件删除（缺陷 2 回归）→ FAIL

## 修改文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `docs/CLASSIC_REGRESSION_MATRIX.md` | 新增 | T16-R0-05 经典功能回归矩阵 |
| `docs/LICENSING_AND_ATTRIBUTION.md` | 新增 | T16-R0-06 许可证与第三方致谢（provisional） |
| `docs/UPSTREAM_SYNC.md` | 新增 | T16-R0-07 上游同步规范（含真实 dry-run 结果） |
| `docs/ARCHITECTURE_MAP.md` | 新增 | T16-R0-08 客户端知识图谱（已修正连接链和 ERuleType） |
| `docs/evidence/classic-baseline/README.md` | 新增 | T16-R0-09 截图证据清单 |
| `scripts/plan16/check-change-boundaries.ps1` | 新增 | T16-R0-10 边界检查脚本（严格 allowlist 逻辑） |
| `scripts/plan16/tests/test-boundary-checker.ps1` | 新增 | T16-R0-10 边界检查自测（11 个 fixture） |
| `.plan16/WP-01A-result.md` | 新增 | 本报告 |
| `.plan16/WP-01A-complete.marker` | 新增 | 完成标记 |

## 设计决策

1. **回归矩阵格式**：使用 markdown 表格而非 YAML/JSON，便于人工阅读和填写；Status 列限制为 4 种值。
2. **边界检查脚本**：使用 PowerShell 5.1 兼容语法；默认 BaseRef 为冻结 commit；新增 `-AllowedPathPrefixes` 精确控制允许范围；Avalonia 和经典文件删除为永久禁止规则。
3. **自测 fixtures**：使用临时 git 仓库隔离测试，避免影响主仓库；16 个 fixture 覆盖允许、越界、显式 allowlist、永久禁止、无改动、精确匹配回归和 staged/unstaged 删除回归七类。支持 `-SkipCommit`/`-SkipStage` 参数测试未提交变更场景。
4. **知识图谱**：使用文本树状结构而非 Mermaid 图，便于终端查看；所有路径通过代码搜索验证；连接链基于实际代码行号。
5. **截图证据**：全部标为 `blocked`，不伪造 GUI 截图；命名规范与回归矩阵 REG 编号对齐。
6. **许可证**：所有 NuGet 包许可证标为 provisional（待 R4B SBOM 扫描）；核心组件许可证基于 GitHub API 确认或标注待核实；不做法律兼容性判断。
7. **上游同步**：`git merge-tree --write-tree` 作为首选无副作用预检；临时分支 rebase 作为后续人工验证方式。

## 验证命令和结果

| 命令 | 结果 |
|------|------|
| `powershell scripts/plan16/tests/test-boundary-checker.ps1` | ✅ 16/16 通过 |
| `dotnet test ServiceLib.Tests --no-restore` | ✅ 46/46 通过，0 失败 |
| `git diff --check` | ✅ 无输出（无 whitespace 错误） |
| `git status --short` | `?? .plan16/` `?? docs/` `?? global.json` `?? scripts/` |
| `powershell scripts/plan16/check-change-boundaries.ps1` | ✅ PASS，0 errors，0 warnings |
| `git merge-tree --write-tree HEAD upstream/master` | ✅ exit 0，无冲突（HEAD 是上游祖先） |

## 人工门禁

| 项目 | 状态 | 原因 |
|------|------|------|
| 经典模式截图采集（16 项） | `blocked` | 需 GUI 环境，Computer Use 不可用 |
| 托盘菜单正常退出 | `blocked` | WP-00 已标为人工门禁 |
| 系统代理恢复 | `blocked` | WP-00 已标为人工门禁 |

## 风险

1. **回归矩阵截图全部 blocked。** 当前 CLI 环境无法采集 GUI 截图，需人工在 GUI 环境下执行。
   截图采集未 accepted。
2. **边界检查仅覆盖目录级规则。** 文件内容级检查（如关键方法是否被删除）未实现，可作为后续增强。
3. **许可证 NuGet 包扫描待 R4B。** 当前所有 NuGet 包许可证均为 provisional，未通过正式 SBOM 工具扫描。
4. **sing-box 具体 SPDX 变体未核实。** GitHub API 报告 NOASSERTION，需核实固定 release 源码中的 LICENSE 文件。
5. **GlobalHotKeys 许可证选择待确认。** 分发树中 LICENSE 为 WTFPL v2，上游 README 允许 WTFPL v2 OR MIT，发布前需确认采用哪个。

## 仓库状态摘要

```
$ git diff --check
（无输出）

$ git status --short
?? .plan16/
?? docs/
?? global.json
?? scripts/

$ powershell scripts/plan16/check-change-boundaries.ps1
RESULT: PASS - All boundary checks passed.

$ powershell scripts/plan16/tests/test-boundary-checker.ps1
RESULT: ALL TESTS PASSED (16/16)

$ dotnet test ServiceLib.Tests --no-restore
通过: 46, 失败: 0, 跳过: 0
```

- `git diff --check`：无 whitespace 错误
- `git status --short`：仅 4 个未跟踪目录/文件，均为本次任务新增，无修改已有文件
- 边界检查脚本：默认参数下 PASS，16 个自测 fixture 全部通过（含缺陷修复回归测试）
- 不包含 WP-01B 的应用身份、更新入口、ManagedRuntimeConfig 或 release schema 实现
- 不修改 Avalonia 和原生功能
