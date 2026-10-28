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
│       ├── App/            AppRoot 入口与 Composition 装配；保留启动、存档等根分部
│       ├── Menus/          标题/探索菜单、独立行囊、人物状态与本机设置
│       ├── Battle/         独立 Controller、CommandRouter、面板、模型、格式器与 AudioPlayer
│       ├── Shops/          单件购买 Controller、面板、只读模型与工厂
│       ├── Audio/          规范化音乐调度与 Windows WinMM GM 输出
│       ├── Ending/         终端结局 Controller、面板与逐帧展示模型
│       ├── Views/          GameViewHost、SceneView 及其绘制/资源分部、DialoguePanel
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

`Game.Content` 负责运行时内容模型、库加载、校验、视图和存档适配。中央 `GameLibrary` 保存公共定义与资源索引，按需创建场景、世界或战场视图，公共人物、对白、程序和图片只存一份。`Audio/MidiScore` 和 `Ending` 定义规范化音乐/结局资产及校验，不解码 DOS 原档。它不引用导入项目；RGB6 调色板等库内低级格式仍由 Content 读取。

`Game.Importing` 引用 Content，将原始 DOS 资源转换为这些公开格式。`Legacy/` 保存归档、RLE/文本解码、原版事件编译、审计和各类导入器；`Imaging/` 保存图集与 PNG 写入工具；`LibraryImportOptions` / `LibraryImportReport` 位于 `JinYong.Importing`。它可以用 `GameLibrary.Load` 回读最终输出，但 Content 不得反向调用它。

Godot 读取核心状态，绘制地图、对话和动画，提交输入并推进时间。宿主保留导入按钮，因此直接引用 Importing；这次拆分不从发行程序剔除导入器，正常库加载只经过 Content。`Game.Tools` 通过 Importing 执行离线导入/审计，通过 Content 检查已导入库。工具和宿主都不能在节点或命令行分支中复制一套剧情规则。

src 保持三个项目，Battle、Event、Mod 暂不独立为 csproj。`BattleSession` 仍在 Core 的 `Battles/`，按 `Roster`、`Turns`、`Movement`、`Magic` 及已有支持/AI/呈现职责分部；`EventRunner` 仍在 `Events/`，按控制流、呈现、计时、快照及商店/武林大会续行分部，合同及宿主接口独立为 `EventContracts.cs` / `IEventWorld.cs`。`Shops` 保存纯规则定义，`GameSession` 的相关内聚分部管理库存和音乐请求。partial 只分开源文件，不创建额外会话、状态或 VM。

Content 中的 `GameLibrary` 按 `Loading`、`Scenes`、`Views` 分开加载校验、场景视图与世界/战场视图；主文件保留库自身的状态与入口。`Packages` 按资源清单、场景与图像、剧情文档、JSON 序列化及 `ScenePackage` 视图分组，文件移动保留原有类型命名空间和数据字段。

Godot 主场景位置保持 `Scenes/AppRoot.tscn`，入口脚本路径改为 `Scripts/App/AppRoot.cs` 并保留原 UID。`Views` 内将资源准备和探索绘制从主 `SceneView` 分开，战斗和世界绘制仍是同一个视图的分部。`GameViewHost` 统一提交 `ScenePackage` 与 `SceneView` 的绑定，资源准备和加载成功才发布新视图，再通过 `SessionChanged` 通知战斗控制器重置。

Battle 目录没有 `AppRoot` 分部，采用明确的类级职责：

| 类型 | 职责 |
|---|---|
| `BattleController` | 协调战斗视图、AI 节奏、呈现与结算阶段 |
| `BattleCommandRouter` | 校验有类型的输入意图和旧上下文，委托 Core 执行动作，管理目标选择 |
| `BattleActionPanel` / `BattleActionModel` | 读取模型绘制控件，发出意图，不直接调用 Core |
| `BattleActionModelFactory` | 纯构建动作模型与状态文案 |
| `BattleResultsPanel` / `BattleResultsModel` | 展示冻结战果模型并发出确认 |
| `BattleResultsFormatter` | 纯 C# 生成结果模型和中文分节，不结算、不抽随机、不回读当前人物数值 |
| `BattleAudioPlayer` | 独立 Node，拥有两个播放器、预载与库内缓存生命周期 |

`AppRoot.Composition` 的 `ComposeGameplayComponents` 统一装配 Battle、Shops、Ending、Audio，并订阅模式/输入确认事件；主循环只推进组件，输入按模态优先级转交。探索按钮被明确的容器持有，不再按子节点顺序推断显示范围；AppRoot 不再保存战斗 UI 字段或直接处理战斗动作分支。Core 的目标查询提供合法范围、路径和影响格，SceneView 缓存绘制结果，Godot 不复制一套判定规则。

商店的 `ShopController` 绑定会话和 Core 令牌，`ShopPanel` 读取 `ShopPanelModel` 并发意图，`ShopModelFactory` 只投影名称、余额、价格和库存。交易仍由 Core 执行，一次确认买一件，反馈后收店；没有批量或出售。`MusicController` 调度 Content 的 120Hz 短消息，`WindowsMidiOutput` 只封装 WinMM 系统 GM 设备。`EndingController` 推进终端阶段，`EndingPanel` 展示已规范化图片；两者不读取原始 IDX/GRP，也不恢复旧事件栈。

原版商店读取、XMI 乐谱转换与结局 ENDCOL/ENDWORD/KEND 解码分别归 Importing 的 `LegacyShopImporter`、`LegacyXmiImporter`、`LegacyEndingImporter`，正常运行不反向调用它们。

行动模型只携带文本、选项、可用性和不透明的 `BattleActionToken`，不向面板暴露 `BattleSession` 或人物引用。命令路由器内部保存对应的战斗、行动者、阶段、回合和呈现标识，拒绝旧面板或旧回合发出的操作；重绑和重置会使旧标识失效。

`Diagnostics` 保留准确命名的 `AppRoot.*Smoke` 驱动，通过控制器只读接口和事件收集证据；正式组件不判断 `_smoke`。`Menus` 的 `GameMenuController` 与 `InventoryController` 分别拥有导航和行囊交互，展示模型不暴露核心会话。存档仍为根分部，尚未完成全部 AppRoot 功能组件化。标题启动、暂停和队伍菜单边界见[正式菜单](menus.md)。

独立组件的文件名等于其主类型名；partial 仅拆分同一类型的内聚实现，不作为按业务扩张 AppRoot 的方式。依赖应由构造参数和明确事件表达，不引入全能 Context 或委托袋。src 的三个项目边界保持不变。

## 新库与兼容代码

新的生产入口使用 `GameLibrary` 和 `content/base/`，不再逐场景导出完整内容包。世界、场景和战斗共用一个资源库与会话；战斗代码与当前边界见[战斗](battles.md)。存档用独立的会话快照保存变化，不复制基础库，见[存读档](persistence.md)。

旧版场景包保留兼容读取。旧生产场景/剧情导入器、有限依赖 profile 和样例包复制器已移除；`tests/Game.Tests/Compatibility/` 只保留旧地图格式夹具生成 helper。验证历史包时必须明确其格式和能力边界，不能用旧包证明中央资源库的新能力。

正式入口为 `build.ps1` 构建和测试、`import.ps1` 一次导入全库、`run.ps1` 加载并运行。`-Scene` 选择预览起点，`-EventDemo` / `-StoryOverlay` 在内存应用剧情数据；没有按场景导出步骤，也不需要生成第二套基础资源。

本轮源码整理、战斗收尾及新增六项事件命令、商店/音乐/结局组件，均未重新导入资源，也未运行审计、构建、测试或画面验收。历史 427 项测试及宿主冒烟是整理前的基线，不能证明移动后的脚本、程序集引用、组件生命周期或新增流程已经通过。src 仍为三个项目；增加宿主职责目录不意味着增加程序集或脚本 VM。

资源库具体布局见 [中央资源库](resource-library.md)，提交与验证约定见 [参与开发](../CONTRIBUTING.md)。
