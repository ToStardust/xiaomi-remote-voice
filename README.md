# 小米遥控器语音助手

将第一代小米蓝牙语音遥控器用于 Windows 操作与微信输入法语音输入。此项目是 [VibeCoding Voice](https://github.com/mac20777/vibecoding-voice) 0.5.18 的非官方改造，与小米和腾讯无关联。

> 测试版：功能已在开发者的 Windows 电脑和一只第一代遥控器上测试，其他遥控器批次、蓝牙适配器和全新系统的兼容性尚待验证。欢迎反馈问题。

## 功能

- 按住语音键说话，松开后由微信输入法识别并输入。
- 方向键、确认、返回和音量操作；长按上下键切换 Codex 对话。
- 长按主页打开应用切换器，方向键选择、确认键切换。
- 长按确认进入或退出鼠标模式：方向键微调或长按移动，确认键单击或双击。空闲时默认 60 秒退出，可在软件中设置。
- 为 Codex、微信、QQ、Obsidian 等应用学习输入框，短按菜单键聚焦；也可在软件设置中重新学习。应用是否暴露可聚焦控件取决于其实现。

## 安装与首次配置

需要 Windows x64、第一代小米蓝牙语音遥控器和蓝牙适配器。使用安装包时请按向导安装或确认 USBPcap、VB-CABLE，安装并启用[微信输入法](https://z.weixin.qq.com/)，在微信输入法的语音设置中选择 `CABLE Output`。安装 USBPcap 后可能需要手动重启。若蓝牙已被其他设备占用，请先断开，再根据向导配对。

安装后从桌面启动，跟随引导完成设置。USBPcap 安装需要管理员权限；程序日常使用以普通用户启动。完整说明见 [安装与使用](packaging/windows/README-安装与使用.txt)。安装包尚未签名，Windows 可能提示未知发布者。

本仓库默认不包含驱动、微信输入法或编译产物；参照 [构建说明](docs/BUILD-WINDOWS.md) 获取依赖并构建。可以从项目 Releases 获取维护者提供的安装包（如果已有发布）。

## 隐私

适配版默认仅在本机监听，并关闭转写记录。语音识别由微信输入法提供，数据处理以其说明为准。不同语音服务可能有不同的数据处理方式。

## 源码与致谢

本项目基于 [mac20777/vibecoding-voice](https://github.com/mac20777/vibecoding-voice)，上游基线为 `02a77999ec9b5a9e8b072d1333eb3dcff1b81932`（0.5.18）。保留上游 [MIT 许可证](LICENSE)和[原 README](docs/UPSTREAM-README.md)。

- `desktop/`、`src/`、`scripts/xiaomi-remote-input.mjs`：桌面程序、USB 数据处理及适配。
- `remote-companion/`：鼠标、输入框聚焦与窗口切换等 Windows 组件。
- `packaging/windows/`：安装包构建、服务安装和说明。
- [USBPcap](https://desowin.org/usbpcap/) 按其官方许可提供；[VB-CABLE](https://vb-audio.com/Cable/) 为 VB-Audio 的 Donationware，请按作者条款使用并考虑支持作者。两者不在源码仓库中。
