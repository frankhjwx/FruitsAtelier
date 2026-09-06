# 构建与测试

## SDK 与构建

项目目标为 .NET 8，语言版本为 C# 12。两个 SDK 入口由 `global.json` 分别固定：

| 入口 | SDK | 用途 |
| --- | --- | --- |
| 仓库根目录 | 10.0.400 | Windows 启动脚本和根目录构建 |
| `macOS/` | 8.0.419 | Mac 脚本，以及 Windows/macOS CI |

Windows 在仓库根目录构建：

```powershell
dotnet build FruitsAtelier.sln -c Release -p:RestoreLockedMode=true
```

使用 SDK 8.0.419 构建 Windows 解决方案时，先进入 `macOS/`：

```powershell
cd macOS
dotnet build ../FruitsAtelier.sln -c Release -p:RestoreLockedMode=true
```

Mac 启动与打包见[macOS 运行](MACOS.md)。包版本由各项目及 `packages.lock.json` 固定，NuGet 缓存位于 `artifacts/packages`。

## 自动回归

测试项目是控制台程序，使用 `dotnet run` 执行。格式导出的量化、回读和边缘样本诊断检查会遍历所有可用语言，使用本地化表验证消息及参数，避免依赖默认界面语言。

Windows 在完成解决方案构建后，从仓库根目录运行：

```powershell
foreach ($suite in @('Core', 'Gameplay', 'App', 'Skinning', 'SkinArchive')) {
    dotnet run --no-build --project "tests/FruitsAtelier.$suite.Tests" -c Release
    if ($LASTEXITCODE -ne 0) { throw "$suite tests failed" }
}
dotnet run --no-build --project tests/FruitsAtelier.Formats.Tests -c Release -- --skip-external-fixtures
```

Mac 从仓库根目录运行：

```bash
bash scripts/Test-Mac.sh                      # 共享回归及 Mac 输入/音频检查
bash scripts/Test-Mac.sh --skip-device-tests  # 共享回归，跳过整个 Mac 原生测试项目
bash scripts/Test-Mac.sh --native-only        # 仅 Mac 输入/音频检查
```

[Desktop regression](../.github/workflows/desktop.yml) 在 push 和 PR 时运行：Windows 构建解决方案并执行共享回归；macOS 执行共享回归并打包应用。运行结果见 [GitHub Actions](https://github.com/frankhjwx/FruitsAtelier/actions)。

## 外部测试资源

仓库包含合成格式样例、旧版本 `.catchproj` 兼容样例和 OGG 音频样例。以下检查另需本地资源：

- Formats 默认运行的两个真实谱面检查需要 `artifacts/beatmaps` 下的外部谱面；`--skip-external-fixtures` 跳过这两项，CI 和 Mac 脚本使用该参数。
- Core 的 `--fixtures` 参数启用额外的真实谱面检查。
- Windows `Audio.Tests` 完整运行需要默认音频设备及 `artifacts/beatmaps` 下的 MP3 样例。`--recovery-check` 只运行注入输出故障的检查，`--lifecycle-check` 只运行播放生命周期检查。
- 指定默认皮肤和用户工程的测试依赖未提交的本地文件；查看各测试输出中的跳过信息。

自动设备测试输出静音 PCM，采样比较在静音前进行。Windows 音频测试命令：

```powershell
dotnet run --project tests/FruitsAtelier.Audio.Tests -c Release
```

## 窗口检查

```bash
./Run-Editor-Mac.command --smoke-check
```

该命令打开 Mac 窗口，执行水果放置、撤销、工程往返和中英文截图检查后退出。截图写入 `artifacts/macos-check`，日志写入 `artifacts/logs/macos.log`。

修改输入或绘制后，手动检查相关操作、语言切换、窗口缩放和文件对话框。当前仍需补充 Windows 实机窗口/音频、Intel Mac、跨屏 DPI、Mac MP3 和 stable 客户端对照检查。
