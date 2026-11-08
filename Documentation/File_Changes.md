# 实际修改与新增文件

## 修改已有文件

|路径|用途|
|---|---|
|project.godot|主入口、自动加载、基础分辨率、补充输入；Compatibility保留|
|assets/scripts/player/player.gd|唯一玩家控制器：形态、动作请求、运动与空中上下文|
|assets/scripts/world/room_travel.gd|复用切场入口，增加冷却/减速传递、重试、出生点与存档定位|
|assets/scripts/world/room_portal.gd|修正原protal拼写，保留接口与条件，正式化目的地提示|

关卡与练习场景统一迁入scenes/whitebox。共享玩家场景迁入scenes/player，保留节点、UID与碰撞尺寸；所有引用同步。地图源文件、白盒贴图、正式美术预留目录分开管理。旧练习场景不参与新23房间连接图。

## 新增实现文件

|路径|用途|
|---|---|
|assets/art/README.md|正式资源预留目录规范|
|assets/data/player/curves/recovery_ease_out.tres|可编辑恢复曲线，默认2t−t²|
|assets/data/player/mud.tres|泥形态可编辑参数|
|assets/data/player/prototype_rules.tres|全局暂定规则可编辑实例|
|assets/data/player/water.tres|水形态可编辑参数|
|assets/data/whitebox/scene_copy.json|正式化场景文案基线|
|assets/data/whitebox/whitebox_tileset.tres|原白盒TileSet资源迁移并更新测试贴图引用|
|assets/data/whitebox/world.json|23场景尺寸、48条有向接口、出生点、事件与未连接扩展接口|
|assets/reference/whitebox/Muddson.ai|原地图源文件迁入白盒参考目录|
|assets/scenes/player/player.tscn|共享玩家场景；保留节点、UID及碰撞尺寸，更新模块引用|
|assets/scenes/ui/main_menu.tscn|F5入口|
|assets/scenes/whitebox/legacy/mp_01_pit.tscn|原练习场景移入白盒留档目录，不接入主线|
|assets/scenes/whitebox/legacy/mp_02_pilgrim_path.tscn|原练习场景移入白盒留档目录，不接入主线|
|assets/scenes/whitebox/legacy/mp_03_altar.tscn|原练习场景移入白盒留档目录，不接入主线|
|assets/scenes/whitebox/legacy/Room01.tscn|原练习场景移入白盒留档目录，不接入主线|
|assets/scenes/whitebox/levels/C01.tscn|独立主线房间C01，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/C02.tscn|独立主线房间C02，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/C03.tscn|独立主线房间C03，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/C04.tscn|独立主线房间C04，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/C05.tscn|独立主线房间C05，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/C06.tscn|独立主线房间C06，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/C07.tscn|独立主线房间C07，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/C08.tscn|独立主线房间C08，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/H01.tscn|独立主线房间H01，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/H02.tscn|独立主线房间H02，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/H03.tscn|独立主线房间H03，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/M01.tscn|独立主线房间M01，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/M02.tscn|独立主线房间M02，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/M03.tscn|独立主线房间M03，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/M04.tscn|独立主线房间M04，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/M05.tscn|独立主线房间M05，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/M06.tscn|独立主线房间M06，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/T01.tscn|独立主线房间T01，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/T02.tscn|独立主线房间T02，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/T03.tscn|独立主线房间T03，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/T04.tscn|独立主线房间T04，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/T05.tscn|独立主线房间T05，共享白盒构建逻辑|
|assets/scenes/whitebox/levels/T06.tscn|独立主线房间T06，共享白盒构建逻辑|
|assets/scenes/whitebox/mp_01_pit.tscn|旧编辑器路径兼容入口，继承legacy场景，不复制关卡内容|
|assets/scenes/whitebox/mp_02_pilgrim_path.tscn|旧编辑器路径兼容入口，继承legacy场景，不复制关卡内容|
|assets/scenes/whitebox/mp_03_altar.tscn|旧编辑器路径兼容入口，继承legacy场景，不复制关卡内容|
|assets/scenes/whitebox/tests/form_lab.tscn|独立形态/能力测试场|
|assets/scripts/combat/attack_ledger.gd|同一攻击ID去重|
|assets/scripts/combat/breakable.gd|重压下砸障碍及持久破坏|
|assets/scripts/core/input_defaults.gd|仅补缺项输入|
|assets/scripts/player/components/action_state.gd|动作枚举、独立计时与中文名称|
|assets/scripts/player/components/form_config.gd|水泥形态配置资源类型|
|assets/scripts/player/components/movement_recovery.gd|统一曲线减速、覆盖规则与跨房间快照|
|assets/scripts/player/components/player_combat.gd|前摇/判定/后摇、命中查询、增伤与落地范围攻击|
|assets/scripts/player/components/player_guard.gd|格挡窗口、CD、成功刷新与攻击后摇衔接|
|assets/scripts/player/components/prototype_rules.gd|正式初始状态、切换/无敌/泥模组/Boss原型规则|
|assets/scripts/progression/progress.gd|能力/进度/检查点/磁盘存档与测试隔离|
|assets/scripts/ui/debug_hud.gd|中文状态、操作与交互提示|
|assets/scripts/ui/main_menu.gd|新流程/继续/测试场|
|assets/scripts/whitebox/room_event.gd|交互/叙事节点|
|assets/scripts/whitebox/whitebox_enemy.gd|占位敌人/Boss预警、攻击、受伤、僵直、两阶段与死亡|
|assets/scripts/whitebox/whitebox_events.gd|独立剧情交互、街区谜题和能力获取事件|
|assets/scripts/whitebox/whitebox_geometry.gd|独立白盒几何、平台、教学设施与测试池构建|
|assets/scripts/whitebox/whitebox_room.gd|房间生命周期、接口、Boss流程与模块调度|
|assets/textures/whitebox/kenney_platformer.webp|原测试贴图迁入白盒贴图目录|
|Run_Tests.ps1|一键导入及图形测试；使用独立测试数据目录|
|tests/build_world.py|可复现房间数据/场景/接口文档生成基线|
|tests/combat_feel_checks.gd|曲线、收势衔接、格挡CD与目录回归|
|tests/package_delivery.py|引用/日志校验、清单与打包|
|tests/repair_scene_migration.py|保留场景内容、修复旧依赖、建立兼容入口并修正编辑器路径历史|
|tests/runtime_suite.gd|真实图形/物理/输入运行检查|
|tests/runtime_suite.tscn|运行检查入口|
|tests/scene_migration_checks.gd|32个场景的依赖、实例化与图形运行回归|

## 交付文档和证据

README_Whitebox_V1_0.md：运行入口、操作、Inspector、确认/暂定规则、进度、未完成项。
Room_Manifest.md：完整尺寸、接口、目标房间与出生点。Test_Report.md：实际结果与未验证范围。
Jump_Landing_Fix.md：本次jump_speed空引用的原因、修复与回归证据。
Combat_Feel_Update.md：当前手感规则、确认/暂定参数、目录与文案调整。
Scene_Path_Repair.md：旧场景缺失依赖的原因、修复和本次验证范围。
runtime_results.json及配套日志/截图：可复查的运行证据。map_source_preview.png：实际.ai的本地渲染参考。
Original_Project_20261105.zip：修改前工程备份（project.godot和assets）。
