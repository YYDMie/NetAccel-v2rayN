# WP-05 Windows 实机/E2E 验证提示词

任务：WP-05 Windows 行为与真实线路验证。

仓库：

```text
D:\AI\Claude code\NetAccel-v2rayN
```

分支：

```text
codex/plan16-wp05-runtime
```

使用仓库 `global.json` 要求的 .NET SDK。若本机已有下列 SDK 路径，优先使用：

```powershell
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' --info
```

不要修改 `global.json`。不要 commit，不要 push。

## 范围

只验证 WP-05：

- T16-R2-10 `ConnectionOwnershipCoordinator`
- T16-R2-11 `ManagedConnectionCoordinator`
- T16-R2-12 托管系统代理连接
- T16-R2-13 托管 TUN 连接
- T16-R2-14 授权集合内选线
- T16-R2-15 安全切线与自动故障切换

不要进入 WP-06、UI/R3、托盘、经典模式 UI 整理、编辑/复制/分享/导出/备份守卫、
Plan 17、部署、套餐/计费/订阅销售、公共节点池或 NetAccel 订阅链接逻辑。

## 项目定位

NetAccel 是个人学习 demo，采用类似游戏加速器的受控分配模型。服务端/管理员决定
设备可用线路集合、推荐线路、fallback 顺序和策略；设备只能在已分配集合内自动选择
或手动选择。

计划 16 第一阶段保留 v2rayN 原生本地节点、通用订阅、导入/导出能力，作为兼容、
迁移和测试通道。这些经典能力不是 NetAccel 托管默认入口，必须与托管线路分离，
本任务不得删除或破坏它们。

托管线路参数只读，不得编辑、分享、导出、转换为经典本地节点，也不得暴露为订阅链接。

## 先读文件

Master 仓库：

```text
D:\AI\Claude code\NetAccel
```

先读：

- `AGENTS.md`
- `docs/16_00_*`
- `docs/16_01_*`
- `docs/16_02_*`
- `docs/16_04_*`
- `docs/16_05_*`
- `docs/16_06_*`

Client 仓库：

- `.plan16/WP-05-codex-prompt.md`
- `.plan16/WP-05-result.md`
- `.plan16/WP-05-claude-verify-fix-prompt.md`
- `NetAccel.Managed/Runtime/ConnectionOwnershipCoordinator.cs`
- `NetAccel.Managed/Runtime/ManagedConnectionCoordinator.cs`
- `NetAccel.Managed.Tests/ConnectionOwnershipCoordinatorTests.cs`
- `NetAccel.Managed.Tests/ManagedConnectionCoordinatorTests.cs`
- `v2rayN/ServiceLib/Manager/AppManager.cs`
- `v2rayN/ServiceLib/Manager/CoreManager.cs`

## 目标

收集 WP-05 从 `code_complete_codex_verified` 进入最终验收所需的 Windows 行为证据。

本任务以验证为主。只有发现明确的 WP-05 安全或清理缺陷时，才允许做最小修复；
修复范围必须限制在 WP-05 runtime 或 ServiceLib 集成相关代码内。若做了修复，
必须重新执行完整验证矩阵并记录变更。

## 前置检查

记录：

```powershell
git status --short --branch
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' --info
whoami
([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsHardwareAbstractionLayer
```

任何连接尝试之前，先记录系统基线：

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' |
  Select-Object ProxyEnable, ProxyServer, AutoConfigURL

Get-NetAdapter | Sort-Object Name | Select-Object Name, InterfaceDescription, Status, MacAddress
Get-Process | Where-Object { $_.ProcessName -match 'xray|sing-box|v2ray|NetAccel' } |
  Select-Object Id, ProcessName, Path
```

如果缺少管理员权限、真实 Master 账号、真实已分配托管线路或必要核心文件，对应 E2E
项必须标记为 `blocked`。不要把未执行的项目写成通过。

## 必跑构建与单元测试

运行：

```powershell
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' build .\NetAccel.Managed\NetAccel.Managed.csproj -c Debug
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' build .\v2rayN\v2rayN\v2rayN.csproj -c Debug
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj -c Debug --filter "FullyQualifiedName~ConnectionOwnershipCoordinatorTests|FullyQualifiedName~ManagedConnectionCoordinatorTests"
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj -c Debug --no-build
git diff --check
```

不要隐藏全量 `NetAccel.Managed.Tests` 中既有 fixture 路径失败。如果运行全量测试，
必须报告精确结果。

## Windows 行为矩阵

### 1. 托管系统代理模式

必须记录：

- 连接前 proxy 注册表基线。
- 托管连接启动结果。
- 连接中 proxy 注册表值。
- 连接中核心进程状态。
- 如果有真实线路，记录出口可达性或公网出口检查结果。
- 停止连接结果。
- 停止后 proxy 注册表值，必须恢复到基线，或符合 ServiceLib 的安全清理行为。
- 停止后核心进程已退出。

通过条件：

- core 能干净启动和停止。
- 连接中系统代理被正确应用。
- stop、启动失败、清理流程后系统代理会恢复或安全清空。
- stop 后没有 owner 残留。

### 2. 托管 TUN 模式

只在具备管理员权限时执行。

必须记录：

- 连接前网络适配器基线。
- 托管 TUN 启动结果。
- 连接中 adapter/route 证据。
- 连接中核心进程状态。
- 如果有真实线路，记录出口可达性或公网出口检查结果。
- 停止连接结果。
- 停止后 adapter/route 证据。
- 停止后核心进程已退出。

同时尝试或说明非管理员行为：

- 非管理员 TUN 必须以清晰的权限不足/阻止状态失败。
- 失败后不得留下半创建的 TUN adapter、proxy、core 进程或 owner lease。

通过条件：

- 只有 policy 和 OS 权限都允许时，TUN 才能启动。
- stop 和启动失败路径都会执行 TUN 清理。
- 清理后没有 TUN adapter、route、proxy、core 进程或 owner 残留。

### 3. 真实 VLESS Reality / Hysteria2 E2E

仅在具备真实 Master 凭据和已分配真实线路时执行。

必须记录：

- 托管配置 revision 和 profile id，需脱敏。
- VLESS Reality 线路被选择并连接成功。
- Hysteria2 线路被选择并连接成功。
- 每条线路的出口或可达性证明，需脱敏。
- `applied` ACK 尝试结果。
- 如可用，记录 session/heartbeat 证据。

不要打印 token、私钥、完整服务端凭据、完整配置或完整订阅/节点 URL。

### 4. 授权集合内选线、安全切线和 fallback

必须记录：

- 手动选择已分配且可用线路成功。
- 手动选择未分配、不可用、维护中或能力不兼容线路，在 core start 前被拒绝。
- 手动切线失败时恢复原线路。
- 手动切线被取消时恢复原线路。
- 自动 fallback 只能使用授权 fallback profiles。
- 自动 fallback 不覆盖用户 manual preference。

### 5. Managed/Classic 连接所有权

必须记录：

- 托管连接中 Managed 拥有 core/proxy/TUN 控制权。
- 经典连接/启动尝试无法通过热键、托盘、直接命令或 reload 路径并发运行。
- 托管 stop 后，如经典兼容路径可用，经典模式仍可使用。
- 经典 stop 后，Managed 可重新获取所有权。

不得为了让测试通过而删除或禁用经典本地节点、订阅、导入/导出路径。

## 收尾检查

每次测试尝试后都要记录：

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' |
  Select-Object ProxyEnable, ProxyServer, AutoConfigURL

Get-NetAdapter | Sort-Object Name | Select-Object Name, InterfaceDescription, Status, MacAddress
Get-Process | Where-Object { $_.ProcessName -match 'xray|sing-box|v2ray|NetAccel' } |
  Select-Object Id, ProcessName, Path

git status --short --branch
git diff --check
```

如果系统代理、TUN adapter、route、进程或 owner 状态有任何残留，必须标记为失败，
并记录实际采用的清理步骤。

## 结果文件

创建或更新：

```text
.plan16/WP-05-windows-e2e-result.md
```

使用以下格式：

```text
# WP-05 Windows E2E Result

Status: passed / blocked / failed
Date:

Environment:
- Windows:
- Admin:
- .NET SDK:
- dotnet path:
- Master reachable:
- real assigned VLESS Reality:
- real assigned Hysteria2:

Build/unit verification:
- command -> result

System proxy:
- baseline:
- connected:
- stopped:
- result:

TUN:
- baseline:
- connected:
- stopped:
- non-admin behavior:
- result:

Real-line E2E:
- VLESS Reality:
- Hysteria2:
- result:

Selection/switch/fallback:
- assigned manual:
- unauthorized/unavailable rejection:
- failed switch restore:
- cancelled switch restore:
- automatic fallback:
- result:

Managed/Classic ownership:
- concurrent classic blocked:
- classic after managed stop:
- managed after classic stop:
- result:

Postflight cleanup:
- proxy:
- adapters/routes:
- processes:
- owner:

Changes made:
- path: summary

Not executed:
- item: reason

Risks/limits:
- ...
```

## 返回格式

完成后回复：

```text
Task: WP-05 Windows E2E verify
Status: passed / blocked / failed

Summary:
- ...

Evidence file:
- .plan16/WP-05-windows-e2e-result.md

Verification:
- command -> result

Not executed:
- item: reason

Changes:
- path: summary, or none

Workspace:
- git diff --check:
- git status --short:
```
