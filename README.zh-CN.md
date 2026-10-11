# 水果工坊 · FruitsAtelier

![水果工坊](assets/branding/wordmark-zh.svg)

[English](README.md) | **简体中文** | [日本語](README.ja.md) | [한국어](README.ko.md)

适用于 Windows 和 macOS 的独立 osu!catch 谱面编辑器。从选曲、编排到试玩，在同一个工作区里完成你的下一张 Catch 谱面。

[官网](https://fruitsatelier.himiko.moe/) · [下载](https://github.com/frankhjwx/FruitsAtelier/releases/latest) · [Discord](https://discord.gg/Dwe7bshYHY) · [Himiko 的 osu! 主页](https://osu.ppy.sh/users/1806962)

**当前版本：0.9.9**

![水果工坊的编辑画布、物件时间轴与 Catch 预览](website/public/assets/eureka.png)

*Σvreka — Halv vs. kuro · Ascendance [Chronosync]。截图中的谱面与皮肤美术归各自创作者所有。*

## 制作你的下一张 Catch 谱面

- **从曲库开始**：浏览、搜索 osu!stable Songs，导入文件夹或 `.osz`，继续编辑已保存的工程。通过标签页切换多个难度，并查看星级。
- **直接编排物件**：放置水果和香蕉雨，批量移动、复制、水平翻转或删除，支持撤销与重做。用控制点或贝塞尔控制柄绘制 FSlider，添加折返、转换导入的滑条，或沿路径生成水果串。
- **调整位置与 Timing**：使用节拍细分、水平网格和距离吸附。编辑红线、绿线，打拍定速，重新吸附物件，并用节拍器辅助检查；设置新连击、物件打击音及各个滑条端点音效。
- **边听边看，随时试玩**：播放 MP3、OGG、WAV 及打击音，支持 10%、25%、50%、75%、100% 和 150% 速度。预览 NM、Easy、Hard Rock，从当前位置开始试玩，支持自定义移动按键、冲刺、连击反馈与自动游玩。
- **使用熟悉的皮肤与语言**：加载 osu!stable 皮肤或导入 `.osk`。界面支持英语、简体中文、繁体中文、日语、韩语、俄语、西班牙语、法语、波兰语、荷兰语、菲律宾语、印尼语和泰语。
- **保存工程，导出谱面**：保存可继续编辑的滑条与难度数据，导出 `.osu`，或向 osu!stable 添加新难度。

支持读取 v12–v14 及兼容 stable 的 lazer v128 Catch `.osu`，导出为 v14。0.9 暂不提供视频与故事板播放。

## 开始使用

### Windows

1. 从 [Releases](https://github.com/frankhjwx/FruitsAtelier/releases/latest) 下载 Windows x64 ZIP。
2. 完整解压后运行 `FruitsAtelier.exe`，保留解压后的所有文件。包内自带 .NET，需要 Windows 10/11 和 DirectX 11。
3. 按首次启动引导设置目录、语言、皮肤、音频与试玩选项。这些选项也可以随时在**曲库 > 设置**中修改。
4. 导入谱面或新建工程，然后打开难度开始编辑。

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

加入 [Discord](https://discord.gg/Dwe7bshYHY) 参与 Alpha 测试、交流使用反馈。可通过 [GitHub Issues](https://github.com/frankhjwx/FruitsAtelier/issues) 报告问题，也可以通过 [Himiko 的 osu! 主页](https://osu.ppy.sh/users/1806962) 联系作者。

## 开发

项目使用 C# 12 与 .NET 8。Windows 源码构建使用 `global.json` 指定的 SDK **10.0.400**，运行 [Run-Editor.cmd](Run-Editor.cmd) 构建并启动。macOS 脚本使用 `macOS/` 下指定的 SDK。

- [构建与测试](docs/TESTING.md) · [打包与发布](docs/RELEASING.md)
- [编辑操作](docs/EDITOR_UI.md) · [工程与文件](docs/WORKSPACE.md)
- [技术架构](docs/ARCHITECTURE.md) · [工程模型](docs/PROJECT_MODEL.md) · [文件格式](docs/STABLE_FORMAT.md)
- [本地化维护](docs/LOCALIZATION.md) · [第三方许可](THIRD_PARTY_NOTICES.md)

技术文档与用户手册以英文维护。
