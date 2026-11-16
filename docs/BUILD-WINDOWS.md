# Windows 构建说明

当前安装包基于上游 VibeCoding Voice **0.5.18 的官方 Windows x64 安装内容**。仓库中的修改过的代码替换该版本的 `app.asar` 中相应文件，因此不是与其他版本通用的补丁。构建脚本会验证原版 `app.asar` 的 SHA-256；本地已验证产物和当前测试安装包使用的应用代码一致。

## 准备

- Windows x64、Node.js 20+、.NET 8 SDK、NSIS 3.11（完整 Unicode 版）；Windows 自带 .NET Framework C# 编译器。
- 从 [VibeCoding Voice 上游](https://github.com/mac20777/vibecoding-voice)取得 0.5.18 官方 Windows x64 程序，准备其安装目录以及**原版未修改**的 `resources/app.asar`；安装内容用于复制 Electron 运行时和辅助资源。不要覆盖自己正在使用的上游安装。
- 官方 [USBPcap 1.5.4.0](https://desowin.org/usbpcap/) 安装程序。参考 SHA-256：`87a7edf9bbbcf07b5f4373d9a192a6770d2ff3add7aa1e276e82e38582ccb622`。
- 从 [VB-Audio](https://vb-audio.com/Cable/) 获取并完整解压原始标准 VB-CABLE 包，保留原 ZIP。其为 Donationware；打包时保留作者的全部原始文件和许可。不要以本说明替代 VB-Audio 原始许可。

## 构建

在仓库根目录的 PowerShell 中设置路径。以下为示例，请替换为实际路径：

```powershell
$env:VIBE_BASE_DIR = 'C:\Path\To\VibeCoding Voice 0.5.18'
$env:USBPCAP_INSTALLER = 'C:\Path\To\USBPcapSetup-1.5.4.0.exe'
$env:VBCABLE_DIR = 'C:\Path\To\Unpacked-VBCABLE'
$env:VBCABLE_ZIP = 'C:\Path\To\VBCABLE_Driver_Pack46.zip'
$originalAsar = 'C:\Path\To\Original-0.5.18\resources\app.asar'
$makensis = 'C:\Program Files (x86)\NSIS\makensis.exe'

node packaging/windows/build-asar.mjs $originalAsar
dotnet publish remote-companion/RemoteCompanion.csproj -c Release -r win-x64 --self-contained true -o packaging/构建/helper-release -p:DebugType=None -p:DebugSymbols=false
node packaging/windows/prepare-payload.mjs
node packaging/windows/generate-uninstall.cjs
New-Item -ItemType Directory -Force dist | Out-Null
Push-Location packaging/windows
& $makensis installer.nsi
Pop-Location
```

输出位于 `dist/`。构建只生成文件，不自动安装驱动或重启。`prepare-payload.mjs` 从 `VIBE_BASE_DIR` 复制运行时，编译隔离的后台服务，并将标准 VB-CABLE 原包和 USBPcap 官方安装程序纳入分发；请使用官方来源和确认许可。

此项目仍属于测试版；源码构建和本机安装包已做隔离启动、自检和目录保护测试，尚未覆盖全新系统、所有蓝牙适配器和驱动安装组合。`npm run desktop:dev` 及上游完整测试套件尚未作为本适配版的发布流程验证。
