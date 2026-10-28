# 架构与格式依据

## 依赖与状态归属

```text
Game.Importing ──→ Game.Content ──→ Game.Core
Godot / Game.Tools / Game.Tests ──→ 上述所需项目
```

`Game.Core`、`Game.Content` 与 `Game.Importing` 都不引用 Godot。Core 拥有规则与状态；Content 保存运行时格式、加载校验、视图和存档适配；Importing 拥有 DOS 解码、编译及库生成。Content 不反向引用 Importing。Godot 保留整库导入按钮，因此也直接引用 Importing，但正常打开已有库只需 Content 的读取流程。

`GameSession` 拥有位置、朝向、背包、物品 ID 标记、变量、队伍和世界运行状态，并驱动唯一的纯 C# `EventRunner`。Godot 读取场景、人物和呈现状态，提交输入，不直接拥有剧情规则。

不可变定义与可变状态分开。`GameLibrary` 集中持有公共内容及文件索引；`StoryCatalog.Party` / `World` 是初始定义，`GameSession.Party` / `World` 是本次会话的副本。`GameSession.Scene` 与 `SceneWorldState.Scene(currentId)` 指向同一份当前场景状态；其他场景副本按需创建，修改不回写原始库或原作目录。

本轮只新增 `Game.Importing` 一个子项目。Battle 与 Event 留在 Core 的职责目录，MOD 使用 Content 的共享文档，不分别建立 csproj。`BattleSession` 将出战、回合、移动和武功分别放入 `Roster` / `Turns` / `Movement` / `Magic` 分部；`EventRunner` 按 `ControlFlow` / `Presentation` / `Timing` / `Persistence` 分部，合同与接口拆为 `EventContracts.cs` 和 `IEventWorld.cs`。这些都是同一个类型的源文件整理，没有增加会话副本或第二 VM。

Godot 脚本分为 `Scripts/App` 的根入口与装配、`Scripts/Battle` 的独立战斗组件、`Scripts/Views` 的视图宿主/绘制/对话，以及 `Scripts/Diagnostics` 的冒烟驱动。主场景仍为 `Scenes/AppRoot.tscn`，绑定 `Scripts/App/AppRoot.cs` 并保留原脚本 UID。`SceneView` 按资源准备、探索、世界和战斗绘制保留内聚分部；背包、存档等仍是 AppRoot 分部，未声称全部根功能已经组件化。

`BattleController` 协调视图切换、AI 节奏、呈现与结算，`BattleCommandRouter` 校验有类型的意图与上下文、委托 Core 并管理选格。`BattleActionPanel` 只读模型并发意图，`BattleActionModelFactory` 纯构建模型/状态文案；`BattleResultsPanel` 只读结果模型并发确认，`BattleResultsFormatter` 只格式化冻结数据；`BattleAudioPlayer` 独立管理双播放器与缓存生命周期。Battle 目录没有 AppRoot partial，不通过全局根上下文取得依赖。

`GameViewHost` 是绑定 `ScenePackage` 与 `SceneView` 的唯一提交入口。加载成功才发布新 Package/Session，并以 `SessionChanged` 通知控制器自行重置，失败保留当前绑定。`AppRoot.Composition` 创建组件、接模式与输入确认事件，并管理显式探索控件容器；根不再持有战斗 UI 字段或直接分支处理战斗动作。

诊断通过控制器只读接口及 `ActionExecuted` / `ResultConfirmed` 订阅计数，正式组件不读取 `_smoke`。独立组件文件名应与主类型一致；partial 只服务同一类型的内聚实现，避免把业务模块铺成 AppRoot 分部，也不引入全能 Context 或委托袋。src 项目边界未因此改变。

## 中央库与导入

`Game.Importing` 中的 `LegacyGameImporter` 从外部原版目录一次生成格式 3 库。RANGER 提供人物、队伍、物品和场景元数据；ALLSIN/ALLDEF 提供全部地图与事件；KDEF/TALK 提供全局程序与对白。导入所有 KDEF 记录，对完整可编译脚本生成标准命令，对不可编译脚本保存拒绝原因。26 号动态脚本编号不再依赖有限的场景 profile：后续全局程序或其不可用原因都已登记。原版编译器、导入器与审计器使用 `JinYong.Importing.Legacy` 命名空间，图集与 PNG 写入器位于 `JinYong.Importing.Imaging`。

生产入口不包含旧逐场景导出器、场景剧情收集器或任务样例复制器。格式 1/2 只兼容读取，旧地图生成 helper 位于 `tests/Game.Tests/Compatibility/` 服务于回归测试。

格式 3 的 `data/gameplay.json` 集中保存公共游戏定义；`data/world.layers.bin` 和 `data/world.events.bin` 集中保存全部场景记录；`data/scene-index.json` 保存索引与原始元数据关系。原版有 100 份地图/事件和 84 条元数据，另外 16 个槽位明确标记没有原始元数据，不虚构入口或图片 bank。

`GameLibrary.Load` 校验文件哈希、相对路径、地图长度、索引、图集矩形、资源映射和公共定义。`OpenScene` 创建当前图的内存 `ScenePackage` 视图，带回同一个 Library 引用，不生成新文件。预览校验出生点；原版切图使用独立入口合同，不误把可到达的阻挡入口当作非法数据。界面预览新建会话；正常游历始终保留同一会话。

共享 JSON 文档、`ScenePackage`、存档封套及运行时库读取均留在 Content。Content 仍解析已导入的地图/事件/战场小端字与图集尺寸，不依赖 DOS 归档解码器。RGB6 调色板由 `Content.Imaging.VgaPalette.DecodeRgb6` 读取，Importing 的旧调色板入口只作代理，避免内容层反向引用导入项目。本轮不改变输出格式、编号或资源打包算法。

完整写入临时同级目录并回读校验后，导入器才发布最终目录；原目录保持只读，不覆盖已有输出。具体文件布局见 [中央资源库](resource-library.md)。

## 图像、音频与原始映射

图像先按 `(bank, localID)` 定位，再映射到内容哈希对应的共享资产。场景 bank 保留原版双倍图片编号；头像等其他 bank 按各自的原始编号解析。相同内容可共享图集矩形，但不根据编号相同就跨 bank 合并。

已核对原版 84 个场景 bank 中主角四方向、每方向七张的 28 帧：编号和内容全部相同。它们可以去重为共享资产，场景 bank 到原始编号的映射仍保留。去重哈希包含尺寸、锚点、调色板索引和独立覆盖平面，避免仅比较 RGBA 外观而丢失原始含义。

`assets/palette.rgb6` 保存 VGA 调色板，通道按 `(v << 2) | (v >> 4)` 展开；共享图集和索引/覆盖数据位于 `assets/sprites/`。有效色号 0 不被自动当作透明。

`assets/source/` 按内容哈希缓存白名单原始资源；`data/source-index.json` 保存原始文件名、`sha256`、`size`、`storage`、`kind`。音效保留 `.wav` 后缀，音乐保留 `.xmi`，其他源数据使用 `.bin`。`data/audio.json` 分别索引事件音效、攻击音效和音乐。事件 WAV 已接通；战斗控制器取出 Core 声音队列并交给独立 `BattleAudioPlayer`，按预载/开始指令驱动攻击与效果播放器，缓存随资源库切换或节点退出释放。XMI 音乐仍未播放。新增音效及组件接线尚未运行验收，缓存资源不证明对应呈现可用。

## 移动与呈现

`SceneWalker` 在核心管理方向顺序、移动节奏与插值。默认每格 60ms、首步后的长按延迟 500ms、停止约 1 秒后回到站立帧，以近似手感为目标，不模拟 BIOS。首步立即提交坐标，连续步保留帧间余数，卡顿后不补走一串格子。抵达呈现端点后先检查自动事件，再接受下一步。

切向不重置首次长按截止时间，松开当前方向后回退到最近仍按住的方向。剧情或模态窗口后需要释放方向键才能重新移动；同帧按下又松开的短点按也会采样。步行图像每次移动尝试推进偏移 `2,4,6,8,10,12`，包括撞墙。F2 只切换插值与直接换格，规则、节奏和到达事件时机不变。

碰撞依次检查边界、建筑、高度差、阻挡事件和原版地形区间。建筑哨兵 15000 不绘制但仍阻挡；事件索引 0 有效，-1 表示无事件。投影为 `screenX=(x-y)*18`、`screenY=(x+y)*9-height`，显示沿用 x-major 两遍顺序；人物在插值半程切换遮挡格子是重制呈现策略。

## 队伍与世界规则

`PartyDefinition` 保留 182 字节人物记录的 91 个原始字、六个队伍槽和物品使用者关系。`PartyState` 管理加入/离队、装备与修炼占用、休息、属性、武功和人物携物。经验与武功熟练字按无符号值解释，身份及姓名字节不可通过属性命令覆盖。

主背包只保存正数量，物品槽 ID 另存为集合，保留原版 18/43 及 50 的 ID 存在语义。重复/满员邀请无副作用；招募携物的非正数量只保留 ID，不扣主背包。属性、背包、携物和变量运算检查 32 位溢出；26 号脚本编号增量保留有符号 16 位回绕。

`Game.Core.Items` 负责装备/修炼资格和探索与战斗共用的药效。装备及修炼只改同一 `PartyState` 的占用和角色字段，基础定义不变、库存不扣；服药与友方暗器通过会话共享背包消费。敌方携物独立。原生物品效果保留原 word 算术与随机顺序，区别于剧情变量和一般属性命令的 32 位运算，见[物品与修炼](items.md)。

事件可跨场景修改字段、单格、整层替换、入口条件、退队事件和商店位置。跨场景商店迁移复用执行器原有 RNG，不建立第二套随机流。`WorldMapDefinition` 保存五层 480×480 地图，`WorldTravelRules` 处理步行、船只、双入口和轻功条件。`GameSession.Advance` 是事件、旅行与战斗呈现的唯一时钟：抵达事件完成后判断出口，淡出后请求宿主准备资源，准备成功才提交位置与当前场景，再淡入处理入口事件。失败退回原视图；会话、队伍、背包、变量、RNG 和各场景修改不重建。世界恢复/天气仍未实现；新增战斗决策与时序呈现的边界见战斗文档。

## 事件与 MOD

原版字节码仅在 Importing 编译；运行时只执行标准命令。当前 55 种标准命令对应 62 个已接入原版编号，已知原版 998/1,018 段完整编译通过。镜头、剧情行走、双动画和战斗继续由同一 Runner 暂停/恢复。`BattleSession` 处理战棋动作，友方引用会话人物，敌方从不可变基线创建；`FinishBattle` 结算后仅凭实际结果消费令牌并恢复原调用栈，事件与战斗共用 RNG。15/24 用同一执行器移交终端生命周期，成功载入后废弃旧栈，不返回其后命令。原机器公式、敌方决策和呈现边界见 [战斗](battles.md)，其余口径见 [指令兼容](original-opcode-coverage.md)。

战斗权威数值同步提交，FIGHT/EFT 和伤害阶段读取动作前后快照；`IsPresenting` 在序列完成前锁住新动作，玩家药效提示使用令牌确认。敌方计划跨移动呈现保留一次决策，续步不重新抽取随机。最近武功槽目前仅保留于单战，未做 DOS 逐帧动态差分。

稳定自由行动状态可通过核心快照保存。内容层用独立存档封套保存库身份、实际剧情叠加和会话变化；宿主准备完整候选视图后才替换当前会话，失败保留旧状态。不复制基础资产，不依赖原安装路径，不重跑开场，见[存读档](persistence.md)。

对话、选择、等待、动画与渐变在同一个 Runner 内暂停/恢复。确认使用单调令牌，执行预算与最近 64 条追踪限制错误循环。开始前预检当前程序和子程序需要的能力，运行中因库存不足或溢出失败时不自动回滚之前的动作。13/14 的渐变暂取 450ms；逻辑顺序保留，时长不是 DOS 动态测量结果。

`StoryDocument` 可在打开场景时显式叠加到公共定义，在内存中创建新视图；`-EventDemo` 走同一入口，不复制资源。当前追加程序、对白、物品和变量定义时拒绝重复 ID，不提供多 MOD 自动发现、排序和任意替换。重启保留当前叠加，运行变化仍只属于会话。不引入 Lua、动态程序集或第二个事件运行时。

## 只读参考依据

OpenLegend 只用于研究，以下是其源码或证据路径，不是工程构建依赖：

- `src/resource/packed_archive.cpp`、`legacy_sprite.cpp`：累计索引、sentinel、空帧别名和 RLE。
- `include/openlegend/model/game_snapshot.hpp`：人物、地图与事件布局。
- `src/scene/scene.cpp`：移动、绘制、人物帧、事件、队伍及商店规则。
- `research/evidence/functions/Z_DAT/0x2C319.md`：68 项原版宽度与 dispatch 合同。
- `research/evidence/functions/Z_DAT/0x300D9.md`：55 参数别名；`0x2E278.md`、`0x2E28A.md`：淡入淡出方向和阻塞顺序。
- `research/evidence/tick-pacing.md`、`src/input/key_repeat.cpp`：移动节奏参考。

格式测试、真实资源剧情验证和 DOS 程序动态差分是不同层级。当前不声称已完成 DOS 逐帧动态对齐或整个原版游戏复刻。

新增战斗行为及本轮项目拆分、Godot 脚本移动、类级重构与根装配均未运行构建、测试或画面验收，也未重新导入资源。既有 427 项测试与历史宿主冒烟只对应整理前的版本，不能证明本轮程序集依赖、场景绑定、组件生命周期或功能已经通过。
