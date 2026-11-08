# 动作手感与目录调整

## 本次规则

|项目|实现|
|---|---|
|泥攻击后摇|1.5秒。保留攻击间隔，允许移动，以高额减速代替原来的站定硬直；泥重压下砸也使用相同收势|
|泥闪避后摇|1秒移速减速|
|起始减速|沿用80%惩罚，即起始移速为正常值的20%|
|恢复方式|两者共享可编辑Curve资源；默认恢复进度为2t−t²，t为已过去时间/总时长|
|格挡衔接|泥攻击后摇前1秒不能格挡，最后0.5秒可以；仍需格挡CD就绪|
|闪避衔接|泥攻击后摇任何时点可使用已冷却的Shift。立即终止攻击收势与攻击间隔；闪避结束后只保留新的1秒闪避减速|
|水格挡CD|按下时开始3秒冷却。格挡成功立即清零CD及攻击间隔；仍需松开再按才能开启下一次格挡|
|泥格挡CD|本次未指定数值，暂设3秒，可在mud.tres调整。成功格挡仍给予原有下一次有效命中增伤，不获得水的CD刷新|
|切换/过门|保留格挡CD、攻击间隔、曲线来源和剩余时间，不借切换清除或重开计时|

原有水0.5秒单次格挡、泥最长2秒持续格挡、攻击前摇/判定阶段以及形态独立能力标记均保留。水普通攻击的0.16秒后摇未调整。模组2达到跳跃上限后的自动落地波仍使用原触发资格，不作为新的手动攻击动作重播。

Shift后的1秒为移速惩罚，沿用原来的动作可用性；不会额外锁住攻击1秒。攻击后摇被格挡衔接时，移动减速和原攻击间隔继续自然计时，不能靠提前松开格挡绕过攻击间隔。

## 曲线数值

|恢复进度|闪避后时间|攻击后时间|实际移速|
|---|---|---|---|
|0%|0秒|0秒|20%|
|25%|0.25秒|0.375秒|55%|
|50%|0.5秒|0.75秒|80%|
|75%|0.75秒|1.125秒|95%|
|100%|1秒|1.5秒|100%|

曲线形状为当前手感选择，并非唯一方案。修改`assets/data/player/curves/recovery_ease_out.tres`中的曲线即可调整；起始倍率由`penalty_initial_multiplier`控制。不会出现负移速或末段超速。

## Inspector

|资源|字段|当前值|
|---|---|---|
|assets/data/player/mud.tres|penalty_duration|1秒|
|同上|attack_recovery|1.5秒|
|同上|soft_attack_recovery|true，收势中可移动|
|同上|block_cancel_window|0.5秒|
|同上|dodge_cancel_recovery|true|
|同上|penalty_initial_multiplier|0.2|
|同上|block_cooldown|3秒，暂定|
|assets/data/player/water.tres|block_cooldown|3秒|
|同上|reset_block_cooldown_on_success|true|
|同上|reset_attack_interval_on_block|true|
|两个形态资源|recovery_curve|恢复曲线资源|

## 目录

|目录|内容|
|---|---|
|assets/scenes/whitebox/levels|M01～M06、T01～T06、C01～C08、H01～H03，共23关|
|assets/scenes/whitebox/tests|独立训练场|
|assets/scenes/whitebox/legacy|此前的mp_*与Room01练习场景，仅留档|
|assets/scenes/player|共享玩家场景|
|assets/scenes/ui|主菜单|
|assets/data/player|形态、规则与恢复曲线资源|
|assets/data/whitebox|房间尺寸/连接/剧情数据、文案基线、白盒TileSet|
|assets/textures/whitebox|测试贴图|
|assets/reference/whitebox|地图源文件|
|assets/art|正式角色、环境、UI、特效、字体与音频预留目录；尚未填入正式资源|
|assets/scripts/player/components|攻击、格挡、移动收势、动作与形态配置模块|
|assets/scripts/whitebox|关卡生命周期、几何构建、事件与测试敌人|
|assets/scripts/world|接口与房间切换|
|assets/scripts/combat、progression、ui、core|通用判定、存档进度、界面和输入|

所有资源引用与project.godot入口已更新。玩家控制器仍只有一套。旧`room_protal.gd`改名为`world/room_portal.gd`，原target_room/target_spawn接口保持兼容。

## 文案

23关卡事件及七道门内容保留。场内移除了“占位、原型安排、接口、CG待制作”等开发说明；房门显示目的地名称，敌人与Boss显示角色名称及战斗状态。开发限制与尚缺资源记录在文档中。

训练场、中文HUD和必要操作提示保留。文案基线为`assets/data/whitebox/scene_copy.json`，生成后写入`world.json`；正常运行只读取world.json。

## 验证

最新完整结果见`Test_Report.md`、`runtime_suite.log`与`runtime_results.json`。专项结果见`combat_feel_suite.log`。检查在实际Godot4.7.2兼容渲染窗口执行，包含曲线采样、真实移动、取消窗口、CD刷新、实际换房与文件路径检查。

正式美术、完整Boss动作与连续真人手感验收仍未完成。
