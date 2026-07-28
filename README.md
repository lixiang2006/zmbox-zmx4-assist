# Zmbox ZMX4 Assist

[English](#english) | 中文

造梦盒子《造梦西游四》专用的本地键鼠录制与回放辅助工具，面向 Windows 10 22H2+ 与 Windows 11。

它只接受进程名为 `造梦盒子` 且标题包含“造梦盒子”的窗口。项目不包含造梦盒子、游戏资源、账号功能、驱动、进程注入、反检测或任何规避机制。

> 请自行确认并遵守游戏、启动器和平台规则。实验性后台窗口消息没有兼容性保证；默认使用前台系统输入。

## 使用

1. 启动 `ZmboxZmx4Assist.exe`，先打开造梦盒子中的《造梦西游四》。
2. 让游戏窗口前台，按 `F8` 开始录制；再次按 `F8` 保存。切离造梦盒子会丢弃本次录制。
3. 选中宏后点击“倒计时锁定”或按 `F9`。在 3 秒内点击造梦盒子窗口；非造梦盒子窗口会被拒绝。
4. 锁定成功会显示 1.5 秒的黑色物理像素外框。直接回放会在外框消失后才发送第一条输入；单独锁定不发送输入。
5. 设置循环、每轮等待、周期冷却与 0.90x–1.10x 速度。`F12` 始终释放已按下的键鼠并停止。

宏会保留按下/抬起时刻；短按的小幅鼠标抖动被视为点击，真实拖拽会保留轨迹。每轮结束、冷却、异常和急停都会释放输入。

## 本地数据与迁移

数据保存于 `%LOCALAPPDATA%\ZmboxZmx4Assist`。首次运行会从旧 `%LOCALAPPDATA%\GameMacro` **复制**宏、热键和可识别的造梦盒子设置；旧目录不会被修改或删除。损坏的宏文件会保留原文件并在界面提示。

## 实验性后台消息

后台通道只发送普通 Windows 窗口消息。请先使用无破坏性动作验证，再手动标记“后台通过”或“不支持”。若未验证或被标记为不支持，应用将禁用后台模式。

## 开发

```powershell
dotnet restore .\ZmboxZmx4Assist.sln
dotnet test .\ZmboxZmx4Assist.sln -c Release
dotnet publish .\src\ZmboxZmx4Assist\ZmboxZmx4Assist.csproj -c Release -r win-x64 --self-contained true
```

仓库采用 [GPL-3.0-only](LICENSE)。贡献要求见 [CONTRIBUTING.md](CONTRIBUTING.md)，架构见 [docs/architecture.md](docs/architecture.md)，故障排查见 [docs/troubleshooting.md](docs/troubleshooting.md)。

## English

**Zmbox ZMX4 Assist** is a local keyboard/mouse recorder and playback helper for *Fantasy Journey to the West IV* running inside Zmbox. It accepts only a visible Zmbox process and window title, keeps data locally, and never uses drivers, injection, anti-detection, or bypass techniques. Use it only in a way that complies with the applicable game and platform rules. Experimental background window messages are unsupported unless you verify them yourself.
