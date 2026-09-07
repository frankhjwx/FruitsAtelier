# 工程与数据模型

默认保存使用 [workspace 工程目录](WORKSPACE.md)：`project.catchdiff` 清单与独立难度文件。本文的 `.catchproj` schema 1/2 描述保留的兼容格式及文档编码。

创作模型通过 UTF-8 JSON `.catchproj` schema 2 持久化（兼容读取 schema 1）；本项目自行解析、输出 stable v14 / Mode=2 `.osu`。工程创作信息、导入上下文和派生输出保持分离。

## 权威数据与派生数据

| 类别 | 当前内容 | 规则 |
| --- | --- | --- |
| 创作数据 | 独立 fruit、轨迹、锚点、逐段类型、控制柄、行程次数、timing / difficulty | 编辑与撤销的权威内容 |
| 导入上下文 | 原始节、对象行、源顺序、完整 timing、slider / 香蕉、资源引用 | 随工程保存，未编辑对象输出保留原表示 |
| 派生结果 | slider 几何路径、SV、F/D/T / 香蕉、RNG、误差、hyperdash | 可重算，不覆盖创作意图 |
| 会话状态 | 对象/锚点选择集合、工具、语言、吸附、视口、transport 时间、皮肤、图层、Tiny 补偿开关、内部批量剪贴板 | 不属于谱面内容 |

## 工程与难度

`BeatmapProject` 保存工程 Name 和 1–256 个 `ProjectDifficulty`；每个难度包含独立 Guid、Version 显示名及完整 `MapDocument`。schema 2 使用 `Project` 外层容器，一次原子保存全部难度；schema 1 的 `Document` 自动包装为单难度工程。资源路径对每个难度分别相对工程目录写入和解析，保持旧文件的路径安全检查。整个工程的 JSON 上限为 32 MiB。

编辑器为每个难度维护独立 `EditorHistory`，通过当前难度访问 `Document`。撤销仅影响当前难度；保存更新全部历史的 baseline。增加难度是工程结构操作，不进入单难度物件撤销栈，保存前保持工程 dirty。当前难度、标签滚动位置、播放头和视口属于会话状态，不写入工程。打开工程默认选第一个难度；切换难度不产生内容历史。工程容器不强制合并不同 `.osu` 的音乐或 timing，避免导入时覆盖原始内容。

## 实际模型

| 类型 | 字段与语义 |
| --- | --- |
| MapDocument | Name、DurationMs、difficulty、TimingPoints、Fruits、Tracks、ImportedSliders、BananaShowers、SourcePath、AudioPath、OriginalSections |
| Fruit | 稳定 Guid Id、TimeMs、X、SourceOrder、OriginalLine |
| CurveTrack | 稳定 Guid Id、Name、Kind（默认 Linear / Bezier）、Nodes、SourceOrder、SpanCount、OriginalLine、CompensateTinyDroplets |
| Anchor | 稳定 Guid Id、TimeMs、X、HandleIn、HandleOut、OutgoingKind（可空） |
| MapPoint | double TimeMs、double X；控制柄中表示相对锚点偏移 |
| TimingPoint | TimeMs、BeatLengthMs、Meter、Uninherited、采样/音量/效果字段、SourceOrder、OriginalLine |
| ImportedSlider | Id、TimeMs、X / Y、PathType、ControlPoints、SpanCount、PixelLength、SourceOrder、OriginalLine |
| BananaShower | Id、TimeMs、EndTimeMs、SourceOrder、OriginalLine |

Difficulty 包含 ApproachRate、CircleSize、SliderMultiplier、SliderTickRate。演示默认时长 30000 ms、拍长 500 ms、offset=0、AR=8、CS=5、SliderMultiplier=1.4、SliderTickRate=1。MapDocument 的 BeatLengthMs / TimingOffsetMs 为无红点时的回退值；真实谱面保留全部 timing 点。

导入 `.osu` 时，`DurationMs` 先由最后一个物件推导；关联音频解码完成后，如果实际音频更长，则完整音频范围可用于放置、数值编辑和拖动物件。加载与导航不修改文档；第一次把物件编辑到原范围之外时，在同一个用户编辑事务中扩展 `DurationMs`。

时间权威值为 double 毫秒，分拍吸附不预先取整。TimingMap 查询当前红点 BPM / offset / Meter 和继承 SV，网格与吸附使用局部拍格及红点边界，绿点不重置相位。每条 slider 锁定起始 timing，沿途变化不修改其速度。切换吸附不修改已有对象或 SliderTickRate。

EditorHistory 使用深复制实现事务、撤销和 dirty 比较，保留对象 ID、逐段类型、行程次数、Tiny 覆盖值、原始行和 timing。保存工程更新基线，不清除撤销重做。视图通过 ContentEquals 失效转换缓存；批量 Legacy 转换使用独立后台快照，完成后校验来源快照再应用。取消活动拖动或草稿恢复整个事务，后续字段按对象 ID 定位，避免写入旧快照。

对象选择集合仅保存完整父对象 ID；同一 slider 的多个派生子对象按 SourceId 去重。B 模式的锚点选择集合限定于当前编辑轨迹，V/F 模式不会局部编辑锚点。框选的开始选择另存快照，Esc 或捕获取消恢复选择，不产生内容历史。

内部剪贴板保存选中完整父对象的深复制快照。粘贴令 `新时刻 = 播放头 + 原时刻 − 所选对象最早开始时间`，保持对象间相对时间、X、几何、相对控制柄、SpanCount 和样本字段，重新分配所有父对象与节点 ID；必要时扩展文档时长，任一对象越界则整批回滚。复制不进撤销栈，批量剪切、删除和每次粘贴各为一个事务；不改剪贴板快照和未选对象，不读写系统剪贴板。

界面语言和语言资源不写入 `.catchproj`。内建默认名在新建对象时通过资源取得；既有 Name、导入元数据和原始行均作为用户数据保留，切换语言不改名。语言变化会使转换诊断缓存失效，不改变转换几何或文档历史。

## FSlider 与 Legacy Slider

轨迹位于 `(timeMs, X)` 平面，段类型为 `Nodes[i].OutgoingKind ?? track.Kind`。null 继承轨迹默认类型；同一轨迹可混合线性和三次贝塞尔。端点时间递增，贝塞尔控制点时间非递减；其时间求值先解 `time(u)`，不能用线性时间比例代替 u。锚点至少间隔 0.001 ms，控制点 X 限制到 0..512。

控制柄保存为相对偏移，锚点移动时一同平移。统一 Slider 工具（B）点击添加无柄点，按住向上拖动设置方向柄；控制点曲线/直线转换通过柄和相邻段类型实现。右键插点默认无柄，邻点有柄时相邻段仍可弯曲，不保证保形；分割按钮按该段类型保留形状及后续类型，不用采样结果覆盖已有锚点和柄。几何 slider 的 Y 不是编辑时间。

批量删除锚点允许端点，保留未删除点的 ID、时刻和顺序。合并相邻段前清除将被激活的旧线性段隐藏柄，并根据剩余可用柄决定段类型；新端点移除不再使用的外向柄。剩余不足两个锚点时 App 删除整个父 slider，撤销恢复完整数据。

`.osu` 读入并保留 L/B/P/C 几何控制点、长度、span 数与原始行的对象称为 Legacy Slider。属性或右键“转换为 FSlider”根据路径弧长和起始速度拟合首 span 的时间—X 轨迹，优先使用直线段，平滑部分使用三次贝塞尔段；这不恢复原作者的控制柄。起终点、转向、平台边界及明显折角优先保留，不吸附到节拍。允许最大 0.25 横向场地单位的拟合误差：在原始折线的每个区间检查三次曲线与直线差值的端点及导数根，超差时继续分段。柄的时间坐标为区间三等分，横向控制点限制在端点范围内并保持方向，避免产生新的反向运动。

转换验证对象数量、类型、顺序、时刻、Fruit/Droplet 横向位置误差及现有 TinyDroplet 贴合规则，再替换源对象。0.25 是导入轨迹拟合容差，与 FSlider 内部 0.0001 对齐容差不同；Legacy 的随机 tiny 偏移不会被当作可编辑轨迹拟合。轨迹近似可能轻微改变星级。重复路径、SV 或 TinyDroplet 约束不可满足时保留原 Legacy Slider。

批量转换一次构建候选并在完整谱面上下文中验证；失败父对象恢复后重新计算下游 RNG。每个 diff 的成功替换合为一个撤销事务，失败对象保留并列出原因。后台任务取消或工程变化时不应用结果。

FSlider 保留原父 Id、SourceOrder、OriginalLine 和 SpanCount。节点只定义首 span，后续 repeat 共用并反向求值；`SpanCount=1` 为单程。新建及由 Legacy 转换的 FSlider 设置 `CompensateTinyDroplets=true`，表示贴合是强约束；`null/false` 仅用于旧工程兼容及底层对照。香蕉雨保存可编辑的开始/结束时间范围；画布上的 X=0–512 矩形和上下手柄是该时间范围的编辑表示，不保存逐根 banana 位置。

## 派生转换

```text
完整 MapDocument
  → 混合段时间—X 求值 / 未编辑导入 L/B/P/C 路径近似
  → 起始 timing 与 SV / 首 span 生成路径与 SpanCount 往返
  → head / tick / repeat / legacy-last-tick / tail、tiny 与香蕉事件
  → 按完整父对象顺序执行 RNG
  → 路径弧长位置 + 随机偏移
  → 按时间稳定排序 F/D/T / 香蕉
  → 误差、失败诊断与 hyperdash
```

`ConvertedCatchObject` 包含 SourceId、EventIndex、Kind、TimeMs、X、TargetX、PathX、RandomOffset、IsStandalone。`X` 是实际位置，`PathX` 是随机偏移前的位置；新曲线的 `TargetX` 是创作目标，导入对象没有额外贴合目标。`(SourceId, EventIndex)` 标识来源并供 hyperdash 绘制共用。

`GeneratedSlider` 保存来源、IsImported、SpanCount、开始时间、总时长、速度、SV、TickDistance、单 span 长度与几何路径、补偿是否应用/成功及最大 tick/tiny 误差。`CatchConversionResult` 提供 Sliders、Objects、Diagnostics、Success 和最大误差。

每条 FSlider 生成一个保留 SpanCount 的 `.osu` slider，按首 span 弧长和行程方向查询实际位置；F/D/T 内部对齐容差为 0.0001 场地单位，最终位置经过 float 运算。几何 Y 在边界折回不额外增加 repeat 或 tick。Legacy Slider 使用原长度裁剪/延长、重复 span 与反向求值。

普通 Droplet 没有横向 RNG 偏移；TinyDroplet 根据实际 RNG 和事件路径进度反求偏移前 X。FSlider 的贴合受 `0..512`、共享 repeat 路径及水平速度约束，自动 SV 可在 stable 的 0.1–10 范围内提高；强约束目标不可达时该轨迹失败，不退回偏离曲线的结果。Legacy Slider 保留 osu 原始 TinyDroplet RNG 偏移。

RNG 固定种子 1337，父对象按开始时间、SourceOrder 稳定排序；无导入顺序的新对象使用确定性集合顺序。一个 stream 的全部 nested RNG 处理完再进入下一个父对象，最后才展开按时间排序。Droplet 消耗旋转随机数，TinyDroplet 使用横向偏移；每根香蕉消耗位置及三次外观随机数，时间保留 float 累加规则。视口裁剪不改变输入。

失败对象不生成结果，整体 Success=false，RNG 仅对应成功生成的子集。路径长度、事件数、导入控制点、repeat、采样和网格均有显式容量限制；具体数值、继承 NaN 的 Catch 处理与源码依据见[转换模块说明](../src/FruitsAtelier.Core/Conversion/UPSTREAM.md)。

Hyperdash 使用完整结果中的 Fruit / Droplet，跳过 TinyDroplet 和香蕉，保留方向和剩余移动量。标记属于起跳对象，CS 改动后重新计算。

## 持久化与输出

`.catchproj` 保存节点、柄、OutgoingKind、轨迹默认 Kind、SpanCount、OriginalLine、CompensateTinyDroplets、difficulty、完整 timing 及资源引用，不写入撤销栈、派生对象或 GPU 缓存。旧工程缺省 OutgoingKind=null、SpanCount=1、Tiny 覆盖=null。读取验证 schema、ID、模型边界和曲线约束，拒绝不支持的字段与版本；继承 NaN 使用 JSON 命名浮点表示。保存采用同目录临时文件后替换，资源路径相对工程目录保存，不复制音频本体。

`.osu` 目前接受并输出 v14 / Mode=2，见[stable 文件规范](STABLE_FORMAT.md)。Legacy Slider 保留原始行；FSlider 按整数时间和路径坐标编码，保留 SpanCount，需要时插入继承 SV 并恢复。由 Legacy 转换的 FSlider 复用原类型/音效/sample 字段；行程次数改变时保留仍存在的边缘样本，新增边缘使用默认值并报告。无法与同时间对象兼容的 SV 冲突明确拒绝。输出回读比较完整对象序列、时间及 X；量化可能产生误差或序列变化。

工程保存与 `.osu` 输出是独立操作，后者不能代替保存创作意图。视频和 storyboard 不加载，原始节文本保留不代表这些资源已打包。
