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
│   └── Game.Content/        内容库、格式转换、校验与资源解析
├── godot/                  Godot .NET 项目与呈现层
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
Game.Core
   ↑
Game.Content
   ↑
Godot / Game.Tools / Tests
```

`Game.Core` 不读取原版磁盘格式，也不依赖 Godot 类型。它拥有队伍、背包、世界变化、事件状态与游戏规则。

`Game.Content` 把原版或 MOD 转成核心定义，负责内容引用和资源文件边界。中央 `GameLibrary` 保存公共定义与资源索引，按需创建某一场景的视图，公共人物、对白、程序和图片只存一份。

Godot 读取核心状态，绘制地图、对话和动画，提交输入并推进时间。`Game.Tools` 负责离线导入与诊断，同样可直接复用内容层。工具和宿主都不能在节点或命令行分支中复制一套剧情规则。

## 新库与兼容代码

新的生产入口使用 `GameLibrary` 和 `content/base/`，不再逐场景导出完整内容包。默认显示场景 70，可通过正常出口进入世界地图，再从入口进入场景，始终复用同一会话。库内保留战斗等资源不表示已实现战斗或存档。

旧版场景包保留兼容读取。旧生产场景/剧情导入器、有限依赖 profile 和样例包复制器已移除；`tests/Game.Tests/Compatibility/` 只保留旧地图格式夹具生成 helper。验证历史包时必须明确其格式和能力边界，不能用旧包证明中央资源库的新能力。

正式入口为 `build.ps1` 构建和测试、`import.ps1` 一次导入全库、`run.ps1` 加载并运行。`-Scene` 选择预览起点，`-EventDemo` / `-StoryOverlay` 在内存应用剧情数据；没有按场景导出步骤，也不需要生成第二套基础资源。

资源库具体布局见 [中央资源库](resource-library.md)，提交与验证约定见 [参与开发](../CONTRIBUTING.md)。
