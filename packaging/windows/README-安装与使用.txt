小米遥控器语音助手 · Windows x64

基于 VibeCoding Voice 0.5.18 的非官方适配版，适配第一代小米蓝牙语音遥控器；不同批次、蓝牙适配器和系统仍需实际验证。

安装前：准备小米蓝牙语音遥控器、Windows x64 电脑和蓝牙适配器。安装程序需要管理员权限以安装 USBPcap 和受限的遥控器采集服务；不主动重启系统。USBPcap 安装后如系统提示重启，请手动重启后继续。

语音输入还需要安装腾讯微信输入法和 VB-Audio 的 VB-CABLE。安装向导提供可选的 VB-CABLE 安装程序；如已安装请勿重复安装。VB-CABLE 是 VB-Audio 的 Donationware，欢迎在 https://vb-audio.com/Cable/ 支持作者；微信输入法官方下载 https://z.weixin.qq.com/ 。在微信输入法“语音输入 → 麦克风”中选择 CABLE Output，并切换到微信输入法。

首次启动后，按引导配对遥控器。保持本程序运行：按住语音键说话、松开后上屏；不自动发送聊天消息。按菜单键可聚焦当前应用的输入框；首次使用其他软件时按提示点击输入框，也可在“遥控器 → 输入框聚焦”里重新学习。

方向键导航；上下长按切换 Codex 对话；主页长按打开应用切换器，方向键选择、确认键切换；确认长按切换鼠标模式。鼠标模式的空闲退出默认 60 秒，可在“遥控器 → 鼠标模式”中修改。菜单长按默认空闲，可自行绑定。

隐私：本适配版默认关闭转写记录并只在本机监听。语音识别由微信输入法提供，使用规则以腾讯说明为准。卸载本软件不会删除 USBPcap、VB-CABLE，也不会删除个人配置。

上游项目 VibeCoding Voice：https://github.com/mac20777/vibecoding-voice ，采用 MIT 许可，详见 LICENSE-VibeCoding-Voice.txt。Electron/Chromium 第三方许可随程序提供。USBPcap 的许可由其官方安装包提供。
