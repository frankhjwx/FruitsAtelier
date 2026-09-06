# 本地化维护

应用自有 GUI 文案、状态提示与 Core 用户诊断通过 `FruitsAtelier.Localization.Strings` 读取，首次启动默认语言为 `en`，不跟随操作系统语言。顶部按钮可切换当前可用语言，用户选择保存在系统应用数据目录的 `FruitsAtelier/language.json`，后续启动优先恢复该选择；未保存选择或设置损坏时使用英文。已存在的谱面标题、对象 Name、皮肤名和用户文件内容是数据，不随语言切换翻译或改写。

## 语言表与新增词条

- 主表：[en.json](../src/FruitsAtelier.Core/Localization/en.json)。英语定义完整键集合。
- 中文表：[zh-CN.json](../src/FruitsAtelier.Core/Localization/zh-CN.json)。键必须与主表一致。
- 调用入口：`using L = FruitsAtelier.Localization.Strings;`，再使用 `L.Get("所属模块.语义键", 参数...)`。

新增自有界面文案时，先给英文主表和全部语言表增加同名键，再从代码读取。完整句子放在资源中，动态名称、数量和数值作为参数传入；不要在界面代码中拼接翻译后的词语来组成句子。格式字段名、扩展名、协议标记、源文件原文等机器数据不作翻译。

使用 .NET 复合格式占位符 `{0}`、`{1:F3}`、`{2:0.######}`。不同语言可以调整顺序，但参数编号集合应保持一致；字面花括号使用 `{{` 和 `}}`。数字按所选语言文化格式化，`.osu` 与工程的机器数值仍由文件模块按其格式规则写出。

内建默认名称也从资源读取，仅在新建对象或确实缺少元数据时使用；当前中文表保留这些名称原有的英文数据值。不得遍历既有文档并在切换语言时重新赋值。语言变化需要使缓存的自有诊断失效或重建，不能保留旧语言字符串作为新界面提示。

## 新增语言

在 `src/FruitsAtelier.Core/Localization` 增加 UTF-8 `<culture>.json`，例如 `fr-FR.json`，复制主表全部键并翻译字符串，包括语言按钮本身的文字。文件名采用有效文化名称。

Core 项目用 `Localization/*.json` 嵌入资源，运行时枚举这些资源得到 `AvailableLanguages`；添加同键 JSON 后重新构建即可发现，无需维护硬编码语言列表。此机制不读取运行目录中的外置覆盖文件，也不在运行中监视 JSON 改动。

## 校验与检查

`Strings.Validate()` / `LocalizationCatalog.Validate()` 检查缺失或多余键、复合格式是否合法，以及各语言参数编号是否匹配。JSON 解析拒绝重复键和非字符串值。缺少翻译时回退英文；主表未知键显示 `[键名]`，便于发现遗漏。

在项目根目录运行 `python src/FruitsAtelier.Core/Localization/audit.py`，检查语言表、直接引用的资源键及明显残留的 GUI 字面量。静态扫描不是 C# 语法解析器，仍需配合 .NET 测试和实际界面检查。

维护后运行现有 Core 本地化测试和 App 界面测试，再实际切换语言检查菜单、属性、工具提示、错误、数字和窄窗口布局。确认切换不修改文档、dirty 状态或已有名称。测试入口为 `tests/FruitsAtelier.Core.Tests/LocalizationTests.cs`。

系统和第三方异常保留原始信息，可加本地化的外层说明。语言资源自举错误使用独立消息，避免递归加载语言表。
