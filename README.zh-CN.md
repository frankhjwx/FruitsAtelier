# 水果工坊 · FruitsAtelier

![水果工坊](assets/branding/wordmark-zh.svg)

[English](README.md) | **简体中文**

适用于 Windows 和 macOS 的 osu!catch 谱面编辑器。编排水果、调整滑条，并随音乐和打击音预览、试玩谱面。

**当前版本：0.8**

## 功能

- **谱面曲库**：浏览、搜索 osu!stable Songs，导入外部文件夹或 `.osz` 压缩包，继续编辑已保存的工程。
- **物件编辑**：放置水果和香蕉雨，批量选择、移动、复制、水平翻转或删除，支持撤销和重做。
- **滑条工具**：用控制点或贝塞尔控制柄绘制、调整 FSlider，添加折返，将导入的滑条转换为 FSlider，或沿滑条路径生成水果串。
- **吸附与音效**：支持节拍细分、水平网格和距离吸附；可设置新连击、物件打击音及滑条端点音效。
- **音乐与预览**：播放 MP3、OGG、WAV 及打击音，支持 25%、50%、75% 和原速播放；使用皮肤及 NM、Easy、Hard Rock 设置预览谱面。
- **试玩**：从当前位置开始接水果，支持移动、冲刺、连击反馈及自动游玩，可自定义移动按键。
- **多难度工程**：使用标签页切换难度、查看星级、保存可编辑工程，并导出 `.osu` 或向 osu!stable 添加新难度。
- **皮肤与语言**：支持 osu!stable 皮肤、`.osk` 导入和中英文界面。

支持读取 v12-v14 的 Catch `.osu`，导出为 v14。0.8 暂不提供视频、故事板播放及 timing point 创建。

## 开始使用

### Windows

从 [Releases](https://github.com/frankhjwx/FruitsAtelier/releases) 下载 Windows x64 ZIP，完整解压后运行 `FruitsAtelier.App.exe`。保留解压后的所有文件。包内自带 .NET，适用于支持 DirectX 11 的 Windows 10/11。

在曲库的设置中选择工程目录，并按需连接 osu!stable 安装目录。导入谱面或新建工程即可开始。

### macOS

从源码构建需要 .NET SDK **8.0.419** 和 Xcode Command Line Tools。运行 `bash scripts/Install-Mac-SDK.sh` 安装项目内 SDK，然后打开 [Run-Editor-Mac.command](Run-Editor-Mac.command)。运行 `bash scripts/Publish-Mac.sh` 生成独立应用。详见 [macOS 指南](docs/MACOS.md)。

## 用户手册

[英文用户手册](docs/USER_MANUAL.md) 包含入门、编辑、保存、试玩和快捷键说明。

保存工程可保留滑条和各难度的可编辑数据，导出则生成供 osu! 使用的 `.osu`。难度通过导出关联文件后，**Ctrl+S 也会更新关联的 `.osu`**；**Ctrl+E** 打开导出选项。

## 开发

Windows 源码构建使用 `global.json` 指定的 .NET SDK **10.0.400** 和 .NET 8 运行时。运行 [Run-Editor.cmd](Run-Editor.cmd) 构建并启动。

- [构建与测试](docs/TESTING.md) · [打包与发布](docs/RELEASING.md)
- [编辑操作](docs/EDITOR_UI.md) · [工程与文件](docs/WORKSPACE.md)
- [技术架构](docs/ARCHITECTURE.md) · [工程模型](docs/PROJECT_MODEL.md) · [文件格式](docs/STABLE_FORMAT.md)
- [本地化维护](docs/LOCALIZATION.md) · [第三方许可](THIRD_PARTY_NOTICES.md)
