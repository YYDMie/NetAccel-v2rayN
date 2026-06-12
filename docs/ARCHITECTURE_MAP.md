# 计划 16 - 客户端知识图谱

> 状态：初始基线
> 创建日期：2026-06-12
> 基线 commit：`1869a95700e17369f071ed23c8c485c2c3e83a1d`（`7.22.6+1`）
> 分析文件数：覆盖 WPF 项目、ServiceLib、ServiceLib.Tests

## 一、项目结构总览

```
NetAccel-v2rayN/
├── LICENSE                          # GPL-3.0
├── README.md
├── global.json                      # .NET SDK 10.0.301 锁定
├── docs/                            # NetAccel 文档（计划 16）
├── scripts/                         # NetAccel 脚本
├── .plan16/                         # 计划 16 状态和证据
└── v2rayN/                          # 解决方案根目录
    ├── v2rayN.sln                   # 解决方案文件
    ├── Directory.Build.props        # 通用构建属性（版本 7.22.6）
    ├── Directory.Packages.props     # NuGet 包版本集中管理
    ├── v2rayN/                      # WPF 主项目（net10.0-windows）
    │   ├── v2rayN.csproj
    │   ├── App.xaml(.cs)            # 应用入口
    │   ├── Views/                   # WPF 视图层
    │   ├── ViewModels/              # WPF 特有 ViewModel（仅 ThemeSettingViewModel）
    │   ├── Manager/                 # WPF 管理器（HotkeyManager, WindowsManager）
    │   ├── Base/                    # WindowBase, MyDGTextColumn
    │   ├── Converters/              # WPF 值转换器
    │   ├── Common/                  # WPF 通用工具
    │   └── Resources/               # 图标、图片资源
    ├── v2rayN.Desktop/              # Avalonia 跨平台项目（不修改）
    ├── ServiceLib/                  # 共享业务逻辑库
    │   ├── ServiceLib.csproj
    │   ├── Global.cs                # 全局常量和配置（CoreUrls 等）
    │   ├── ViewModels/              # 共享 ViewModel 层
    │   ├── Handler/                 # 数据处理器
    │   ├── Services/                # 业务服务
    │   ├── Manager/                 # 核心管理器
    │   ├── Models/                  # 数据模型
    │   ├── Enums/                   # 枚举定义
    │   ├── Base/                    # MyReactiveObject 基类
    │   ├── Common/                  # 通用工具
    │   ├── Events/                  # 事件定义
    │   ├── Helper/                  # 辅助工具
    │   ├── Resx/                    # 本地化资源
    │   └── Sample/                  # 示例数据
    ├── ServiceLib.Tests/            # 单元测试项目
    ├── ServiceLib.UdpTest/          # UDP 测试工具
    ├── GlobalHotKeys/               # 子模块（全局热键）
    └── AmazTool/                    # 打包工具
```

## 二、WPF 应用入口链

```
v2rayN/v2rayN/App.xaml.cs
  └── App 入口
        ├── MainWindow (主窗口)
        │   └── v2rayN/v2rayN/Views/MainWindow.xaml(.cs)
        └── 初始化 → AppManager
```

### 关键文件

| 文件 | 类型 | 路径 |
|------|------|------|
| App 入口 | `App` | `v2rayN/v2rayN/App.xaml.cs` |
| 主窗口 XAML | `MainWindow` | `v2rayN/v2rayN/Views/MainWindow.xaml` |
| 主窗口代码 | `MainWindow` | `v2rayN/v2rayN/Views/MainWindow.xaml.cs` |
| 窗口基类 | `WindowBase` | `v2rayN/v2rayN/Base/WindowBase.cs` |

## 三、WPF 视图层 (Views)

```
v2rayN/v2rayN/Views/
├── MainWindow.xaml(.cs)              # 主窗口：节点列表、状态栏、菜单
├── ProfilesView.xaml(.cs)            # 节点列表视图
├── ProfilesSelectWindow.xaml(.cs)    # 节点选择窗口
├── StatusBarView.xaml(.cs)           # 状态栏（连接状态、速度、操作按钮）
├── MsgView.xaml(.cs)                 # 日志消息视图
├── SubSettingWindow.xaml(.cs)        # 订阅管理
├── SubEditWindow.xaml(.cs)           # 订阅编辑
├── AddServerWindow.xaml(.cs)         # 手动添加节点
├── AddServer2Window.xaml(.cs)        # 自定义核心添加
├── AddGroupServerWindow.xaml(.cs)    # 批量添加/分组
├── RoutingSettingWindow.xaml(.cs)    # 路由方案设置
├── RoutingRuleSettingWindow.xaml(.cs)# 路由规则编辑
├── RoutingRuleDetailsWindow.xaml(.cs)# 路由规则详情
├── DNSSettingWindow.xaml(.cs)        # DNS 设置
├── OptionSettingWindow.xaml(.cs)     # 选项设置（端口、代理等）
├── ThemeSettingView.xaml(.cs)        # 主题设置
├── CheckUpdateView.xaml(.cs)         # 更新检查
├── BackupAndRestoreView.xaml(.cs)    # 备份恢复
├── GlobalHotkeySettingWindow.xaml(.cs)# 全局热键设置
├── FullConfigTemplateWindow.xaml(.cs)# 完整配置模板
├── ClashProxiesView.xaml(.cs)        # Clash 代理视图
├── ClashConnectionsView.xaml(.cs)    # Clash 连接视图
└── QrcodeView.xaml(.cs)             # 二维码扫描
```

## 四、ViewModel 层

### 4.1 ServiceLib 共享 ViewModels

```
v2rayN/ServiceLib/ViewModels/
├── MainWindowViewModel.cs      # 主窗口核心逻辑（节点管理、连接控制、菜单命令）
├── ProfilesViewModel.cs        # 节点列表逻辑
├── ProfilesSelectViewModel.cs  # 节点选择逻辑
├── StatusBarViewModel.cs       # 状态栏逻辑
├── MsgViewModel.cs             # 日志消息逻辑
├── SubSettingViewModel.cs      # 订阅管理逻辑
├── SubEditViewModel.cs         # 订阅编辑逻辑
├── AddServerViewModel.cs       # 添加节点逻辑
├── AddServer2ViewModel.cs      # 自定义核心添加
├── AddGroupServerViewModel.cs  # 批量添加
├── RoutingSettingViewModel.cs  # 路由设置逻辑
├── RoutingRuleSettingViewModel.cs
├── RoutingRuleDetailsViewModel.cs
├── DNSSettingViewModel.cs      # DNS 设置逻辑
├── OptionSettingViewModel.cs   # 选项设置
├── CheckUpdateViewModel.cs     # 更新检查逻辑
├── BackupAndRestoreViewModel.cs# 备份恢复逻辑
├── GlobalHotkeySettingViewModel.cs
├── FullConfigTemplateViewModel.cs
├── ClashProxiesViewModel.cs    # Clash 代理视图
└── ClashConnectionsViewModel.cs
```

### 4.2 WPF 特有 ViewModels

```
v2rayN/v2rayN/ViewModels/
└── ThemeSettingViewModel.cs    # WPF 主题设置（Avalonia 版本不共享）
```

## 五、核心管理器层 (Manager)

```
v2rayN/ServiceLib/Manager/
├── AppManager.cs               # 应用生命周期管理（初始化、退出、清理）
├── CoreManager.cs              # 核心进程管理（启动/停止 Xray/sing-box/mihomo）
├── CoreInfoManager.cs          # 核心信息（版本、路径、下载 URL）
├── CoreAdminManager.cs         # 核心管理 API
├── ConfigHandler.cs → Handler  # 配置处理（注意：实际在 Handler 目录）
├── StatisticsManager.cs        # 流量统计
├── TaskManager.cs              # 定时任务（自动更新、自动清理等）
├── NoticeManager.cs            # 通知管理
├── PacManager.cs               # PAC 文件管理
├── ProfileExManager.cs         # 节点扩展信息（排序、延迟、速度）
├── GroupProfileManager.cs      # 分组管理
├── WebDavManager.cs            # WebDAV 备份
├── ClashApiManager.cs          # Clash API 管理
└── CertPemManager.cs           # 证书管理

v2rayN/v2rayN/Manager/
├── HotkeyManager.cs            # 全局热键注册和处理
└── WindowsManager.cs           # Windows 窗口管理
```

## 六、数据处理器层 (Handler)

```
v2rayN/ServiceLib/Handler/
├── ConfigHandler.cs            # 配置读写（SQLite CRUD，SubItem/ProfileItem/RoutingItem 等）
├── CoreConfigHandler.cs        # 核心配置生成入口
├── ConnectionHandler.cs        # 连接处理（IP 信息、出口检测）
├── SubscriptionHandler.cs      # 订阅更新（下载、解析、保存）
├── AutoStartupHandler.cs       # 开机自启动
│
├── Builder/                    # 配置构建器
│   ├── CoreConfigContextBuilder.cs  # 核心配置上下文构建（关键！读取 Routing/DNS/TUN 等）
│   └── NodeValidator.cs             # 节点校验
│
├── Fmt/                        # 格式解析器（链接 → 节点）
│   ├── FmtHandler.cs           # 格式处理入口
│   ├── BaseFmt.cs              # 基类
│   ├── VLESSFmt.cs             # VLESS 链接解析
│   ├── VmessFmt.cs             # VMess 链接解析
│   ├── Hysteria2Fmt.cs         # Hysteria2 链接解析
│   ├── TrojanFmt.cs            # Trojan 链接解析
│   ├── ShadowsocksFmt.cs       # SS 链接解析
│   ├── V2rayFmt.cs             # v2ray 通用格式
│   ├── SingboxFmt.cs           # sing-box 格式
│   ├── ClashFmt.cs             # Clash 格式
│   ├── WireguardFmt.cs         # WireGuard 格式
│   ├── TuicFmt.cs              # TUIC 格式
│   ├── NaiveFmt.cs             # Naive 格式
│   ├── AnytlsFmt.cs            # AnyTLS 格式
│   ├── SocksFmt.cs             # SOCKS 格式
│   ├── InnerFmt.cs             # 内部 URI 格式
│   └── HtmlPageFmt.cs          # HTML 页面解析
│
└── SysProxy/                   # 系统代理
    ├── SysProxyHandler.cs      # 系统代理设置入口
    ├── ProxySettingWindows.cs  # Windows 代理设置（WinINET）
    ├── ProxySettingLinux.cs    # Linux 代理设置
    └── ProxySettingOSX.cs      # macOS 代理设置
```

## 七、业务服务层 (Services)

```
v2rayN/ServiceLib/Services/
├── CoreConfig/                 # 核心配置生成
│   ├── V2ray/                  # Xray/v2ray 配置生成
│   │   ├── CoreConfigV2rayService.cs    # 入口
│   │   ├── V2rayOutboundService.cs      # 出站配置
│   │   ├── V2rayInboundService.cs       # 入站配置
│   │   ├── V2rayRoutingService.cs       # 路由配置
│   │   ├── V2rayDnsService.cs           # DNS 配置
│   │   ├── V2rayLogService.cs           # 日志配置
│   │   ├── V2rayConfigTemplateService.cs# 模板
│   │   ├── V2rayBalancerService.cs      # 负载均衡
│   │   └── V2rayStatisticService.cs     # 统计配置
│   └── Singbox/                # sing-box 配置生成
│       ├── CoreConfigSingboxService.cs  # 入口
│       ├── SingboxOutboundService.cs
│       ├── SingboxInboundService.cs
│       ├── SingboxRoutingService.cs
│       ├── SingboxDnsService.cs
│       ├── SingboxLogService.cs
│       ├── SingboxConfigTemplateService.cs
│       ├── SingboxRulesetService.cs
│       └── SingboxStatisticService.cs
│
├── SpeedtestService.cs         # 测速服务（TCP Ping/Real Ping/下载测速）
├── DownloadService.cs          # 下载服务（核心下载、Geo 文件下载）
├── UpdateService.cs            # 更新服务（GUI 更新、核心更新、Geo 更新）
├── ProcessService.cs           # 进程管理服务
├── WindowsJobService.cs        # Windows Job Object（子进程回收）
│
└── Statistics/                 # 统计服务
    ├── StatisticsXrayService.cs
    └── StatisticsSingboxService.cs
```

## 八、数据模型层 (Models)

```
v2rayN/ServiceLib/Models/
├── Entities/                   # SQLite 实体
│   ├── ProfileItem.cs          # 节点配置（地址、端口、协议、UUID 等）
│   ├── SubItem.cs              # 订阅信息（URL、备注、UA、过滤器）
│   ├── RoutingItem.cs          # 路由方案
│   ├── DNSItem.cs              # DNS 配置
│   ├── RulesItem.cs            # 路由规则
│   ├── ServerStatItem.cs       # 节点统计（流量、延迟）
│   ├── ProfileExItem.cs        # 节点扩展（排序、延迟、速度）
│   ├── ProfileGroupItem.cs     # 分组
│   ├── FullConfigTemplateItem.cs # 完整配置模板
│   ├── ProtocolExtraItem.cs    # 协议扩展参数
│   └── TransportExtraItem.cs   # 传输层扩展参数
│
├── Configs/                    # 配置模型
│   ├── Config.cs               # 全局配置（活动节点、端口、代理模式等）
│   └── ConfigItems.cs          # 配置项定义
│
├── CoreConfigs/                # 核心配置模型
│   └── （Xray/sing-box 配置结构）
│
└── Dto/                        # 数据传输对象
    ├── ProfileItemModel.cs     # 节点显示模型
    ├── RoutingItemModel.cs     # 路由显示模型
    ├── RoutingTemplate.cs      # 路由模板
    ├── RulesItemModel.cs       # 规则显示模型
    ├── CheckUpdateModel.cs     # 更新检查模型
    ├── GitHubRelease.cs        # GitHub Release 信息
    ├── SpeedTestResult.cs      # 测速结果
    ├── ServerTestItem.cs       # 测试项
    ├── ServerSpeedItem.cs      # 速度项
    ├── ClashProxyModel.cs      # Clash 代理模型
    ├── ClashConnectionModel.cs # Clash 连接模型
    ├── VmessQRCode.cs          # VMess 二维码
    ├── SsSIP008.cs             # SIP008 格式
    ├── CmdItem.cs              # 命令项
    ├── ComboItem.cs            # 下拉选项
    ├── RetResult.cs            # 返回结果
    ├── SemanticVersion.cs      # 语义版本
    └── IPAPIInfo.cs            # IP API 信息
```

## 九、枚举定义 (Enums)

```
v2rayN/ServiceLib/Enums/
├── EConfigType.cs      # 协议类型（VMess/VLESS/Trojan/SS/Hysteria2/TUIC/WireGuard/AnyTLS/Naive/SOCKS/HTTP/Custom/PolicyGroup/ProxyChain）
├── ECoreType.cs        # 核心类型（Xray/sing-box/mihomo/v2fly/Hysteria/Naive/TUIC）
├── ESysProxyType.cs    # 系统代理类型（ForcedClear/ForcedChange/Unchanged/Pac）
├── ESpeedActionType.cs # 测速类型（Tcping/Realping/FastRealping/UdpTest/Speedtest/Mixedtest）
├── ERuleMode.cs        # 路由模式（Rule/Direct/Global）
├── ERuleType.cs        # 路由规则类型
├── ETransport.cs       # 传输类型（tcp/kcp/ws/grpc/httpupgrade/xhttp）
├── EGlobalHotkey.cs    # 全局热键类型
├── ETheme.cs           # 主题类型（Light/Dark/System）
├── EInboundProtocol.cs # 入站协议
├── EMove.cs            # 移动方向
├── EMultipleLoad.cs    # 负载策略
├── EPresetType.cs      # 预设类型
├── EServerColName.cs   # 服务器列名
├── EGirdOrientation.cs # 网格方向
└── EViewAction.cs      # 视图操作
```

## 十、核心调用链

### 10.1 连接流程

```
MainWindowViewModel.Reload()
  → CoreConfigContextBuilder.BuildAll(_config, profileItem)
      → 内部调用 Build() 读取 Config/ProfileItem/RoutingItem/DNSItem/全局设置
      → 返回 CoreConfigContextBuilderAllResult { MainResult, PreSocksResult }
  → CoreManager.Instance.LoadCore(mainContext, preContext)
      → 启动核心进程
  → SysProxyHandler.UpdateSysProxy(_config, false)
      → 设置系统代理（在 LoadCore 返回后由 MainWindowViewModel 调用）
```

> **注意：** `SysProxyHandler.UpdateSysProxy` 由 `MainWindowViewModel` 在 `LoadCore` 返回后
> 调用，不是由 `CoreManager` 内部调用。调用链为：
> `MainWindowViewModel.Reload()` → `CoreConfigContextBuilder.BuildAll()` → `CoreManager.LoadCore()` → `SysProxyHandler.UpdateSysProxy()`。

### 10.2 订阅更新流程

```
SubSettingViewModel.SubUpdateCmd
  → SubscriptionHandler.UpdateSub()
      → DownloadService.Download() (下载订阅内容)
      → FmtHandler.Parse() (解析链接格式)
      → ConfigHandler.AddBatchServers() (保存到 SQLite)
```

### 10.3 导入流程

```
MainWindow (Ctrl+V 剪贴板)
  → AddBatchServers
      → FmtHandler.Parse() (自动识别格式)
      → ConfigHandler.AddBatchServers() (保存)
```

### 10.4 测速流程

```
MainWindowViewModel (测速命令)
  → SpeedtestService
      → Tcping / Realping / Speedtest
      → 更新 ProfileExItem (延迟/速度)
      → 更新 UI
```

## 十一、托管改造关键接入点（计划 16 新增，尚未实现）

以下为计划 16 托管模块需要接入的关键位置：

| 接入点 | 当前文件 | 改造用途 |
|--------|----------|----------|
| 配置构建 | `CoreConfigContextBuilder.cs` | 托管模式注入独立 RuntimeConfig，不读经典 SQLite |
| 核心启动 | `CoreManager.cs` | ConnectionOwnershipCoordinator 控制启动权 |
| 系统代理 | `SysProxyHandler.cs` | 托管模式由 Coordinator 管理代理设置 |
| 节点列表 | `ProfilesViewModel.cs` | 托管线路独立页面，不混入经典列表 |
| 订阅更新 | `SubscriptionHandler.cs` | 托管同步走 ManagedConfigSyncService |
| 导入解析 | `FmtHandler.cs` / `ConfigHandler.AddBatchServers` | 托管禁止调用 |
| 分享导出 | `ShareServerCmd` / `Export2ShareUrl` | 托管来源拒绝 |
| 备份恢复 | `BackupAndRestoreViewModel.cs` | 排除托管敏感数据 |
| 更新检查 | `CheckUpdateViewModel.cs` | 托管壳禁用上游更新入口 |
| 路由/DNS | `RoutingSettingViewModel.cs` / `DNSSettingViewModel.cs` | 托管策略独立 |

## 十二、测试覆盖

```
v2rayN/ServiceLib.Tests/
├── CoreConfig/
│   ├── Context/
│   │   └── CoreConfigContextBuilderTests.cs  # 配置上下文构建测试
│   └── （Xray/sing-box 配置生成测试）
├── Fmt/
│   └── （格式解析测试：FmtHandler、内部 URI、WireGuard 等）
└── GlobalUsings.cs
```

已知测试缺口（`16_01` 第二十五节）：
- WPF ViewModel 和页面行为
- 订阅更新和自动任务
- SQLite 数据迁移
- 系统代理和 TUN
- 更新安全
- 备份恢复隔离
- 托管身份、加密包、来源策略和会话
