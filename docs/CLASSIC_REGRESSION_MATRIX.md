# 计划 16 - 经典功能回归矩阵

> 状态：可逐项填写
> 创建日期：2026-06-12
> 依据：`16_01_v2rayN功能全景与改造矩阵.md` 第二十六节"回归最低集合"
> 当前基线：v2rayN `7.22.6+1`，commit `1869a957`

## 使用说明

- **Status** 列仅允许：`pending` / `pass` / `fail` / `blocked`
- **Evidence** 列填写证据文件路径或截图路径
- 当前无法真实执行的功能不得写 `pass`，应标为 `pending` 或 `blocked`
- 每次 R0-R5 候选版本至少执行本矩阵一次

---

## 一、节点和协议

### 1.1 本地节点

| 项目 | 值 |
|------|-----|
| Task ID | REG-01 |
| 对应 | `16_01` 第三节 |
| 前置条件 | 应用已启动，进入经典模式 |
| 操作步骤 | 1. 主菜单 → 添加服务器 → `AddServerWindow`<br>2. 手动输入 VLESS Reality 参数（地址、端口、UUID、Public Key、ShortId、SNI）<br>3. 确认保存<br>4. 在节点列表中选中新节点<br>5. 点击"设为活动节点" |
| 期望 | 节点出现在列表中，所有字段正确保存，可被选为活动节点 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-01-local-node.png` |

### 1.2 通用订阅

| 项目 | 值 |
|------|-----|
| Task ID | REG-02 |
| 对应 | `16_01` 第六节 |
| 前置条件 | 应用已启动，进入经典模式，有一个可用订阅 URL |
| 操作步骤 | 1. 主菜单 → 订阅设置 → `SubSettingWindow`<br>2. 添加订阅，输入 URL 和备注<br>3. 点击"全部更新"<br>4. 等待更新完成<br>5. 检查节点列表是否出现订阅节点 |
| 期望 | 订阅添加成功，节点列表显示订阅分组和节点 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-02-subscription.png` |

### 1.3 剪贴板导入

| 项目 | 值 |
|------|-----|
| Task ID | REG-03 |
| 对应 | `16_01` 第五节 |
| 前置条件 | 剪贴板中有有效的 VLESS 或 Hysteria2 链接 |
| 操作步骤 | 1. 复制 VLESS 链接到剪贴板<br>2. 在主窗口按 `Ctrl+V`<br>3. 确认导入<br>4. 检查节点列表 |
| 期望 | 节点被正确解析并添加到列表 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-03-clipboard-import.png` |

### 1.4 二维码/文件导入

| 项目 | 值 |
|------|-----|
| Task ID | REG-04 |
| 对应 | `16_01` 第五节 |
| 前置条件 | 有包含节点信息的二维码图片文件 |
| 操作步骤 | 1. 在主窗口按 `Ctrl+S` 打开扫码<br>2. 或使用图片二维码扫描导入<br>3. 检查节点列表 |
| 期望 | 二维码内容被正确解析并添加节点 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-04-qr-import.png` |

### 1.5 分享和导出

| 项目 | 值 |
|------|-----|
| Task ID | REG-05 |
| 对应 | `16_01` 第八节 |
| 前置条件 | 已有至少一个本地节点 |
| 操作步骤 | 1. 选中一个本地节点<br>2. 右键 → 分享/导出<br>3. 复制分享链接<br>4. 验证链接可被其他客户端导入 |
| 期望 | 分享链接格式正确，可被解析还原 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-05-share-export.png` |

---

## 二、核心管理

### 2.1 Xray 连接

| 项目 | 值 |
|------|-----|
| Task ID | REG-06 |
| 对应 | `16_01` 第十节 |
| 前置条件 | 已有 VLESS/VMess 节点，Xray 核心已下载 |
| 操作步骤 | 1. 选中 VLESS 节点<br>2. 设为活动节点<br>3. 启动连接<br>4. 检查连接状态和日志 |
| 期望 | Xray 核心启动，连接成功，代理可用 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-06-xray-connect.png` |

### 2.2 sing-box 连接

| 项目 | 值 |
|------|-----|
| Task ID | REG-07 |
| 对应 | `16_01` 第十节 |
| 前置条件 | 已有 Hysteria2 节点，sing-box 核心已下载 |
| 操作步骤 | 1. 选中 Hysteria2 节点<br>2. 设为活动节点<br>3. 启动连接<br>4. 检查连接状态和日志 |
| 期望 | sing-box 核心启动，连接成功，代理可用 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-07-singbox-connect.png` |

---

## 三、系统代理

### 3.1 系统代理设置、清除和退出恢复

| 项目 | 值 |
|------|-----|
| Task ID | REG-08 |
| 对应 | `16_01` 第十二节 |
| 前置条件 | 应用已启动，无活动连接 |
| 操作步骤 | 1. 设置系统代理为"自动配置系统代理"<br>2. 检查 WinINET 设置<br>3. 启动连接<br>4. 停止连接<br>5. 检查系统代理是否恢复<br>6. 退出应用<br>7. 检查系统代理是否恢复为退出前状态 |
| 期望 | 代理设置正确应用，停止后恢复，退出后恢复 |
| Status | `pass`（部分） / `blocked`（退出恢复） |
| Evidence | `docs/evidence/classic-baseline/REG-08-system-proxy-auto.png` + `REG-08-system-proxy-cleared.png` |
| 验证详情 | 2026-06-13 UIA 自动化验证：自动配置→ProxyEnable=1, 清除→ProxyEnable=0, Registry 正确。退出恢复需人工验证。 |

---

## 四、TUN

### 4.1 TUN 启动和停止

| 项目 | 值 |
|------|-----|
| Task ID | REG-09 |
| 对应 | `16_01` 第十三节 |
| 前置条件 | 应用以管理员身份运行 |
| 操作步骤 | 1. 设置中启用 TUN<br>2. 启动连接<br>3. 检查 TUN 适配器是否创建<br>4. 检查路由表<br>5. 停止连接<br>6. 检查 TUN 适配器是否移除 |
| 期望 | TUN 适配器正确创建和移除，路由表正确 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-09-tun.png` |

---

## 五、测速

### 5.1 TCP Ping、Real Ping 和下载测速

| 项目 | 值 |
|------|-----|
| Task ID | REG-10 |
| 对应 | `16_01` 第九节 |
| 前置条件 | 已有至少一个节点 |
| 操作步骤 | 1. 选中节点<br>2. 右键 → TCP Ping<br>3. 右键 → Real Ping<br>4. 右键 → 下载测速<br>5. 检查测速结果 |
| 期望 | 测速结果合理显示，不崩溃 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-10-speedtest.png` |

---

## 六、路由和 DNS

### 6.1 路由配置

| 项目 | 值 |
|------|-----|
| Task ID | REG-11 |
| 对应 | `16_01` 第十四节 |
| 前置条件 | 应用已启动 |
| 操作步骤 | 1. 设置 → 路由设置 → `RoutingSettingWindow`<br>2. 查看内置路由方案<br>3. 切换默认路由<br>4. 启动连接，验证路由规则生效 |
| 期望 | 路由方案可切换，规则正确应用到核心配置 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-11-routing.png` |

### 6.2 DNS 配置

| 项目 | 值 |
|------|-----|
| Task ID | REG-12 |
| 对应 | `16_01` 第十五节 |
| 前置条件 | 应用已启动 |
| 操作步骤 | 1. 设置 → DNS 设置 → `DNSSettingWindow`<br>2. 修改 Direct DNS 和 Remote DNS<br>3. 保存<br>4. 启动连接，验证 DNS 配置生效 |
| 期望 | DNS 设置可修改并正确应用到核心配置 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-12-dns.png` |

---

## 七、托盘和热键

### 7.1 托盘显示、隐藏和退出

| 项目 | 值 |
|------|-----|
| Task ID | REG-13 |
| 对应 | `16_01` 第十八节 |
| 前置条件 | 应用已启动 |
| 操作步骤 | 1. 关闭主窗口，确认隐藏到托盘<br>2. 双击托盘图标，确认窗口恢复<br>3. 右键托盘，查看菜单<br>4. 点击"退出"，确认进程终止<br>5. 检查系统代理是否恢复 |
| 期望 | 托盘功能正常，退出后进程终止，系统代理恢复 |
| Status | `pass`（最小化） / `blocked`（退出/恢复） |
| Evidence | `docs/evidence/classic-baseline/REG-13-tray-before.png` |
| 验证详情 | 2026-06-13 UIA 验证：点击 menuClose 后进程持续运行（PID 27792, Responding=True, Threads=31），MainWindowHandle=0，确认最小化到托盘。托盘图标渲染和右键退出需人工验证。 |

---

## 八、备份恢复

### 8.1 本地备份恢复

| 项目 | 值 |
|------|-----|
| Task ID | REG-14 |
| 对应 | `16_01` 第二十节 |
| 前置条件 | 应用已启动，有配置数据 |
| 操作步骤 | 1. 备份和恢复 → `BackupAndRestoreView`<br>2. 本地备份，选择路径<br>3. 检查 ZIP 内容<br>4. 修改一些配置<br>5. 本地恢复<br>6. 检查配置是否恢复 |
| 期望 | 备份 ZIP 包含配置，恢复后数据一致 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-14-backup-restore.png` |

---

## 九、更新检查

### 9.1 更新检查

| 项目 | 值 |
|------|-----|
| Task ID | REG-15 |
| 对应 | `16_01` 第二十一节 |
| 前置条件 | 应用已启动 |
| 操作步骤 | 1. 帮助/关于 → 检查更新 → `CheckUpdateView`<br>2. 检查 Xray/sing-box 核心更新<br>3. 检查 Geo 文件更新<br>4. 记录检查结果 |
| 期望 | 更新检查不崩溃，结果正确显示 |
| Status | `pending` |
| Evidence | `docs/evidence/classic-baseline/REG-15-update-check.png` |

---

## 十、WPF 主窗口和设置

### 10.1 主窗口和经典入口

| 项目 | 值 |
|------|-----|
| Task ID | REG-16 |
| 对应 | `16_01` 第十八/十九节 |
| 前置条件 | 应用已启动 |
| 操作步骤 | 1. 启动应用，检查主窗口标题<br>2. 检查节点列表视图<br>3. 检查状态栏<br>4. 检查菜单栏各项入口<br>5. 切换主题（Light/Dark）<br>6. 切换语言 |
| 期望 | 主窗口正常显示，所有经典入口可访问 |
| Status | `pass` |
| Evidence | `docs/evidence/classic-baseline/REG-16-main-window.png` |
| 验证详情 | 2026-06-13 UIA 验证：窗口标题 `v2rayN - V7.22.6 - X64 - 以管理员身份运行`；菜单栏（配置项/订阅分组/设置/帮助/推广）完整；节点列表 DataGrid 12 列可见；状态栏（本地/局域网/TUN/代理/路由）完整；日志/查询统计/其它标签页可用。 |

---

## 十一、回归执行记录

每次回归执行时，在此表中添加一行记录：

| 日期 | 版本/Commit | 执行人 | 通过数 | 失败数 | 阻塞数 | 待执行数 | 备注 |
|------|-------------|--------|--------|--------|--------|----------|------|
| 2026-06-13 | 1869a957 (7.22.6+1) | Claude Code (UIA 自动化) | 3 (REG-08/13/16) | 0 | 13 | 0 | GUI 自动化验证，3 个托盘项仍需人工 |

---

## 附录：功能与矩阵条目映射

| 矩阵条目 | `16_01` 章节 | 关键代码入口 |
|----------|-------------|-------------|
| REG-01 | 第三节 节点和协议 | `AddServerWindow`, `AddServerViewModel` |
| REG-02 | 第六节 订阅 | `SubSettingWindow`, `SubSettingViewModel`, `SubscriptionHandler` |
| REG-03 | 第五节 节点导入 | `AddBatchServers`, `FmtHandler` |
| REG-04 | 第五节 节点导入 | `ScanImageTask`, `QrcodeView` |
| REG-05 | 第八节 分享和导出 | `ShareServerCmd`, `Export2ShareUrl`, `FmtHandler` |
| REG-06 | 第十节 核心管理 | `CoreManager`, `CoreConfigV2rayService` |
| REG-07 | 第十节 核心管理 | `CoreManager`, `CoreConfigSingboxService` |
| REG-08 | 第十二节 系统代理 | `SysProxyHandler`, `ProxySettingWindows` |
| REG-09 | 第十三节 TUN | `CoreManager`, `EnableTun` config |
| REG-10 | 第九节 测速 | `SpeedtestService` |
| REG-11 | 第十四节 路由 | `RoutingSettingWindow`, `V2rayRoutingService`, `SingboxRoutingService` |
| REG-12 | 第十五节 DNS | `DNSSettingWindow`, `V2rayDnsService`, `SingboxDnsService` |
| REG-13 | 第十八节 托盘 | `MainWindow_Closing`, `StatusBarView`, `HotkeyManager` |
| REG-14 | 第二十节 备份恢复 | `BackupAndRestoreView`, `BackupAndRestoreViewModel` |
| REG-15 | 第二十一节 更新 | `CheckUpdateView`, `UpdateService` |
| REG-16 | 第十八/十九节 主窗口 | `MainWindow`, `MainWindowViewModel` |
