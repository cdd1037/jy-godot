# 金庸群侠传 · Godot .NET 重制

以 Godot .NET 表现层、纯 C# 游戏核心和中央资源库重制 DOS《金庸群侠传》。原版资源由用户本机导入，剧情和 MOD 共用一个 C# 事件执行器，不引入 Lua。

本目录是独立工程根目录；构建、导入和运行不要求相邻存在 OpenLegend、其他复刻工程或原版游戏目录。

## 开始运行

需要 Godot **4.7.2 .NET** 和 **.NET 10 SDK**，Windows 优先，使用 GL Compatibility 渲染。Godot 可加入 PATH，也可通过 `run.ps1 -Godot` 指定可执行文件。

在本目录执行：

```powershell
.\build.ps1
.\import.ps1 -Source 'D:\Games\LEGEND'
.\run.ps1
```

`import.ps1` 一次导入完整资源库，默认输出 `content/base/`；可用 `-Output` 指定其他新目录。源目录只读，输出不得位于源目录内，也不会覆盖已有输出。所选目录应直接包含完整原版数据，包括 RANGER、ALLSIN、ALLDEF、KDEF、TALK、SDX/SMP、调色板和头像资源。

默认打开场景 **70“主角的家”**。测试启动直接自由行动，通过同一个事件执行器应用开场完成后的状态；播放完整开场用：

```powershell
.\run.ps1 -PlayOpening
.\run.ps1 -Scene 74
```

`-Scene` 选择预览起点。界面中的“预览此场景”复用同一个资源库并重启会话。正常游玩可从场景出口进入世界地图，再走向入口进入场景，沿途保留同一会话。也可用 Godot .NET 编辑器打开 [godot/project.godot](godot/project.godot) 运行；界面支持导入整库或打开已有库。

## 当前内容

已接入主角家的开场、娃娃交谈、8 个箱子、床的休息流程，以及选择、任务变量、物品使用、队伍、人物属性、武功和跨场景数据修改。库中集中保留人物、初始背包、全局脚本和对白、地图事件、共享图像及来源资源缓存。

统一事件执行器有 **52 种标准命令**。原版 68 个编号中 **59 个已有转换**；已知原版 KDEF 中 **882/1,018 段完整编译通过**。这些数字分别表示内部接口与编译兼容，不代表完整剧情运行验收。世界地图行走、乘船规则、入口条件、场景进出与关联跳转已接入；剧情镜头、行走和双对象动画使用同一执行器。战斗、存档、音乐播放、商店和结局仍未实现；世界恢复、天气及待机动画也尚待补齐。

独立编写的任务样例直接叠加到本机基础库，不复制图片或生成新场景包：

```powershell
.\run.ps1 -EventDemo
```

按 E 找面前娃娃接任务，再从行囊使用“测试信物”交付，领取一次奖励。自定义剧情可用 `-StoryOverlay 'content\mods\example.json'`；当前是显式单个剧情叠加，尚无完整 MOD 发现、排序和冲突管理界面。

## 操作

- 方向键 / WASD：行走；默认约 60ms 一格，长按首步后等待 500ms 再连续行走。
- E / 空格 / Enter：交互及确认对话；↑↓ 选择，允许取消时按 Esc。
- 查看背包：查看物品，并对面前物件使用；脚本决定是否消耗。
- F2：切换平滑插值与直接换格，两者移动规则和节奏相同。
- F1：碰撞辅助；鼠标滚轮：缩放。
- 重新开始：恢复当前场景会话，保留当前剧情叠加与开场播放设置。

## 工具与验证

```powershell
dotnet tools/Game.Tools/bin/Debug/net10.0/Game.Tools.dll inspect --library content/base
dotnet tools/Game.Tools/bin/Debug/net10.0/Game.Tools.dll audit-events --source 'D:\Games\LEGEND' --output '.local/opcodes.json'
.\run.ps1 -Smoke -Headless -Report .local/verification/smoke.json
.\run.ps1 -Smoke -TravelSmoke -Headless -Report .local/verification/travel.json
```

`build.ps1` 构建解决方案并运行测试。合成夹具由测试独立编写，不含原作资源。真实资源测试通过 `JINYONG_ORIGINAL_DATA` 或本地 `.local/original-data.txt` 指定来源；缺少原版数据时相应测试明确标记为不可用。完整开场冒烟加 `-PlayOpening`，任务样例冒烟加 `-EventDemo`；画面截图需正常渲染模式，使用 `-Capture` 指定输出。

源码、文档与独立示例纳入版本控制；`content/base/`、旧导入包、`.local/`、Godot 缓存和构建产物不提交。原作资源及其解码结果不分发，项目当前未提供源码许可声明。

参阅 [工程结构](docs/project-layout.md)、[中央资源库](docs/resource-library.md)、[世界旅行](docs/world-travel.md)、[架构](docs/architecture.md)、[事件编写](docs/event-authoring.md)、[指令兼容](docs/original-opcode-coverage.md)、[验证记录](docs/verification.md) 和 [参与开发](CONTRIBUTING.md)。
