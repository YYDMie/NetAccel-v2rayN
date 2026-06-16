# WP-03 结果报告

## 状态

**verification_pass** — 经 FIX3 后所有验证命令通过。详见 [WP-03-FIX3-result.md](WP-03-FIX3-result.md)。

## 历史修复轮次

| 轮次 | 状态 | 说明 |
|------|------|------|
| FIX1 | code_complete | URL、ACK、ETag 代码修复 |
| FIX2 | code_complete | 验证命令修复（未能真正复现） |
| FIX3 | verification_pass | SDK 版本统一 + 验证全部通过 |

---

## FIX2 修复记录（保留）

## 修复的 5 个问题

| # | 问题 | 修复方式 | 验证 |
|---|------|----------|------|
| 1 | `/api/v1` 重复拼接 | `NormalizeBaseUrl` 只清理尾部斜杠和 `/api/v1`；`BuildUrl` 统一剥离 path 中的 `/api/v1` 前缀，确保只生成一次 | 30 种 URL 组合 Theory 测试通过 |
| 2 | config ACK `applied_revision` 逻辑错误 | 改为 `stage == "applied" && status == "success"` 时才设置 `applied_revision = revision`；其他情况为 `null` | 编译+测试通过 |
| 3 | `GetWithETagAsync` 不支持 401 retry | 添加与 `SendAsync` 一致的 401 refresh + retry 逻辑；retry 后保留 `If-None-Match` header；只 retry 一次 | 4 个 GetWithETagAsync 401 测试通过 |
| 4 | 304/null 语义误判 | 引入 `ETagResponse<T>` 结构化结果；`GetWithETagAsync` 返回 `ETagResponse<T>`，`IsNotModified` 明确标识 304；调用方使用 `etagResponse.IsNotModified` 判断 | GetWithETagAsync_304_ReturnsIsNotModified 测试通过 |
| 5 | 验证命令可复现 | `Directory.Build.props` 被前序 agent 改回 net9.0，再次改回 net10.0；dotnet.cmd 验证通过 | 全部验证命令通过 |

## 新增/修改文件

### 核心模块
- `v2rayN/NetAccel.Managed/Api/ManagedApiClient.cs`
  - 新增 `ETagResponse<T>` 类型
  - `IManagedApiClient.GetWithETagAsync` 返回类型改为 `ETagResponse<T>`
  - `NormalizeBaseUrl` 只清理尾部斜杠和 `/api/v1`
  - `BuildUrl` 剥离 path 中的 `/api/v1` 前缀，确保只生成一次
  - `GetWithETagAsync` 添加 401 refresh + retry 逻辑，保留 `If-None-Match`
  - `IsRefreshEndpoint` 改为检查 path 结尾（支持各种路径前缀）

- `v2rayN/NetAccel.Managed/Services/ManagedConfigSyncService.cs`
  - `FetchConfigAsync` 使用 `etagResponse.IsNotModified` 判断 304
  - `AckConfigAsync` 修复 `AppliedRevision = stage == "applied" && status == "success" ? revision : null`

### 测试文件
- `v2rayN/NetAccel.Managed.Tests/Api/ManagedApiClientTests.cs`
  - URL 规范化 Theory 测试覆盖 30 种组合（含真实服务层路径）
  - 新增 `GetWithETagAsync_200_ReturnsData` 测试
  - 新增 `GetWithETagAsync_401_RefreshThenRetry200` 测试
  - 新增 `GetWithETagAsync_401_RefreshThenRetry304` 测试
  - 新增 `GetWithETagAsync_401_RefreshFails_ThrowsManagedApiError` 测试
  - 新增 `GetWithETagAsync_401_RetriesOnlyOnce` 测试
  - 新增 `GetWithETagAsync_IfNoneMatch_PreservedAfterRefresh` 测试
  - 更新 `GetWithETagAsync_304_ReturnsIsNotModified` 使用新返回类型

- `v2rayN/NetAccel.Managed.Tests/Services/AuthServiceTests.cs`
  - 更新 `FakeApiClient.GetWithETagAsync` 返回 `ETagResponse<T>`
  - 更新 `CountingFakeApiClient.GetWithETagAsync` 返回 `ETagResponse<T>`

- `v2rayN/NetAccel.Managed.Tests/Services/InstanceServiceTests.cs`
  - 更新 `FakeApiClient.GetWithETagAsync` 返回 `ETagResponse<T>`

- `v2rayN/NetAccel.Managed.Tests/Services/StartupCoordinatorTests.cs`
  - 更新 `FakeApiClient.GetWithETagAsync` 返回 `ETagResponse<T>`

## 验证结果

### 1. NetAccel.Managed.Tests
```powershell
.\dotnet.cmd test .\v2rayN\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj --verbosity minimal
```
结果：**通过: 53, 失败: 0, 跳过: 0**（从 30 增加到 53 个测试）

### 2. ServiceLib.Tests
```powershell
.\dotnet.cmd test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj --no-restore --verbosity minimal
```
结果：**通过: 46, 失败: 0, 跳过: 0**

### 3. v2rayN WPF 构建
```powershell
.\dotnet.cmd build .\v2rayN\v2rayN\v2rayN.csproj -c Debug --verbosity minimal
```
结果：**已成功生成。0 警告，0 错误。**

### 4. git diff --check
仅有 `.github/workflows/*.yml` 和 `Directory.Build.props` 的 LF/CRLF 预先存在警告，无新增空白错误。

### 5. git status --short --branch
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
?? v2rayN/NetAccel.Managed.Tests/
?? v2rayN/NetAccel.Managed/
?? v2rayN/v2rayN/Views/ManagedSpikeWindow.xaml
?? v2rayN/v2rayN/Views/ManagedSpikeWindow.xaml.cs
```

## URL 规范化测试覆盖

Theory 测试覆盖 30 种输入组合：

| Base URL | Path | Expected URL |
|----------|------|--------------|
| `https://host` | `/client/login` | `https://host/api/v1/client/login` |
| `https://host/` | `/client/login` | `https://host/api/v1/client/login` |
| `https://host/api/v1` | `/client/login` | `https://host/api/v1/client/login` |
| `https://host/api/v1/` | `/client/login` | `https://host/api/v1/client/login` |
| `https://host` | `client/login` | `https://host/api/v1/client/login` |
| `https://host` | `/api/v1/client/login` | `https://host/api/v1/client/login` |
| `https://host/` | `/api/v1/client/login` | `https://host/api/v1/client/login` |
| `https://host/api/v1` | `/api/v1/client/login` | `https://host/api/v1/client/login` |
| `https://host/api/v1/` | `/api/v1/client/login` | `https://host/api/v1/client/login` |
| `https://host` | `api/v1/client/login` | `https://host/api/v1/client/login` |
| `https://host` | `/api/v1/client/managed/config` | `https://host/api/v1/client/managed/config` |
| `https://host/api/v1` | `/api/v1/client/managed/config` | `https://host/api/v1/client/managed/config` |
| `https://host` | `/api/v1/client/managed/status` | `https://host/api/v1/client/managed/status` |
| `https://host` | `/api/v1/client/managed/policy` | `https://host/api/v1/client/managed/policy` |
| `https://host` | `/api/v1/client/managed/selection` | `https://host/api/v1/client/managed/selection` |
| `https://host` | `/api/v1/client/managed/config/42/ack` | `https://host/api/v1/client/managed/config/42/ack` |
| `https://host` | `/api/v1/client/instances/register` | `https://host/api/v1/client/instances/register` |
| `https://host` | `/api/v1/client/instances/inst-1/heartbeat` | `https://host/api/v1/client/instances/inst-1/heartbeat` |
| `https://host` | `/api/v1/client/instances/inst-1/credentials/rotate` | `https://host/api/v1/client/instances/inst-1/credentials/rotate` |
| `https://host` | `/api/v1/client/logout` | `https://host/api/v1/client/logout` |
| `https://host` | `/api/v1/client/refresh` | `https://host/api/v1/client/refresh` |

## GetWithETagAsync 测试覆盖

| 测试场景 | 期望行为 |
|----------|----------|
| 304 Not Modified | `IsNotModified = true`, `Data = null` |
| 200 OK | `IsNotModified = false`, `Data != null` |
| 401 → refresh 成功 → retry 200 | 2 次调用，`Data != null` |
| 401 → refresh 成功 → retry 304 | 2 次调用，`IsNotModified = true` |
| 401 → refresh 失败 | 1 次调用，抛出 `ManagedApiError` |
| 401 → refresh 成功 → retry 401 | 2 次调用（只 retry 一次），抛出 `ManagedApiError` |
| 401 → refresh 成功 → retry 保留 `If-None-Match` | 2 次调用，`IsNotModified = true`，`If-None-Match` header 保留 |

## 安全确认

- [x] access token / refresh token / instance credential 未存入 app config、SQLite、普通 JSON settings、日志或 ViewModel 属性
- [x] WindowsCredentialVault 使用真实 Windows Credential Manager
- [x] ManagedApiClient 不记录 token、instance credential、envelope plaintext
- [x] spike-v0 envelope 明文仅存在于内存，不缓存，不启用离线启动
- [x] `ConfigSyncResult.DecryptedPayload` 仅作为单次调用短生命周期结果返回
- [x] WP-03 managed flow 未使用旧 `/client/plans` 和 `/client/plan/:id/select`
- [x] 未修改 SubscriptionHandler、AddBatchServers、classic import/export parsers
- [x] 未将 managed profiles 写入 SubItem 或 ProfileItem SQLite
- [x] 未移除 local node、generic subscription、import/export 或现有 v2rayN 兼容功能
- [x] 未触碰 Avalonia v2rayN.Desktop
