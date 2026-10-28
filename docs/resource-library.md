# 中央资源库

中央 `GameLibrary` 是基础内容与 MOD 共用的读取入口。原版资源只导入一次，人物、初始背包、公共事件程序、对白和共享贴图不再随每个场景重复导出。默认库位于 `content/base/`，默认打开场景 70。

生成端位于 `Game.Importing`：`JinYong.Importing.Legacy.LegacyGameImporter` 读取 DOS 原档，使用该项目中的解码器、脚本编译器与图集写入器。`LibraryImportOptions` 和 `LibraryImportReport` 位于 `JinYong.Importing`。读取端及共享格式模型留在 `Game.Content`，依赖方向为 `Game.Importing → Game.Content → Game.Core`；Content 不调用导入器。Godot 因保留导入按钮仍引用 Importing，普通库加载不经过 DOS 转换。

src 保持 Core/Content/Importing 三个项目。本轮保持格式版本 3、资源 ID、图集去重和原始 bank 映射，新增商店定义、规范化音乐及结局资产；未重新导入资源，旧库不会自动补齐新程序和这些资产，须导入到新目录。战斗、事件与 MOD 不另建 csproj。

## 格式 3 布局

```text
content/base/
├── manifest.json           format_version = 3，库入口与完整性信息
├── data/
│   ├── gameplay.json       公共人物、初始背包、脚本、对白、商店及游戏定义
│   ├── scene-index.json    场景索引与原始场景元数据
│   ├── world.layers.bin    集中保存所有场景的地图层
│   ├── world.events.bin    集中保存所有场景的事件记录
│   ├── main-map.bin        世界地图五层 480×480 原始编号
│   ├── battles.json        战斗、武功、物品与原版成长规则
│   ├── battlefields.bin    集中保存两层 64×64 战场
│   ├── sprites.json        资源 bank / 原始 ID 到共享图片资产的映射
│   ├── audio.json          事件/攻击音效、原始音乐及规范化乐谱索引
│   ├── music/01.json…      GAME01–24 的 120Hz MIDI 短消息乐谱
│   ├── ending.json         结局调色板、字图描述及动画偏移
│   ├── ending.pixels.bin   规范化结局字图/透明度及 320×200 动画像素
│   └── source-index.json   原始文件名到哈希、大小、存储路径与种类
└── assets/
    ├── palette.rgb6        原始 VGA 调色板
    ├── sprites/            按内容去重后的共享图集
    └── source/             按 SHA-256 寻址的原始资源白名单缓存
```

这份文档定义目录和职责，具体 JSON 字段及二进制定位以内容层模型与读取器为准。不得让运行时通过猜测原始文件名或写死本机路径绕过索引。

Content 继续负责库中的小端地图/事件/战场读取、PNG 尺寸核验、RGB6 调色板展开和路径/哈希校验。调色板公共入口为 `JinYong.Content.Imaging.VgaPalette.DecodeRgb6`；导入侧旧 `LegacySpriteDecoder.ReadPalette` 只代理它。原始 IDX/GRP 及 RLE/Big5 转换属于 Importing，不是运行时加载前提。

## 公共定义与场景视图

`data/gameplay.json` 集中保存角色定义、初始队伍和背包、世界旅行初始状态、商店初始定义，以及全局脚本与对白。`GameLibrary.OpenScene` / `OpenWorldView` 创建内存视图，不重新复制或导出公共内容。库定义与 `GameSession` 的可变状态分开，开箱、队伍变化、事件修改和地图修改由同一会话保存。正常场景出口与世界入口切换保留会话；界面切换预览会另建会话。

`LegacyShopImporter` 读取 RANGER entry 5：原版五条记录，每条 30 字节，依次是五个物品 ID、五个库存、五个价格，均为小端 16 位字，固定对应场景 1/3/40/60/61，默认货币物品 174。定义写入 `shops`，运行库存独立并进入可选快照 `shop_stocks`；不在每个视图复制一套交易状态。用于独立测试的局部合成 RANGER 可只含前 N 条完整记录，不能把这种夹具称为原版整库导入结果。

原版地图归档具有 100 个地图及事件记录，而 RANGER 只有 84 条场景元数据。两者分别保留，不通过臆造名称、入口或元数据把数量补齐。`data/scene-index.json` 表达实际可用的元数据与地图记录关系，84–99 记录保留零占位元数据及 `has_legacy_metadata=false`。缺少图像 bank 时 `OpenScene` 明确拒绝可行走预览。

`data/world.layers.bin` 与 `data/world.events.bin` 各只有一份，按连续场景编号排列。每场景地图为六层 64×64 的小端 int16，共 49152 字节；事件为 200×11 个小端 int16，共 4400 字节。场景索引负责定位对应记录。`main-map.bin` 按 Earth、Surface、Building、BuildX、BuildY 保存五层 480×480 小端 int16，共 2304000 字节；后两层保留建筑拥有者坐标，前三层保留图片编号。

## 图片与来源资源

不同资源 bank 中相同的原始图片编号不必代表相同图片，因此图片引用包含 bank 身份。`data/sprites.json` 用 `banks[bank][localID]` 得到资产 ID，再从 `assets[assetID]` 定位共享图集。场景 bank 使用 `scene/000` 等名称，世界地图使用 `world`，两者保留双倍原始图片编号；头像 bank 使用 `portrait`，其编号规则独立。世界与场景图像经过同一次共享图集打包，不创建第二套资源目录。

原版 84 个场景 bank 的主角 28 帧，编号与字节内容已确认全部相同；仍保留每个 bank 的原始映射，共享由内容去重决定。哈希包括尺寸、锚点、索引及覆盖平面，而不是按 localID 跨 bank 直接合并。游戏层不依赖图集页数或打包顺序。

来源缓存保留后续解码所需的白名单游戏资源。`data/source-index.json` 以原始文件名为键，记录 `sha256`、`size`、`storage`、`kind`，存储路径指向 `assets/source/` 中按哈希寻址的内容。音效后缀为 `.wav`，音乐为 `.xmi`，其他数据为 `.bin`。原始编号、来源文件名与存储文件分离，不因内容去重丢失映射。

战斗采用同一张全局图像资产表：`battlefield/000` 等 bank 保留 WDX/WMP 双倍编号及 EOF 帧，空帧不按 SDX 别名处理；`effect` 使用 `13000+2*i`，`fight/000` 等使用 `16000+2*i`。全部仍经一次共享图集打包。定义、武功和原物品字位于 `battles.json`，战场位于统一 `battlefields.bin`，不再导出战斗专用包。

`GameLibrary.OpenBattleView` 保留当前战场的静态图像映射，另为共享 `effect` 和本战所需的 `fight/NNN` 建立内存 bank 视图。出战确认前就包括全部可选队员，以及指定、强制出战者和敌人使用的头像 bank。不同 FIGHT bank 都从 16000 编号，不能将它们合并成单个 ID 字典；运行时按 bank 和本地 ID 查找。Godot 先准备本次视图的帧映射和纹理，再提交资源切换，继续复用同一库的图集缓存。缺失 bank 或帧明确报告，不从其他头像 bank 任意借帧；合法空帧保持为空，不隐藏整片战场。

`Z.DAT` 只在导入时抽取经指令布局校验的经验表与成长常量，程序自身不进入 source vault；其指纹登记在 `source_hashes`，因此来源指纹数可能大于缓存文件索引数。运行只读取解析后的数据。

`data/audio.json` 的 `effects` 与 `attacks` 分别供效果和攻击 WAV 使用。战斗呈现计划区分预载与开始播放，宿主用两个独立播放器允许两类声音重叠；WAV 按库内路径缓存，切换资源库时停止播放并释放旧缓存。音效来源仍是同一 source vault，不另复制战斗音频。

`music` 保留 GAME*.XMI 的 source vault 路径，`scores` 指向 `data/music/01.json` 至 `24.json` 的规范化 `MidiScore`。索引键沿用文件编号 1–24，Core 的曲目 ID 为 0–23，宿主通过 `scores[musicId+1]` 定位。`LegacyXmiImporter` 离线解析单序列 XMIDI、音符时长和支持的循环，输出有序 120Hz MIDI 短消息；不支持的结构拒绝导入，不在运行时猜测分支或解码 XMI。乐谱和其他生成文件一起纳入 `manifest.file_hashes`。

Godot 的 `MusicController` 调度乐谱，由 `WindowsMidiOutput` 经 WinMM 使用 Windows 系统 GM 合成器，不包含 DOS 驱动或 FM 合成器，音色和混音不等同原 DOS。Core 负责入场/离场/战斗曲目、66 同曲重启及 8 的一次离场覆盖。本轮未构建或试听，缓存与源码接线不能当作音乐验收；旧库只有原 XMI 时会明确要求重导入。

结局原资源 ENDCOL.COL、ENDWORD.IDX/GRP、KEND.IDX/GRP 仍保留在同一 source vault。`LegacyEndingImporter` 在导入时一次生成 `data/ending.json` / `data/ending.pixels.bin`：23 张字图保留索引与透明度，221 帧动画各为 320×200 索引像素，另存展开调色板和连续偏移。部分提供结局原档时拒绝整次导入；没有结局文件的合成小库可以省略，运行到结局时明确要求重导。

Content 的 `EndingAssetsLoader` 核对 manifest 哈希、23/221 数量、调色板、尺寸、透明度和连续布局；宿主只将规范化内容转成纹理，不解析 IDX/GRP、不生成临时碎片缓存。呈现保留 443 帧滚字、221 帧电影、3244 帧名单及两次按键确认，时长采用近似值，不声称 DOS 帧时钟或像素演出已验收。

缓存只能接纳导入器明确列出的游戏数据格式；不递归复制原版目录，不缓存 DOS 可执行文件、驱动、配置和用户存档。源目录保持只读，生成库必须位于源目录之外。读取库文件时使用受库根约束的相对路径，校验哈希及必要的数据尺寸，不能接受越出库目录的路径。

## MOD 与兼容边界

自行编写的 MOD 定义位于 `content/mods/`，示例位于 `content/examples/`，与生成的基础库分开管理。当前通过 `run.ps1 -StoryOverlay` 显式加载单个 `StoryDocument`；`-EventDemo` 是同一机制的快捷入口，不生成或复制资源包。

叠加场景必须匹配当前视图，追加的程序、对白、物品、变量和不可用程序 ID 不得与基础定义冲突。它使用同一 `GameLibrary` 和事件执行器；尚未实现多 MOD 自动发现、排序和任意替换补丁。重新开始保留当前叠加，会话进度重新初始化。

剧情仍由唯一的 C# `EventRunner` 执行。扩展 50 需要独立的格式转换支持，不能按原版 50 猜测解析；当前不引入 Lua 或动态程序集插件。

格式 1/2 的旧场景包只通过兼容读取保留。旧生产导入器、有限动态依赖 profile 和样例包复制器已移除；`tests/Game.Tests/Compatibility/` 仅保留旧地图回归夹具生成 helper。生产导入和默认启动使用格式 3 中央库。

## 版本控制与分发

整个 `content/base/`、旧 `content/imported/`、`.local/` 和 `artifacts/` 都被 Git 忽略。可提交内容限于源码、文档、独立编写的 MOD/示例以及具有分发权的素材。原作资源与其解码结果均不随仓库分发；资源哈希只证明导入来源和完整性，不授予分发权。

导入数量、耗时和运行验证结果应由实际报告记录，不写成格式保证。报告不得把“缓存成功”“能够解码”或“脚本编译通过”当作完整游戏运行验收。

新增战斗 bank 视图、动画与音效，以及本轮六项事件转换和商店/音乐/结局资源管线，均未重新导入、构建、测试或验收，也未重跑脚本审计。既有 427 项测试属于旧版本基线，998 接受/20 拒绝属于旧编译审计，不能作为本轮改动已经验证的证据，也不能推算新库数量。
