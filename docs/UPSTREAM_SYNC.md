# 计划 16 - 上游同步规范

> 状态：基线
> 创建日期：2026-06-12
> 上游：`https://github.com/2dust/v2rayN.git`
> Fork：`https://github.com/YYDMie/v2rayN.git`

## 一、Remote 配置

| Remote | URL | 用途 |
|--------|-----|------|
| `origin` | `https://github.com/YYDMie/v2rayN.git` | NetAccel fork，推拉代码 |
| `upstream` | `https://github.com/2dust/v2rayN.git` | 上游 2dust/v2rayN，只读同步 |

当前状态：

```
origin   → https://github.com/YYDMie/v2rayN.git (fetch/push)
upstream → https://github.com/2dust/v2rayN.git (fetch/push)
```

## 二、Dry-Run 预检（无副作用）

在修改当前分支历史之前，使用 `git merge-tree` 进行无副作用预检。
该命令不修改工作区、不创建分支、不改变 HEAD。

### 2.1 首选命令：merge-tree

```powershell
# 1. 获取上游最新代码（不修改任何本地状态）
git fetch upstream

# 2. 无副作用合并预检
git merge-tree --write-tree HEAD upstream/master
# 输出 merge-tree 结果 tree SHA（纯计算，不修改 index/工作区/HEAD）

# 3. 查看上游新提交
git log --oneline HEAD..upstream/master

# 4. 查看预期冲突
git diff --name-only HEAD upstream/master
```

### 2.2 已执行的 dry-run 结果（2026-06-12）

```
HEAD:           1869a95700e17369f071ed23c8c485c2c3e83a1d
upstream/master: da81101cfd339f9ae4ff32bdb1597796cacedce7
merge-base:     1869a95700e17369f071ed23c8c485c2c3e83a1d
HEAD is ancestor: True
merge-tree exit code: 0
merge-tree result tree: cebd26add66fc035a1906f56549c78fbf89786cf
```

结论：HEAD 是 upstream/master 的祖先，merge 无冲突。上游有 6 个文件变更：

```
v2rayN/Directory.Build.props                              (版本号)
v2rayN/ServiceLib/Handler/Fmt/VmessFmt.cs                 (VMess 解析)
v2rayN/ServiceLib/Models/CoreConfigs/V2rayConfig.cs        (核心配置模型)
v2rayN/ServiceLib/Models/Dto/VmessQRCode.cs                (VMess 二维码)
v2rayN/ServiceLib/Sample/SampleClientConfig                (示例配置)
v2rayN/ServiceLib/Services/CoreConfig/V2ray/V2rayStatisticService.cs (统计服务)
```

工作区和分支在 dry-run 后未被修改（已验证 `git status`）。

### 2.3 后续人工验证（临时分支 rebase）

如需在临时分支上实际执行 rebase 验证冲突解决：

```powershell
# 创建临时分支
git checkout -b dryrun/sync-test codex/plan16

# 实际 rebase（会修改临时分支）
git fetch upstream
git rebase upstream/master

# 观察冲突数量和位置
# 验证构建
cd v2rayN
dotnet restore v2rayN.sln
dotnet test ServiceLib.Tests --no-restore
cd ..

# 运行边界检查
powershell -ExecutionPolicy Bypass -File scripts/plan16/check-change-boundaries.ps1

# 如不满意，回退到原分支
git checkout codex/plan16
git branch -D dryrun/sync-test

# 如满意，执行正式同步（见第三节）
```

## 三、正式同步流程

### 3.1 标准同步步骤（推荐使用 rebase）

```powershell
# 1. 确保当前在 codex/plan16 分支，工作区干净
git status --short
# 应为空（无未提交改动）

# 2. 获取上游最新代码
git fetch upstream

# 3. 查看上游新提交数量
git log --oneline HEAD..upstream/master | Measure-Object -Line

# 4. 创建同步临时分支（保护主分支）
git checkout -b sync/upstream-$(Get-Date -Format 'yyyyMMdd') codex/plan16

# 5. Rebase 到上游最新
git rebase upstream/master

# 6. 解决冲突（如有）
# 详见第四节"冲突区域和解决策略"

# 7. 运行测试验证
cd v2rayN
dotnet restore v2rayN.sln
dotnet test ServiceLib.Tests --no-restore
cd ..

# 8. 运行边界检查
powershell -ExecutionPolicy Bypass -File scripts/plan16/check-change-boundaries.ps1

# 9. 确认无误后合入主分支
git checkout codex/plan16
git merge sync/upstream-$(Get-Date -Format 'yyyyMMdd')

# 10. 清理临时分支
git branch -d sync/upstream-$(Get-Date -Format 'yyyyMMdd')
```

### 3.2 替代方案：merge（当 rebase 冲突过多时）

```powershell
# 仅在 rebase 冲突过多时使用
git fetch upstream
git checkout codex/plan16
git merge upstream/master --no-ff -m "sync: merge upstream v2rayN <new-commit-short>"
```

**决策规则：**
- 上游变更少于 50 个文件且冲突少于 5 处 → 使用 rebase（保持线性历史）
- 上游变更大规模重构或冲突过多 → 使用 merge（保留清晰的同步点）
- 无论哪种方式，都必须在临时分支上先验证再合入

## 四、冲突区域和解决策略

### 4.1 预期冲突热点

| 区域 | 路径 | 冲突概率 | 策略 |
|------|------|----------|------|
| WPF 主窗口 | `v2rayN/v2rayN/Views/MainWindow.xaml(.cs)` | 高 | 优先保留 NetAccel 改动，审查上游新功能 |
| WPF Views | `v2rayN/v2rayN/Views/*.xaml(.cs)` | 中 | 逐文件审查，NetAccel 托管视图优先 |
| WPF ViewModels | `v2rayN/v2rayN/ViewModels/*.cs` | 中 | 主题设置等保留 NetAccel |
| ServiceLib ViewModels | `v2rayN/ServiceLib/ViewModels/*.cs` | 中 | `MainWindowViewModel` 冲突概率最高 |
| ServiceLib 全局 | `v2rayN/ServiceLib/Global.cs` | 中 | 全局配置和枚举 |
| 配置处理器 | `v2rayN/ServiceLib/Handler/ConfigHandler.cs` | 中 | 数据模型变更 |
| 核心配置 | `v2rayN/ServiceLib/Services/CoreConfig/` | 低-中 | 配置生成逻辑 |
| 枚举 | `v2rayN/ServiceLib/Enums/*.cs` | 低 | 新协议类型等 |
| 实体模型 | `v2rayN/ServiceLib/Models/Entities/*.cs` | 低 | 字段增删 |
| 项目文件 | `*.csproj`, `Directory.Build.props` | 低 | 依赖版本 |

### 4.2 解决优先级

1. **NetAccel 托管模块** (`NetAccel.Managed/`, `NetAccel.Managed.Tests/`) → 无冲突，上游无此目录
2. **NetAccel 文档和脚本** (`docs/`, `scripts/`, `.plan16/`) → 无冲突，上游无此目录
3. **WPF 托管壳** (计划 16 R3 新增的 WPF 页面) → 保留 NetAccel 改动
4. **ServiceLib 共享层** → 逐行审查，保留上游 bug fix，保留 NetAccel 扩展
5. **上游纯新增功能** → 审查后接受，除非与 NetAccel 改造冲突

## 五、保护目录规则

以下目录/文件在同步时有特殊保护要求：

### 5.1 绝对保护（不得被上游覆盖）

| 目录/文件 | 原因 |
|-----------|------|
| `NetAccel.Managed/` | 托管模块，上游无此目录 |
| `NetAccel.Managed.Tests/` | 托管测试 |
| `.plan16/` | 计划 16 状态和证据 |
| `docs/` | NetAccel 文档 |
| `scripts/` | NetAccel 脚本 |
| `global.json` | .NET SDK 版本锁定 |
| `LICENSE` | GPL-3.0，已保留上游原始 |

### 5.2 协同修改（需人工审查）

| 目录/文件 | 原因 |
|-----------|------|
| `v2rayN/v2rayN/Views/*.xaml(.cs)` | WPF 视图，可能被 NetAccel 扩展 |
| `v2rayN/ServiceLib/ViewModels/` | ViewModel 层，可能被 NetAccel 扩展 |
| `v2rayN/ServiceLib/Handler/` | 处理器，可能被 NetAccel 增加来源守卫 |
| `v2rayN/ServiceLib/Manager/` | 管理器，CoreManager 等 |
| `v2rayN/ServiceLib/Global.cs` | 全局配置 |
| `v2rayN/v2rayN.sln` | 解决方案文件，NetAccel 可能新增项目 |

### 5.3 上游权威目录（必须经过 diff 审查）

以下目录以上游代码为主，但**不是无条件接受**。即使上游是权威来源，
每次同步仍必须：

1. 执行 `git diff` 逐文件审查变更内容
2. 运行经典回归测试（`ServiceLib.Tests` 46/46）
3. 运行边界检查脚本
4. 确认不影响 NetAccel 托管隔离

| 目录/文件 | 原因 |
|-----------|------|
| `v2rayN/ServiceLib/Services/CoreConfig/` | 核心配置生成，上游权威 |
| `v2rayN/ServiceLib/Handler/Fmt/` | 格式解析器，上游权威 |
| `v2rayN/ServiceLib/Handler/Builder/` | 配置构建器 |
| `v2rayN/ServiceLib/Models/` | 数据模型 |
| `v2rayN/ServiceLib/Enums/` | 枚举定义 |
| `v2rayN/GlobalHotKeys/` | 子模块，上游权威 |

### 5.4 Avalonia 项目（永久禁止修改）

| 目录/文件 | 原因 |
|-----------|------|
| `v2rayN/v2rayN.Desktop/` | Avalonia 跨平台项目，NetAccel 不修改 |

## 六、同步后检查清单

每次同步完成后必须执行：

```text
[ ] 1. git log 确认同步 commit 范围
[ ] 2. dotnet restore v2rayN.sln
[ ] 3. dotnet test ServiceLib.Tests --no-restore
[ ] 4. dotnet build v2rayN/v2rayN.csproj -c Debug -p:EnableWindowsTargeting=true
[ ] 5. powershell scripts/plan16/check-change-boundaries.ps1
[ ] 6. 检查 NetAccel 托管模块未被修改
[ ] 7. 检查 docs/、.plan16/、scripts/ 未被修改
[ ] 8. 检查 global.json 未被修改
[ ] 9. 更新 docs/BUILD_BASELINE.md 中的上游 commit 记录
[ ] 10. 更新 docs/CLASSIC_REGRESSION_MATRIX.md（如上游新增/删除功能）
```

## 七、版本记录

| 同步日期 | 上游 old commit | 上游 new commit | 策略 | 冲突数 | 结果 |
|----------|-----------------|-----------------|------|--------|------|
| （待填写） | | | | | |
