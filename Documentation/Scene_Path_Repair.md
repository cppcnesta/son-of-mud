# 旧场景加载失败修复

截图中的mp_01_pit.tscn来自旧编辑器路径。目录迁移后，编辑器中保留的旧场景又保存到了原位置，其依赖仍指向迁移前路径，因此弹出加载错误。

## 已修复的依赖

|旧引用|当前资源|
|---|---|
|res://assets/scenes/tilesets/whitebox_tileset.tres|res://assets/data/whitebox/whitebox_tileset.tres|
|res://assets/scenes/player.tscn|res://assets/scenes/player/player.tscn|
|res://assets/scripts/room_protal.gd|res://assets/scripts/world/room_portal.gd|

旧位置的mp_01、mp_02、mp_03现在是轻量兼容入口，继承whitebox/legacy中的对应场景。关卡地形、出生点和玩家内容保留，没有新增第二套控制器，也不加入23关卡主线。保留了旧入口和留档场景各自的UID，避免原有链接冲突。

同时修正了编辑器历史记录中的旧文件路径，没有清空整个编辑器缓存或项目设置。当前弹窗是在修复前生成的；关闭弹窗，重新打开工程后从主菜单运行即可。当前主线入口仍为project.godot指定的assets/scenes/ui/main_menu.tscn。

## 实际验证

Godot4.7.2，Windows图形窗口，Compatibility/OpenGL渲染。

- 旧位置的3个兼容入口、legacy中的4个练习场景、训练场、主菜单、23个主线场景，共32个场景逐一加载并进入场景树运行。
- 兼容入口验证继承后的Ground、Player与Spawns节点。
- 资源导入日志无错误；本次专项检查结果见scene_migration_suite.log与scene_migration_results.json。
- 本次未改变玩家动作规则，因此没有重复执行上一轮305项完整玩法检查；其结果作为历史记录保留。
