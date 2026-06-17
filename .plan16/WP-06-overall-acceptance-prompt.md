# WP-06 总体验收 / Verify-Fix 提示词

任务：Plan 16 WP-06 来源隔离和经典模式总体验收

状态目标：verify/fix。先验收 WP-06A + WP-06B 的合并效果；只有发现阻断性问题时才做最小修复。不要进入 WP-07、UI/R3、安装器、发布、真实线路 E2E 或经典能力退场。

工作目录：

```text
D:\AI\Claude code\NetAccel-v2rayN
```

环境注意：

- 使用本机 .NET SDK：`C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe`
- 不要修改 `global.json`。
- 当前工作区包含 WP-05、WP-06A、WP-06B 的未提交改动；不要 `git reset`、不要 revert、不要覆盖这些改动。
- `git diff --check` 已知可能只有 `v2rayN/ServiceLib/Manager/CoreManager.cs` 的 CRLF warning，记录即可。
- 全量 `NetAccel.Managed.Tests` 已知有 14 个既有 fixture path 失败，路径指向 `D:\AI\Claude code\NetAccel\contracts\fixtures\...`，不是 WP-06 回归；但必须运行并记录。

先阅读：

1. `D:\AI\Claude code\NetAccel\AGENTS.md`
2. `D:\AI\Claude code\NetAccel\docs\16_00_托管客户端文档索引.md`
3. `D:\AI\Claude code\NetAccel\docs\16_04_托管客户端开发任务分解.md`
4. `D:\AI\Claude code\NetAccel\docs\16_05_Claude_Code任务提示词手册.md`
5. `D:\AI\Claude code\NetAccel\docs\16_06_托管客户端验收与收尾手册.md`
6. `.plan16\WP-05-result.md`
7. `.plan16\WP-06A-result.md`
8. `.plan16\WP-06B-result.md`

项目定位必须保持：

- NetAccel 是个人学习 demo，不是机场、套餐、付费订阅、公共节点池或流量售卖。
- Plan 16 第一阶段保留 v2rayN 原生本地节点、通用订阅、导入导出、备份恢复，用于兼容、迁移和测试。
- 不能把经典能力失败解释为“以后会移除”。经典退场属于未来计划 17，不属于 WP-06。
- NetAccel 托管线路只读、独立来源、独立存储，不得进入经典 SQLite、订阅、分享、导出、备份或恢复链路。
- 用户只能在 Master 给当前账号/实例分配的线路集合内选择。

验收范围：

WP-06 对应 T16-R2-16 至 R2-22：

- T16-R2-16：托管与原生 SQLite 隔离
- T16-R2-17：编辑/删除/复制策略守卫
- T16-R2-18：分享/导出策略守卫
- T16-R2-19：备份恢复隔离
- T16-R2-20：经典模式启动器
- T16-R2-21：经典热键和托盘所有权守卫
- T16-R2-22：统一退出清理

不要进入：

- WP-07 / R3 UI
- 托管最终壳 UI
- 经典模式 UI 清理
- 图标、托盘视觉、页面重构
- 真实 Windows 管理员 TUN E2E
- 真实 Master / VLESS Reality / Hysteria2 E2E
- 删除、隐藏、禁用原生本地节点、通用订阅、导入导出或备份恢复

必须检查的合并矩阵：

| 项目 | 期望 |
|------|------|
| 托管 ProfileItem | 不写入 `SubItem/ProfileItem`，`IndexId` 使用 `managed:` 仅作为内存/防线标记 |
| AddServerCommon | 拒绝 `managed:` |
| RemoveServers | 拒绝 `managed:` |
| CopyServer | 拒绝 `managed:` |
| EditCustomServer / ViewModel edit | 拒绝托管来源 |
| Share / FmtHandler / InnerFmt | 托管来源无法生成链接、二维码、剪贴板或批量导出 |
| Backup ZIP | 不包含 managed secrets/cache/envelope/key/token 或 `managed-*` / `netaccel-credential-*` 文件 |
| Restore ZIP | 跳过托管文件，并清理恢复后 DB 中的 `managed:*` ProfileItem/SubItem |
| Managed owner active | 经典 Reload/F5、系统代理热键、StatusBar proxy、TUN toggle 不能启动第二核心或覆盖系统网络状态 |
| Classic handoff | Managed -> Classic 前停止托管连接、恢复系统状态、释放 Managed owner、持有 Classic owner |
| Classic owner held | Managed acquire/start 应被 owner coordinator 阻断 |
| AppExitAsync / SessionEnding / tray exit | 调用统一清理，幂等，尽力恢复 proxy/TUN，释放 owner |
| 经典本地节点 | 添加、编辑、复制、分享、导出仍可用 |
| 通用订阅 | 添加、更新、导入仍可用 |
| 经典备份恢复 | 仍可用，但不包含托管数据 |

重点审查点：

1. `ManagedConnectionGuard` 是否在真实托管运行时初始化时被注册，而不是只在测试中注册。
2. `ClassicModeLauncher` 是否持有 Classic lease；不能只 acquire 后立即 release。
3. `ManagedExitCleanup` 是否幂等，是否不受 caller cancellation token 中断。
4. backup/restore 过滤是否与当前托管文件位置、命名策略一致；若未来缓存位于 NetAccel 独立应用数据目录，记录为设计前提。
5. static callback 是否会污染测试；如果需要，测试中应清理 callback。
6. 直接调用 handler/ViewModel/event path 是否也被守卫，不能只隐藏 UI 菜单。

允许的修复：

- 只修 WP-06 范围内的阻断问题。
- 可以补测试、修接线、修 owner lifetime、修 backup/restore 漏洞、修文档结果。
- 不做无关重构，不改 UI，不删经典功能。

验证命令：

使用 `C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe`：

```powershell
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' build .\NetAccel.Managed\NetAccel.Managed.csproj -c Debug
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' build .\v2rayN\v2rayN\v2rayN.csproj -c Debug
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj -c Debug --filter "FullyQualifiedName~ConnectionOwnershipCoordinatorTests|FullyQualifiedName~ManagedConnectionCoordinatorTests|FullyQualifiedName~ManagedProfileGuardTests|FullyQualifiedName~ClassicModeLauncherTests|FullyQualifiedName~ManagedExitCleanupTests"
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\v2rayN\ServiceLib.Tests\ServiceLib.Tests.csproj -c Debug
& 'C:\Users\huangtingyang\.dotnet-codex-sdk-10\dotnet.exe' test .\NetAccel.Managed.Tests\NetAccel.Managed.Tests.csproj -c Debug --no-build
git diff --check
git status --short
```

交付：

新增或更新 `.plan16\WP-06-overall-acceptance-result.md`，必须包含：

- `Status: accepted` / `changes_requested` / `blocked`
- 本次是否有代码修复
- 若有修复，列出文件和原因
- WP-06 合并保护矩阵结果
- 经典兼容能力回归摘要
- 运行命令和结果
- 未执行项及原因
- 风险/限制
- `git status --short` 摘要

验收结论要求：

- 不能写“基本完成”“应该没问题”。
- 若所有代码级验收通过，但真实 Windows 管理员/TUN/GUI E2E 不可执行，可标记为 `accepted`，并在“未执行项”中记录实机门禁。
- 若发现托管数据可导出、可备份、可进入经典 SQLite，或 Managed/Classic 可双核心并发，必须标记 `changes_requested` 并修复或明确阻断原因。
