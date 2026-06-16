# iFlyCompass GUI

iFlyCompass 的 Windows 桌面管理工具，基于 WinUI 3 构建，提供图形化界面来安装、配置和管理 [iFlyCompass](https://github.com/MoyuZJ912/iFlyCompass) 服务。

## 功能

- 自动下载 iFlyCompass 并配置环境
- 检查 iFlyCompass 更新
- 控制 iFlyCompass 服务
- 支持开机静默自启（没有系统托盘）
- 管理小说阅读器小说文件（导入时自动转换文件编码为 UTF-8 ）
- 管理视频播放器视频文件（可选 H.265 编码转换）
- 配置 AI对话 功能（API Key、API 地址、模型参数、System Prompt）
- 管理用户（新建用户、重置密码、编辑昵称、管理员权限）
- 查看 iFlyCompass 日志
- 支持项目后台运行（无系统托盘）


## 安装教程

### 自动安装（推荐）
>由于部分系统会自动禁用Windows更新服务，导致应用安装程序无法正常安装依赖，加上还需要手动安装证书，所以推荐使用自动安装方式。

在release中下载最新版本的 iFlyCompassGUI-Setup.exe，然后双击运行安装程序，安装程序会自动帮你在网上下载项目所需的依赖并安装，然后帮你安装证书和应用（安装包程序自带证书和msix安装包）。

### 手动安装（调用系统应用安装程序）
#### 下载文件
在release中下载最新版本的 iFlyCompassGUI 应用和证书文件。
>文件一般为iFlyCompassGUI_x.x.x.x_x64.msix和iFlyCompassGUI.cer

#### 安装证书（首次安装时需要）
1. 右键点击 .cer 证书文件，选择“安装证书”。
2. 在向导中，关键选择“本地计算机”，然后点击“下一步”。
3. 选择“将所有的证书放入下列存储”，点击“浏览”，选择“受信任的根证书颁发机构”，点击“确定”。
4. 确认安全警告后，完成导入。

#### 安装应用
双击运行应用，按照提示完成安装。


## 使用说明
第一次启动后，应用会进入欢迎页，点击"安装"按钮即可自动完成以下步骤：

1. 从 GitHub 下载 iFlyCompass 最新版本
2. 解压到本地目录
3. 下载嵌入式 Python 3.12.10 并配置
4. 安装 pip 及所有 Python 依赖

安装完成后，通过左侧导航栏使用各项功能。主页可以启动 iFlyCompass 服务，启动后点击"打开网页"即可在浏览器中访问 `http://127.0.0.1:5002`。


## 技术栈

| 技术 | 说明 |
|------|------|
| .NET 10 | 目标框架 `net10.0-windows10.0.19041.0` |
| WinUI 3 | Windows App SDK 2.1.3 |
| CommunityToolkit.Mvvm | MVVM 框架（8.2.2） |
| Microsoft.Extensions.DependencyInjection | 依赖注入（10.0.0） |
| Microsoft.Data.Sqlite | SQLite 数据库操作（10.0.0） |
| aria2c | BT/HTTP 下载工具（运行时工具） |
| FFmpeg | 视频编码转换（运行时工具） |
| C# 13 | 语言版本 |

## 目录结构

```
iFlyCompassGUI/
├── App.xaml.cs              # 应用入口与依赖注入配置
├── MainWindow.xaml          # 主窗口（导航框架）
├── Views/                   # 页面视图（XAML）
├── ViewModels/              # 视图模型（MVVM）
├── Services/                # 业务服务层
├── Models/                  # 数据模型
├── Converters/              # 值转换器
├── Helpers/                 # 工具类
└── Assets/                  # 图标资源
```

## 依赖的运行时组件

项目运行时会在数据目录下自动创建：
- `./iFlyCompass/` — iFlyCompass 主程序（从 GitHub 克隆）
- `./python/` — 嵌入式 Python 3.12.10

## 许可证

MIT License — 详见 [LICENSE](LICENSE)