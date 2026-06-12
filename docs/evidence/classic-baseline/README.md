# 计划 16 - 经典模式冻结基线截图证据清单

> 状态：部分完成（GUI 自动化 + 人工 blocked）
> 创建日期：2026-06-12
> 更新日期：2026-06-13
> 测试环境：Windows 11 x64, .NET 10.0.301, Release self-contained build
> 测试构建：`C:\tmp\plan16-gui\publish\NetAccel.exe` (WP-01B identity build)

## 一、截图目录结构

```
docs/evidence/classic-baseline/
├── README.md                           # 本文件
├── REG-01-settings.png                 # 参数设置对话框（CoreInfo/基础设置）
├── REG-08-system-proxy-auto.png        # 系统代理：自动配置系统代理
├── REG-08-system-proxy-cleared.png     # 系统代理：清除系统代理
├── REG-13-tray-before.png             # 托盘测试前主窗口状态
├── REG-16-main-window.png             # 主窗口完整截图
└── (blocked items 见下文)
```

## 二、命名规范

| 部分 | 格式 | 说明 |
|------|------|------|
| 前缀 | `REG-` | 固定前缀，表示回归矩阵条目 |
| 编号 | `01`-`16` | 对应 `CLASSIC_REGRESSION_MATRIX.md` 中的 REG-XX |
| 简述 | 小写英文短横线分隔 | 功能简述 |
| 后缀 | `.png` | 统一 PNG 格式 |

变体命名（同一功能多个截图）：
- `REG-06-xray-connect-before.png` — 连接前状态
- `REG-06-xray-connect-after.png` — 连接后状态
- `REG-06-xray-connect-log.png` — 日志截图

## 三、截图要求

1. **窗口完整**：截取整个应用窗口，不裁剪标题栏或状态栏
2. **关键状态可见**：节点列表、连接状态、速度信息等关键数据必须清晰可读
3. **分辨率**：至少 1920x1080，DPI 缩放 100%
4. **主题**：默认系统主题（Light 或 System）
5. **语言**：简体中文

## 四、测试结果总览

### R0-04 原生启动冒烟（GUI 部分）

| 检查项 | 状态 | 证据 |
|--------|------|------|
| Release self-contained 启动 | ✅ pass | PID 27792, 窗口标题 `v2rayN - V7.22.6 - X64 - 以管理员身份运行` |
| 主窗口完整渲染 | ✅ pass | `REG-16-main-window.png` — 菜单栏、节点列表、状态栏、日志标签页均可见 |
| 系统代理下拉框功能 | ✅ pass | UIA 验证 4 个选项：清除/自动配置/不改变/Pac 模式 |
| 系统代理 → 自动配置 | ✅ pass | `REG-08-system-proxy-auto.png` — Registry ProxyEnable=1, ProxyServer=127.0.0.1:10808 |
| 系统代理 → 清除 | ✅ pass | `REG-08-system-proxy-cleared.png` — Registry ProxyEnable=0 |
| 托盘最小化（关闭按钮） | ✅ pass | 点击 menuClose 后进程持续运行（PID 27792, Responding=True, 31 线程），MainWindowHandle=0 |
| 托盘图标渲染 | ⚠️ blocked | 需人工验证：CLI/UIA 无法截图系统托盘区域 |
| 托盘右键退出 | ⚠️ blocked | 需人工验证：UIA 无法定位 NotifyIcon 上下文菜单 |
| 系统代理退出恢复 | ⚠️ blocked | 依赖托盘正常退出流程 |

### R0-09 冻结经典模式基线截图

| REG 编号 | 功能 | 状态 | 证据 / 说明 |
|----------|------|------|-------------|
| REG-01 | 本地节点 | ⚠️ blocked | 需手动添加节点并截图（需 GUI 交互） |
| REG-02 | 通用订阅 | ⚠️ blocked | 需可用订阅 URL 和网络 |
| REG-03 | 剪贴板导入 | ⚠️ blocked | 需 GUI 交互和剪贴板操作 |
| REG-04 | 二维码/文件导入 | ⚠️ blocked | 需 GUI 交互和文件选择 |
| REG-05 | 分享和导出 | ⚠️ blocked | 需 GUI 交互和已有节点 |
| REG-06 | Xray 连接 | ⚠️ blocked | 需 Xray 核心下载和可用节点 |
| REG-07 | sing-box 连接 | ⚠️ blocked | 需 sing-box 核心下载和可用节点 |
| REG-08 | 系统代理 | ✅ pass | `REG-08-system-proxy-auto.png` + `REG-08-system-proxy-cleared.png` |
| REG-09 | TUN | ⚠️ blocked | 需管理员权限和 TUN 驱动 |
| REG-10 | 测速 | ⚠️ blocked | 需网络和可用节点 |
| REG-11 | 路由配置 | ⚠️ blocked | 需 GUI 交互（下拉框有 3 个路由配置） |
| REG-12 | DNS 配置 | ⚠️ blocked | 需 GUI 交互 |
| REG-13 | 托盘 | ✅ pass | 进程最小化后持续运行，MainWindowHandle=0，`REG-13-tray-before.png` |
| REG-14 | 备份恢复 | ⚠️ blocked | 需 GUI 交互 |
| REG-15 | 更新检查 | ⚠️ blocked | 需网络（WP-01B 已验证更新拦截逻辑） |
| REG-16 | 主窗口 | ✅ pass | `REG-16-main-window.png` |

### R0-11 隔离 NetAccel 应用身份（GUI 验证部分）

| 检查项 | 状态 | 证据 |
|--------|------|------|
| 窗口标题包含版本信息 | ✅ pass | `v2rayN - V7.22.6 - X64 - 以管理员身份运行` |
| 系统代理操作正常 | ✅ pass | Registry 验证：设置/清除均正确 |
| 菜单结构完整 | ✅ pass | UIA 验证：配置项/订阅分组/设置/帮助/重启服务/推广/关闭 |
| 设置对话框可打开 | ✅ pass | `REG-01-settings.png` — CoreInfo/基础设置/v2rayN设置/KCP设置/TcpFastOpen/预定义配置/关于 |
| 路由下拉框可用 | ✅ pass | 3 个路由配置项可见 |
| 系统代理下拉框可用 | ✅ pass | 4 个选项：清除/自动配置/不改变/Pac 模式 |
| TUN 开关可见 | ✅ pass | togEnableTun 按钮可见 |
| 单实例互斥 | ✅ pass | WP-00 已验证（SecondExited=True, CountAfterSecond=1） |

## 五、UI 自动化验证详情

### 5.1 主窗口 UI 元素（UIA Tree）

```
Window: v2rayN - V7.22.6 - X64 - 以管理员身份运行
├── ToolBar (PART_Toggle)
├── StatusBarView
│   ├── txtInboundDisplay: "本地:[mixed:10808]"
│   ├── txtInboundLanDisplay: "局域网:none"
│   ├── togEnableTun: "启用 Tun"
│   ├── cmbSystemProxy: "系统代理" (ComboBox, 4 items)
│   └── cmbRoutings2: "路由" (ComboBox, 3 items)
├── ProfilesView
│   ├── lstGroup: "订阅分组" (List)
│   ├── txtServerFilter: "过滤器，按回车执行"
│   └── DataGrid (列: 类型/别名/地址/端口/传输协议/TLS/订阅分组/延迟/速度/今日上传/IP信息/今日下载)
├── GridSplitter
└── TabControl (tabMain1)
    ├── 日志
    ├── 查询统计
    └── 其它
```

### 5.2 系统代理 Registry 验证

| 操作 | ProxyEnable | ProxyServer | 结果 |
|------|-------------|-------------|------|
| 初始状态 | 0 | 127.0.0.1:10808 | — |
| 自动配置系统代理 | 1 | 127.0.0.1:10808 | ✅ Registry 正确更新 |
| 清除系统代理 | 0 | 127.0.0.1:10808 | ✅ Registry 正确更新 |

### 5.3 托盘行为验证

| 检查项 | 结果 |
|--------|------|
| 点击 menuClose 前 | MainWindowHandle=27792, Title='v2rayN - V7.22.6...' |
| 点击 menuClose 后 | MainWindowHandle=0, Title='', 进程仍在运行 |
| 进程状态 | Responding=True, Threads=31 |
| 恢复方式 | 需人工双击托盘图标或重启进程 |

## 六、仍需人工验证的项目

以下项目因环境限制无法自动化验证：

| 项目 | 原因 | 人工验证步骤 |
|------|------|-------------|
| 托盘图标真实渲染 | UIA 无法截图系统托盘区域 | 启动后检查通知区域是否出现 v2rayN 图标 |
| 托盘右键菜单 | UIA 无法定位 NotifyIcon 上下文菜单 | 右键托盘 → 检查菜单项（主界面/退出） |
| 托盘正常退出 | 依赖托盘右键菜单交互 | 右键托盘 → 退出，确认进程全部终止 |
| 系统代理退出恢复 | 依赖托盘正常退出流程 | 退出后检查系统代理设置是否恢复为退出前状态 |
| 本地节点添加 | 需 GUI 交互和手动输入 | 添加一个 VMess/VLESS 节点并截图 |
| 订阅更新 | 需可用订阅 URL 和网络 | 添加订阅 URL → 更新订阅 → 截图 |
| Xray/sing-box 连接 | 需核心下载和可用节点 | 选择节点 → 连接 → 检查日志 → 截图 |
| TUN 启动 | 需管理员权限和 TUN 驱动 | 点击 TUN 开关 → 确认驱动安装 → 截图 |
| 测速 | 需网络和可用节点 | 选择节点 → TCP 测速 → 截图结果 |
| 路由/DNS 配置 | 需 GUI 交互 | 打开设置 → 路由/DNS → 截图 |
| 备份恢复 | 需 GUI 交互 | 设置 → 备份和还原 → 导出 → 截图 |
| 更新检查 | 需网络 | 帮助 → 检查更新 → 截图 |

## 七、测试执行记录

**测试时间：** 2026-06-13 00:50-01:00 UTC+8

**测试工具：**
- PowerShell 5.1 + UI Automation (UIAutomationClient/UIAutomationTypes)
- .NET System.Drawing (CopyFromScreen 截图)
- Windows Registry (HKCU\...\Internet Settings 验证代理)

**测试构建：**
- 来源：`C:\tmp\plan16-gui\publish\NetAccel.exe`
- 构建命令：`dotnet publish v2rayN/v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true`
- 基线 commit：`1869a95700e17369f071ed23c8c485c2c3e83a1d` (7.22.6+1)

**已执行的自动化测试：**
1. 进程启动和窗口标题验证
2. UIA 树遍历（菜单/工具栏/状态栏/数据网格/标签页）
3. 系统代理 Registry 读写验证（设置/清除）
4. 托盘最小化行为验证（menuClose → 进程存活/窗口隐藏）
5. 设置对话框打开和截图
6. 路由/系统代理下拉框选项枚举
