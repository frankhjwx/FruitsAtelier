# 水果工坊 · FruitsAtelier

![水果工坊](assets/branding/wordmark-zh.svg)

[English](README.md) | **简体中文**

独立的 osu!catch 谱面编辑器，支持 Windows 和 macOS。使用时间—X 画布编辑水果与 FSlider，并随音乐预览 Catch 对象。

项目正在开发中。

## 功能

- 预览播放 [Catch 打击音](docs/HITSOUNDS.md)，支持谱面自定义音效、滑条 tick 和香蕉音效。

- 打开 `.osz`、v14 / Mode=2 `.osu` 和 `.catchproj` 工程。
- 编辑水果、FSlider 和香蕉雨，支持节拍吸附、多选、批量移动、剪切复制和撤销重做。
- 将导入的 Legacy Slider 转换为可编辑的 FSlider，调整锚点、贝塞尔控制柄和行程次数。
- 播放 MP3 / OGG / WAV，拖动时间轴定位；预览支持 AR、CS 和 Catch 皮肤。
- 自定义 workspace，保存 `project.catchdiff` 与自动命名的各难度文件；资源只引用，仅 Export 写入 osu!stable Songs。
- 独立曲库界面，支持原始／罗马字歌名、Artist、作者与 Tags 搜索。参见 [workspace 使用说明](docs/WORKSPACE.md)。
- 持久引用外部文件夹，或将 OSZ 完整解压到 workspace 资源目录；重启后仍可从曲库继续使用。
- 默认英文界面，支持切换中文并记住语言设置。

## 运行

### macOS

需要 .NET SDK **8.0.419** 和 Xcode Command Line Tools。在仓库目录执行以下命令可安装项目内 SDK：

```bash
bash scripts/Install-Mac-SDK.sh
```

双击 [Run-Editor-Mac.command](Run-Editor-Mac.command) 构建并启动。生成独立应用：

```bash
bash scripts/Publish-Mac.sh
```

输出为 `artifacts/macos/FruitsAtelier.app`，包含 .NET 运行时。详见 [macOS 说明](docs/MACOS.md)。

### Windows

需要 .NET SDK **10.0.400**（根目录 `global.json` 指定）和 **.NET 8 运行时**。双击 [Run-Editor.cmd](Run-Editor.cmd) 构建并启动。

编译后的程序位于 `src/FruitsAtelier.App/bin/Release/net8.0-windows/FruitsAtelier.App.exe`。

## 文档

- [编辑操作](docs/EDITOR_UI.md)
- [功能与文件说明](docs/PRODUCT.md)
- [构建与测试](docs/TESTING.md)
- [技术架构](docs/ARCHITECTURE.md)
- [工程数据模型](docs/PROJECT_MODEL.md) · [Catch 绘制与转换](docs/CATCH_RENDERING.md) · [文件格式](docs/STABLE_FORMAT.md)
- [本地化维护](docs/LOCALIZATION.md)
- [第三方依赖与许可](THIRD_PARTY_NOTICES.md)
