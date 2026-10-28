# 工程结构

`JinYong.Godot/` 是完整、独立的仓库根目录。原版数据是导入时选择的外部输入，参考项目只用于研究；构建和运行不依赖它们的相邻目录结构。

```text
JinYong.Godot/
├── JinYong.slnx
├── global.json
├── Directory.Build.props
├── build.ps1
├── import.ps1
├── run.ps1
├── AGENTS.md
├── CONTRIBUTING.md
├── src/
│   ├── Game.Core/           游戏规则、不可变定义、会话与事件执行
│   ├── Game.Content/        运行时内容模型、加载、校验、视图与存档
│   └── Game.Importing/      DOS 解码、脚本编译与整库生成
├── godot/                  Godot .NET 项目与呈现层
│   ├── Scenes/AppRoot.tscn 主场景，继续绑定同一入口脚本 UID
│   └── Scripts/
│       ├── App/            AppRoot 入口、启动、UI、内容、输入、剧情、背包、存档与旅行
│       ├── Battle/         AppRoot.Battle、Controls、Actions、Results、BattleAudio 与结果文字
│       ├── Views/          SceneView、Assets、Exploration、World、Battle 与 DialoguePanel
│       └── Diagnostics/    AppRoot 各项 Smoke 和 SmokeRoute
├── tools/Game.Tools/       导入、检查及诊断命令行工具
├── tests/Game.Tests/       核心、内容与真实资源验证
│   └── Compatibility/     旧地图格式回归夹具生成 helper
├── docs/                   架构、格式和验证说明
├── content/
│   ├── examples/           独立编写的内容示例，纳入版本控制
│   ├── mods/               MOD 内容定义，纳入版本控制
│   ├── base/               导入生成的中央资源库，不提交
│   └── imported/           旧场景包本机留存，不提交
├── .local/                 本地配置、临时目录、验证报告，不提交
└── artifacts/              构建导出物，不提交
```

部分目录按需要创建，不要求通过空占位文件把它们全部加入 Git。项目自有且可分发的素材可以随代码管理；原作导入资源始终留在生成的内容库内。

## 依赖方向

```text
Game.Importing ──→ Game.Content ──→ Game.Core
Godot / Game.Tools / Tests ──→ 上述所需项目
```

`Game.Core` 不读取原版磁盘格式，也不依赖 Godot 类型。它拥有队伍、背包、世界变化、事件状态与游戏规则。

`Game.Content` 负责运行时内容模型、库加载、校验、视图和存档适配。中央 `GameLibrary` 保存公共定义与资源索引，按需创建场景、世界或战场视图，公共人物、对白、程序和图片只存一份。它不引用导入项目；RGB6 调色板等库内低级格式仍由 Content 读取。

`Game.Importing` 引用 Content，将原始 DOS 资源转换为这些公开格式。`Legacy/` 保存归档、RLE/文本解码、原版事件编译、审计和各类导入器；`Imaging/` 保存图集与 PNG 写入工具；`LibraryImportOptions` / `LibraryImportReport` 位于 `JinYong.Importing`。它可以用 `GameLibrary.Load` 回读最终输出，但 Content 不得反向调用它。

Godot 读取核心状态，绘制地图、对话和动画，提交输入并推进时间。宿主保留导入按钮，因此直接引用 Importing；这次拆分不从发行程序剔除导入器，正常库加载只经过 Content。`Game.Tools` 通过 Importing 执行离线导入/审计，通过 Content 检查已导入库。工具和宿主都不能在节点或命令行分支中复制一套剧情规则。

本轮只新增这一个子项目，Battle、Event、Mod 暂不独立为 csproj。`BattleSession` 仍在 Core 的 `Battles/`，按 `Roster`、`Turns`、`Movement`、`Magic` 及已有支持/AI/呈现职责分部；`EventRunner` 仍在 `Events/`，按 `ControlFlow`、`Presentation`、`Timing`、`Persistence` 分部，合同及宿主接口独立为 `EventContracts.cs` / `IEventWorld.cs`。partial 只分开源文件，不创建额外会话、状态或 VM。

Content 中的 `GameLibrary` 按 `Loading`、`Scenes`、`Views` 分开加载校验、场景视图与世界/战场视图；主文件保留库自身的状态与入口。`Packages` 按资源清单、场景与图像、剧情文档、JSON 序列化及 `ScenePackage` 视图分组，文件移动保留原有类型命名空间和数据字段。

Godot 主场景位置保持 `Scenes/AppRoot.tscn`，入口脚本路径改为 `Scripts/App/AppRoot.cs` 并保留原 UID。`Views` 内将资源准备和探索绘制从主 `SceneView` 分开，战斗和世界绘制仍是同一个视图的分部；`Diagnostics` 集中冒烟驱动代码，不改变正式入口与规则归属。

战斗宿主进一步按 `AppRoot.Battle` 主流程、`Controls` 控件、`Actions` 输入提交、`Results` 战果遮罩及 `BattleAudio` 音效分工。`BattleResultsText` 是同一 Host 内不依赖 Godot 类型的纯 C# 格式器，只把冻结结果转为中文分节；它不发奖励、不消费 RNG、不回读当前人物数值。Core 的目标查询提供合法范围、路径和影响格，SceneView 缓存绘制结果，Godot 不复制一套判定规则。

## 新库与兼容代码

新的生产入口使用 `GameLibrary` 和 `content/base/`，不再逐场景导出完整内容包。世界、场景和战斗共用一个资源库与会话；战斗代码与当前边界见[战斗](battles.md)。存档用独立的会话快照保存变化，不复制基础库，见[存读档](persistence.md)。

旧版场景包保留兼容读取。旧生产场景/剧情导入器、有限依赖 profile 和样例包复制器已移除；`tests/Game.Tests/Compatibility/` 只保留旧地图格式夹具生成 helper。验证历史包时必须明确其格式和能力边界，不能用旧包证明中央资源库的新能力。

正式入口为 `build.ps1` 构建和测试、`import.ps1` 一次导入全库、`run.ps1` 加载并运行。`-Scene` 选择预览起点，`-EventDemo` / `-StoryOverlay` 在内存应用剧情数据；没有按场景导出步骤，也不需要生成第二套基础资源。

本轮源码整理及其后的战斗结算阶段、战果页、目标预览和旧按钮保护，均未重新导入资源，也未运行构建、测试或验收。历史 427 项测试及宿主冒烟是整理前的基线，不能证明移动后的脚本、程序集引用或新增流程已经通过。

资源库具体布局见 [中央资源库](resource-library.md)，提交与验证约定见 [参与开发](../CONTRIBUTING.md)。
