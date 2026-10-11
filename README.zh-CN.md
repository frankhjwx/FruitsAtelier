# 水果工坊 · FruitsAtelier

![水果工坊](assets/branding/wordmark-zh.svg)

[English](README.md) | **简体中文** | [日本語](README.ja.md) | [한국어](README.ko.md)

适用于 Windows 和 macOS 的独立 osu!catch 谱面编辑器。从选曲、编排到试玩，在同一个工作区里完成你的下一张 Catch 谱面。

[官网](https://fruitsatelier.himiko.moe/) · [下载](https://github.com/frankhjwx/FruitsAtelier/releases/latest) · [Discord](https://discord.gg/Dwe7bshYHY)

**当前版本：0.9.9**

![水果工坊的编辑画布、物件时间轴与 Catch 预览](website/public/assets/eureka.png)

*Σvreka — Halv vs. kuro · Ascendance [Chronosync]。*

## 制作你的下一张 Catch 谱面

- **从曲库开始**：浏览、搜索 osu!stable Songs，导入文件夹或 `.osz`，继续编辑已保存的工程。通过标签页切换多个难度，并查看星级。
- **直接编排物件**：在横轴表示 Catch 位置、纵轴表示时间的画布上放置水果和香蕉雨。平移、缩放视图，批量移动、复制、水平翻转或删除，支持撤销与重做。
- **调整 Timing 与打击音**：编辑红线、绿线，打拍定速，重新吸附物件，并用节拍器辅助检查；设置新连击、物件打击音及各个滑条端点音效。Hitsound Copier（Beta）可从其他难度复制音效，应用前先预览结果。
- **边听边看，随时试玩**：播放 MP3、OGG、WAV 及打击音，支持 10%、25%、50%、75%、100% 和 150% 速度。预览 NM、Easy、Hard Rock，从当前位置开始试玩，支持自定义移动按键、冲刺、连击反馈与自动游玩。
- **使用熟悉的皮肤与语言**：加载 osu!stable 皮肤或导入 `.osk`。界面支持英语、简体中文、繁体中文、日语、韩语、俄语、西班牙语、法语、波兰语、荷兰语、菲律宾语、印尼语和泰语。
- **保存工程，导出谱面**：保存可继续编辑的滑条与难度数据，导出 `.osu` 难度或 `.osz` 谱面集，与 osu!stable 同步已关联的难度。检查外部修改冲突，并通过版本历史恢复难度快照。

## 为 Catch 设计的工具

- **FSlider**：用控制点或贝塞尔控制柄直接绘制随时间变化的水平运动，添加折返，或转换导入的 Legacy Slider。FSlider 导出为普通 osu! 滑条，工程中保留可编辑曲线。
- **直接编辑水滴**：选择滑条内的单个水果或水滴，水平拖动或输入 X 坐标，并用网格吸附、距离吸附调整间距。
- **随机化与去随机化**：针对 NM 或 HR 补偿 TinyDroplet 的随机偏移，也可自定义随机强度与种子。针对 HR 去随机化时，可切换两种预览检查结果。
- **DPB 与距离吸附**：用 Distance Per Beat（每拍距离）定义水平间距基准，同时配置最多八档距离吸附。节拍吸附控制时间，网格吸附控制水平位置。
- **移动指示与分析**：查看前后物件间距，在画布上识别站立、走路、冲刺和超级冲刺连接。通过全曲 Movement strain 曲线定位运动负荷较高的段落；AiMod 可检查物件起始时间重叠。
- **可编辑的 Stream 与 Stack**：沿滑条曲线按指定节拍细分生成水果串，或用宽度包络生成交替排列的叠果。保留可编辑的父曲线，调整单个水果，或拆分成独立水果；两者均导出为 hit circle。

支持读取 v12–v14 及兼容 stable 的 lazer v128 Catch `.osu`，导出为 v14。0.9 暂不提供视频与故事板播放。

## 开始使用

### Windows

1. 从 [Releases](https://github.com/frankhjwx/FruitsAtelier/releases/latest) 下载 Windows x64 ZIP。
2. 完整解压后运行 `FruitsAtelier.exe`，保留解压后的所有文件。包内自带 .NET，需要 Windows 10/11 和 DirectX 11。
3. 按首次启动引导设置目录、语言、皮肤、音频与试玩选项。这些选项也可以随时在**曲库 > 设置**中修改。
4. 导入谱面或新建工程，然后打开难度开始编辑。

支持内置更新的安装可在**设置 > 应用更新 > 检查更新**中下载更新、保存并重启。工程与自定义皮肤应放在应用的 `current/` 文件夹之外。

### macOS

从源码构建需要 .NET SDK **8.0.419** 和 Xcode Command Line Tools。在仓库根目录运行：

```bash
bash scripts/Install-Mac-SDK.sh
./Run-Editor-Mac.command
```

运行 `bash scripts/Publish-Mac.sh` 生成独立应用，详见 [macOS 指南（英文）](docs/MACOS.md)。

## 使用指南与交流

[用户手册（英文）](docs/USER_MANUAL.md) 介绍入门、编辑、保存和试玩；[键盘与鼠标操作参考（英文）](docs/KEY_BINDINGS.md) 列出完整快捷键。

难度通过导出关联文件后，**Ctrl+S 也会更新关联的 `.osu`**；使用 **Ctrl+Alt+E** 打开导出选项。

工作区的 `.catchdiff` 文件保留编辑数据，也支持打开旧版 `.catchproj` 工程。请保留完整工程文件夹与引用资源。版本历史支持比较快照和恢复单个难度，保留策略通常限制为 30 天、每个工程 100 轮。详见[同步与恢复说明（英文）](docs/SYNCHRONIZATION.md)。

加入 [Discord](https://discord.gg/Dwe7bshYHY) 参与 Alpha 测试、交流使用反馈。可通过 [GitHub Issues](https://github.com/frankhjwx/FruitsAtelier/issues) 报告问题，或[在 osu! 上联系我](https://osu.ppy.sh/users/1806962)。

## 开发

项目使用 C# 12 与 .NET 8。Windows 源码构建使用 `global.json` 指定的 SDK **10.0.400**，运行 [Run-Editor.cmd](Run-Editor.cmd) 构建并启动。macOS 脚本使用 `macOS/` 下指定的 SDK。

- [构建与测试](docs/TESTING.md) · [打包与发布](docs/RELEASING.md)
- [编辑操作](docs/EDITOR_UI.md) · [工程与文件](docs/WORKSPACE.md)
- [技术架构](docs/ARCHITECTURE.md) · [工程模型](docs/PROJECT_MODEL.md) · [文件格式](docs/STABLE_FORMAT.md)
- [本地化维护](docs/LOCALIZATION.md) · [第三方许可](THIRD_PARTY_NOTICES.md)

技术文档与用户手册以英文维护。
