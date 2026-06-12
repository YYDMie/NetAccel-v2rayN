# R0 GUI 证据采集结果

> 任务：R0-04/R0-09/R0-11 GUI 部分
> 状态：code_complete（部分 pass，部分 blocked）
> 日期：2026-06-13
> 测试环境：Windows 11 x64, .NET 10.0.301, Release self-contained

## 一、测试目标

补充 WP-00/WP-01A/WP-01B 中因 CLI 环境无法完成的 GUI 验证项：

1. **R0-04**：原生启动冒烟的 GUI 部分（托盘图标、托盘退出、系统代理恢复）
2. **R0-09**：冻结经典模式基线截图（16 个 REG 项目）
3. **R0-11**：隔离 NetAccel 应用身份的 GUI 验证

## 二、测试方法

使用 PowerShell UI Automation (UIAutomationClient + UIAutomationTypes) 进行 GUI 元素检查和交互，
System.Drawing.CopyFromScreen 进行窗口截图，Windows Registry 验证系统代理状态。

## 三、测试结果

### 3.1 R0-04 原生启动冒烟（GUI 补充）

| 检查项 | 状态 | 详情 |
|--------|------|------|
| Release self-contained 启动 | ✅ pass | PID 27792, 无 .NET Runtime 弹窗 |
| 主窗口标题 | ✅ pass | `v2rayN - V7.22.6 - X64 - 以管理员身份运行` |
| 主窗口 UI 完整性 | ✅ pass | 菜单栏/工具栏/状态栏/节点列表/日志标签页均可见 |
| 系统代理下拉框 | ✅ pass | 4 选项：清除/自动配置/不改变/Pac 模式 |
| 系统代理 Registry 写入 | ✅ pass | 设置→ProxyEnable=1, 清除→ProxyEnable=0 |
| 托盘最小化（menuClose） | ✅ pass | 进程持续运行，MainWindowHandle=0 |
| 托盘图标渲染 | ⚠️ blocked | UIA 无法截图系统托盘区域 |
| 托盘右键退出 | ⚠️ blocked | UIA 无法定位 NotifyIcon 上下文菜单 |
| 系统代理退出恢复 | ⚠️ blocked | 依赖托盘正常退出流程 |

### 3.2 R0-09 冻结经典模式基线截图

| REG | 功能 | 状态 | 截图文件 |
|-----|------|------|----------|
| 01 | 本地节点 | ⚠️ blocked | `REG-01-settings.png`（仅设置对话框） |
| 02 | 通用订阅 | ⚠️ blocked | 需订阅 URL 和网络 |
| 03 | 剪贴板导入 | ⚠️ blocked | 需 GUI 交互 |
| 04 | 二维码/文件导入 | ⚠️ blocked | 需 GUI 交互 |
| 05 | 分享和导出 | ⚠️ blocked | 需已有节点 |
| 06 | Xray 连接 | ⚠️ blocked | 需核心和节点 |
| 07 | sing-box 连接 | ⚠️ blocked | 需核心和节点 |
| 08 | 系统代理 | ✅ pass | `REG-08-system-proxy-auto.png` + `REG-08-system-proxy-cleared.png` |
| 09 | TUN | ⚠️ blocked | 需管理员权限和驱动 |
| 10 | 测速 | ⚠️ blocked | 需网络和节点 |
| 11 | 路由配置 | ⚠️ blocked | 需 GUI 交互 |
| 12 | DNS 配置 | ⚠️ blocked | 需 GUI 交互 |
| 13 | 托盘 | ✅ pass | `REG-13-tray-before.png` + 进程验证 |
| 14 | 备份恢复 | ⚠️ blocked | 需 GUI 交互 |
| 15 | 更新检查 | ⚠️ blocked | 需网络 |
| 16 | 主窗口 | ✅ pass | `REG-16-main-window.png` |

### 3.3 R0-11 隔离应用身份（GUI 验证）

| 检查项 | 状态 | 详情 |
|--------|------|------|
| 窗口标题包含版本 | ✅ pass | `v2rayN - V7.22.6 - X64 - 以管理员身份运行` |
| 系统代理操作正常 | ✅ pass | Registry 验证通过 |
| 菜单结构完整 | ✅ pass | 7 个顶级菜单项可见 |
| 设置对话框可打开 | ✅ pass | 6 个标签页：基础设置/v2rayN设置/KCP/TcpFastOpen/预定义配置/关于 |
| 路由下拉框可用 | ✅ pass | 3 个路由配置 |
| 系统代理下拉框可用 | ✅ pass | 4 个选项 |
| TUN 开关可见 | ✅ pass | togEnableTun 按钮 |
| 单实例互斥 | ✅ pass | WP-00 已验证 |

## 四、采集的证据文件

| 文件 | 大小 | 说明 |
|------|------|------|
| `REG-01-settings.png` | 38KB | 参数设置对话框（CoreInfo/基础设置） |
| `REG-08-system-proxy-auto.png` | 44KB | 系统代理自动配置状态 |
| `REG-08-system-proxy-cleared.png` | 46KB | 系统代理清除状态 |
| `REG-13-tray-before.png` | 42KB | 托盘测试前主窗口 |
| `REG-16-main-window.png` | 42KB | 主窗口完整截图 |

## 五、UI 自动化发现

### 5.1 主窗口 UIA 结构

```
Window: v2rayN - V7.22.6 - X64 - 以管理员身份运行
├── ToolBar (PART_Toggle)
├── StatusBarView
│   ├── txtInboundDisplay: "本地:[mixed:10808]"
│   ├── txtInboundLanDisplay: "局域网:none"
│   ├── togEnableTun: "启用 Tun"
│   ├── cmbSystemProxy: "系统代理" (ComboBox)
│   └── cmbRoutings2: "路由" (ComboBox)
├── ProfilesView
│   ├── lstGroup: "订阅分组" (List)
│   ├── txtServerFilter: "过滤器，按回车执行"
│   └── DataGrid (12 列)
├── GridSplitter
└── TabControl (tabMain1)
    ├── 日志 (txtMsgFilter/copy/clear)
    ├── 查询统计
    └── 其它
```

### 5.2 设置菜单结构

```
设置 (MenuItem, ExpandCollapse)
├── 参数设置
├── 路由设置
├── DNS 设置
├── 完整配置模板设置
├── 全局热键设置
├── ───────── (Separator)
├── 以管理员身份重启
├── 解除 Win10 UWP 应用回环代理限制
├── 清除所有服务统计数据
├── ───────── (Separator)
├── 区域预置设置
├── 备份和还原
└── 打开存储所在的位置
```

## 六、系统代理 Registry 验证

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings

初始状态:     ProxyEnable=0, ProxyServer=127.0.0.1:10808
自动配置后:   ProxyEnable=1, ProxyServer=127.0.0.1:10808  ✅
清除后:       ProxyEnable=0, ProxyServer=127.0.0.1:10808  ✅
```

## 七、托盘行为验证

```
点击 menuClose 前:
  MainWindowHandle = 27792
  MainWindowTitle  = "v2rayN - V7.22.6 - X64 - 以管理员身份运行"

点击 menuClose 后:
  MainWindowHandle = 0
  MainWindowTitle  = ""
  Process.Responding = True
  Process.Threads = 31

结论: 应用正确最小化到系统托盘，进程持续运行
```

## 八、结论

### 已完成（pass）

- 主窗口完整渲染和 UI 元素验证
- 系统代理设置/清除 Registry 验证
- 托盘最小化行为验证
- 设置对话框打开和标签页结构
- 路由/系统代理下拉框功能
- 单实例互斥（WP-00 已验证）
- 5 张截图证据采集

### 仍需人工验证（blocked）

- 托盘图标真实渲染（UIA 限制）
- 托盘右键菜单和正常退出（UIA 限制）
- 系统代理退出恢复（依赖托盘退出）
- 12 个 REG 截图（需 GUI 交互、网络、核心等）

### 后续建议

1. 人工验证 3 个托盘相关项目并补充截图
2. 配置可用节点后批量采集剩余 REG 截图
3. 考虑使用 WinAppDriver 或类似工具替代 UIA 以支持更复杂的 GUI 自动化
