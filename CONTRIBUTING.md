# 参与开发

本目录是独立工程根目录。克隆或复制整个目录即可开发，不需要把 OpenLegend、其他复刻工程或原版游戏放在相邻目录。

开发环境使用 Godot 4.7.2 .NET 与 .NET 10 SDK；SDK 选择以 `global.json` 为准。Godot 编辑器入口是 `godot/project.godot`，解决方案入口是 `JinYong.slnx`。

## 开发约定

- 使用中文沟通。提交信息使用英文、带 scope 的约定式提交，例如 `feat(content): import a shared resource library`、`fix(events): preserve original branch targets`。
- `Game.Core` 保存游戏定义、规则与运行状态。`Game.Content` 保存运行时内容模型，负责加载、校验、内存视图及存档适配。`Game.Importing` 负责 DOS 原档解码、脚本编译和资源库生成；依赖方向为 `Game.Importing → Game.Content → Game.Core`，三者均不引用 Godot，禁止反向依赖。
- Godot 节点负责输入和呈现，通过核心接口推进游戏。宿主保留整库导入按钮，因此也直接引用 `Game.Importing`；正常读取资源库不调用原档导入器。
- 本轮只新增 `Game.Importing` 一个子项目。战斗、事件暂留在 Core，MOD 文档留在 Content；优先按职责拆分文件与 partial class，不为目录名称增加独立程序集。
- MOD 与原版导入结果使用同一套标准事件命令和 C# 执行器，不引入 Lua、动态代码插件或第二个脚本运行时。
- 优先扩展中央 `GameLibrary`。不要恢复“每个场景携带一套人物、背包、对白和公共资源”的生产导出方式；旧格式只保留兼容读取及回归夹具。
- 新格式和原版兼容决定需注明参考文件、数据布局及必要特殊行为。合成测试夹具与真实原作资源验证必须明确区分。
- 时序、分支、数据隔离、内容完整性与路径边界的变化应有对应验证。低风险文档和样式改动无需为了数量添加测试。

目录分工见 [工程结构](docs/project-layout.md)，资源契约见 [中央资源库](docs/resource-library.md)，事件命令见 [事件编写说明](docs/event-authoring.md)。

Godot 脚本按 `Scripts/App`、`Battle`、`Views`、`Diagnostics` 组织，入口仍为 `Scenes/AppRoot.tscn`，绑定 `Scripts/App/AppRoot.cs`。移动带 `.uid` 的脚本时一起保留 UID 并更新场景引用；同一个 partial 类按启动、内容、输入、剧情和呈现职责拆文件，不增加另一份状态。Core 的 `BattleSession` 按出战、回合、移动、武功分部，`EventRunner` 按控制流、呈现、计时和快照分部；它们仍分别是同一个会话和执行器。

正式开发入口为 `build.ps1`、`import.ps1 -Source` 和 `run.ps1`。导入生成整个 `content/base/`；`run.ps1 -Scene` 只选择预览起点。剧情样例使用内存叠加，不复制基础库。旧格式兼容代码只保留读取和测试夹具，不作为新增生产导出接口。

## 验证

在工程根目录执行：

```powershell
.\build.ps1
```

此入口构建解决方案并运行自动化测试。真实资源验证使用自行提供的原版目录，通过 `JINYONG_ORIGINAL_DATA` 环境变量或本地 `.local/original-data.txt` 指定；未提供资源时，相应测试不应假装完成原版验收。

渲染或输入变化需要 Godot 冒烟验证。集中开发期间可以合并相关画面验证，避免频繁启动窗口；提交说明应如实记录完成的检查和待统一检查的项目，不能把编译通过等同于完整游戏可玩。

本轮工程拆分与文件整理依用户要求未运行构建、测试或验收，也未重新导入原版库。既有 427 项测试及历史宿主冒烟属于整理前的版本基线，不能作为新项目引用、移动后的脚本绑定或本轮功能已通过的证据。

## 内容与生成文件

`content/base/` 是本机导入库，`.local/` 是本机配置与验证缓存，`artifacts/` 是导出产物；这些目录均不提交。Godot 缓存、`bin/`、`obj/` 和测试结果同样不提交。

可提交的 MOD 定义与独立编写的示例放在 `content/mods/` 或 `content/examples/`。加入素材前需确认有相应分发权；测试夹具应自行构造，避免夹带原作资源。变更前后的原版源目录必须保持只读。

公共资源去重时必须保留原版 bank 与 localID 映射。编号相同不代表跨 bank 内容相同；去重依据包含尺寸、锚点、索引和覆盖平面的内容哈希。来源缓存保留文件名到 SHA-256 与存储路径的索引，不能通过重命名后丢弃原始编号关系。

项目当前没有提供源码使用或再分发的许可声明。不要默认把源码、原作游戏资源或第三方参考工程视为采用相同的开源许可；也不要未经确认新增许可证。仓库不分发原版资源、DOS 可执行文件、驱动或用户存档。
