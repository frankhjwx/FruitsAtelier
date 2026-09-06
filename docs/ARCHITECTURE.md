# 技术架构

## 平台与依赖

应用使用 C# 12 / .NET 8。Windows 和 macOS 共用编辑器、数据模型与文件格式，窗口、绘制和音频分别接入系统实现。

| 层 | Windows | macOS |
| --- | --- | --- |
| 入口 | `FruitsAtelier.App`，`net8.0-windows` | `FruitsAtelier.Mac`，`net8.0` |
| 窗口与输入 | Win32、DPI 消息、原生文件对话框 | Avalonia 桌面窗口与文件选择器 |
| 绘制 | DX11 / DXGI、Direct2D / DirectWrite，Vortice 3.6.2 | Avalonia 11.3.7，`MacCanvas` 实现 `ICanvas` |
| PNG | Windows Imaging Component | Avalonia 位图 |
| 音频 | NAudio 共享模式 WASAPI；Media Foundation / NVorbis / WAV reader | AVAudioPlayer；NVorbis 将 OGG 解码为 PCM WAV |

SDK 选择和构建命令见[构建与测试](TESTING.md)，包版本与许可证见[第三方声明](../THIRD_PARTY_NOTICES.md)。

## 代码布局

| 目录 | 职责 |
| --- | --- |
| `src/FruitsAtelier.Core/Model` | 文档、FSlider、导入对象和 timing |
| `Core/Formats` | `.osu` 读写、工程 JSON 和原子保存 |
| `Core/Editing`、`Core/Curves`、`Core/Timing` | 事务、撤销、时间坐标、曲线求值和节拍吸附 |
| `Core/Conversion`、`Core/Gameplay` | 路径生成、Catch 事件、RNG、尺寸和 hyperdash |
| `Core/Localization` | 语言表、格式化与校验 |
| `src/FruitsAtelier.App/Editor` | 共享布局、输入、选择、剪贴板和转换缓存 |
| `App/Rendering`、`App/Platform`、`App/Audio` | Windows 宿主及资源管理 |
| `App/Skinning` | 共享皮肤映射、尺寸和裁剪 |
| `src/FruitsAtelier.Mac` | Mac 窗口、画布、音频和原生音频桥接 |
| `tests` | 控制台测试项目 |
| `macOS/tests` | 共享 App / Skinning / SkinArchive 测试的跨平台项目入口 |

表中 `Core/`、`App/` 分别简写对应的源项目目录。Mac 项目通过链接源码复用 `EditorView`、`ICanvas`、皮肤和谱面包处理代码，通过项目引用使用 Core。Core 不引用窗口或图形设备类型。

## Workspace 与曲库

`Core/Workspace` 提供工程目录事务、SQLite 索引、元数据扫描、资源引用诊断和显式导出计划。共享 `EditorView.Library` 负责曲库/设置/导出页面；平台宿主负责文件夹选择、音频切换和资源导出。曲库扫描与星级计算在后台执行，UI 读取完成后的结果。详见 [workspace](WORKSPACE.md)。

## 编辑与转换

宿主将输入映射到 DIP 坐标后交给 `EditorView`。内容修改以事务提交到 `EditorHistory`，一次拖动、批量操作或曲线草稿形成一步撤销。选择和视口作为会话状态单独维护。

转换缓存比较文档快照和 Tiny 补偿设置；变更后同步转换完整文档，再按视口裁剪绘制。语言切换会重建诊断缓存。两视图使用同一转换结果，完整对象序列用于 RNG 和 hyperdash 计算。

绘制经 `ICanvas` 提交，编辑器不持有设备资源。Windows 播放时从 `WM_PAINT` 请求下一次绘制，通过 `Present(1)` 呈现；Mac 由约 16 ms 的定时器请求重绘。播放位置由各平台音频后端提供。

模型及转换流程见[数据模型](PROJECT_MODEL.md)，显示公式见[Catch 绘制与转换](CATCH_RENDERING.md)。

## 文件与资源

Core 负责文本与工程序列化；宿主负责对话框、谱面包提取和资源复制。`.osz` 导入只提取支持的文件，`.osk` 只提取 `skin.ini` 与 Catch PNG。导入器校验路径、重复条目、链接及解压容量，先写临时目录再发布缓存。

皮肤包上限为 256 MiB，选中文件各 16 MiB、总计 64 MiB，ZIP 条目上限为 20000。源码运行时资源缓存位于 `artifacts/beatmaps` 和 `artifacts/skins`；Mac 独立应用使用 `~/Library/Application Support/FruitsAtelier`。可选默认皮肤见[皮肤说明](../assets/skins/README.md)。

## 音频与生命周期

Windows `AudioTransport` 在串行 worker 上处理加载、播放、暂停和 seek，UI 读取不可变状态快照。MP3 连续解码到有界 PCM 缓存；暂停可复用活动 WASAPI 会话，seek 和 EOF 重播重建输出。细节见[Windows 音频](../src/FruitsAtelier.App/Audio/REFERENCE.md)。

Mac 通过 `Native/Audio.m` 调用 AVAudioPlayer，播放位置取自播放器。OGG 先由 NVorbis 解码为有容量上限的 PCM WAV，过期加载结果被丢弃，EOF 后重建播放器。平台操作见[macOS 说明](MACOS.md)。

Windows resize 时先释放 back buffer 对应的 Direct2D target，调整 DXGI buffer 后重建；零尺寸跳过呈现。鼠标捕获丢失或失焦会取消活动交互。Mac 将对应事件映射到相同的编辑器取消方法。

## Interactive editing performance

The editor keeps a per-instance `CatchConversionCache`. Unchanged FSliders, imported
sliders, and banana showers reuse their derived output only when their source content,
conversion settings, timing points, and incoming legacy RNG state match. Cached entries
restore the outgoing RNG state, so a change to an earlier parent invalidates downstream
results wherever the random sequence changes. Failed conversions are not cached.
The normal converter remains available without a cache for export and independent checks.

Canvas and preview rendering select the visible interval from time-sorted catch objects
by binary search. Sidebar labels are formatted only for visible rows, offscreen anchors
are culled, and timeline events share pixel-sized markers (hyperdash takes precedence).
Hit testing rejects distant curve segments before sampling them. Editing snapshots
copy existing identities without generating replacement IDs, and group dragging uses
direct target lookup. Undo/redo still retains independent document snapshots.

Star calculation runs against separate snapshots on a bounded background worker and
waits for an active content drag to finish. See [Catch difficulty](CATCH_DIFFICULTY.md).
