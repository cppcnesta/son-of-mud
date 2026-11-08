extends RefCounted
## 在图形运行时检查移动曲线、取消窗口、格挡CD与迁移后的资源。
var driver: Node

func wait_for_recovery(p: CharacterBody2D) -> void:
	for i in 45:
		if p.action.kind == MudActionState.Kind.ATTACK_RECOVERY:
			return
		await driver.frames(1)

func run(test_driver: Node) -> bool:
	driver = test_driver
	Progress.new_game(false)
	Progress.enter_test()
	await driver.load_room("TEST")
	var p: CharacterBody2D = driver.p
	var floor_y: float = driver.room.floor_y
	# 资源曲线本身及两个持续时长；不是仅比较配置字段。
	for duration in [1.0, 1.5]:
		var recovery := MudMovementRecovery.new()
		recovery.start(MudMovementRecovery.Source.DODGE if duration == 1.0 else MudMovementRecovery.Source.ATTACK, duration, 0.2, p.mud_config.recovery_curve)
		driver.check(absf(recovery.multiplier() - 0.2) < 0.001, "%.1f秒后摇从80%%减速开始" % duration)
		var samples: Array[float] = [recovery.multiplier()]
		for step in 4:
			recovery.tick(duration * 0.25)
			samples.append(recovery.multiplier())
		driver.check(absf(samples[1] - 0.55) < 0.005 and absf(samples[2] - 0.8) < 0.005 and absf(samples[3] - 0.95) < 0.005 and samples[4] == 1.0, "%.1f秒曲线：20%%→55%%→80%%→95%%→100%%，无反向或超速" % duration)
	# 实际攻击后摇可移动，但不能连点攻击；前1秒不能格挡。
	await driver.reset_actor(Vector2(400, floor_y - 24), 1)
	driver.check(p.combat.start(p.config(), false), "泥普通攻击进入独立前摇")
	await wait_for_recovery(p)
	driver.check(p.action.kind == MudActionState.Kind.ATTACK_RECOVERY and absf(p.recovery.remaining - 1.5) < 0.04 and p.recovery.source == MudMovementRecovery.Source.ATTACK, "有效判定后进入1.5秒移动减速后摇")
	var start_x: float = p.position.x
	Input.action_press("move_right")
	await driver.frames(15)
	Input.action_release("move_right")
	driver.check(p.position.x > start_x + 8 and p.position.x < start_x + 70, "泥攻击后摇期间实际可移动，受到高额减速")
	driver.check(not p.combat.start(p.config(), false), "泥攻击后摇不能连点重开攻击")
	driver.check(not p.start_block() and p.guard.cooldown_remaining == 0.0, "泥攻击后摇前1秒拒绝格挡，失败不消耗CD")
	for i in 80:
		if p.combat.recovery_remaining <= 0.45:
			break
		await driver.frames(1)
	Input.action_press("block")
	driver.check(p.start_block() and p.action.kind == MudActionState.Kind.BLOCK, "泥攻击后摇最后0.5秒允许格挡")
	var previous_interval: float = p.combat.attack_interval_remaining
	var before_cd: float = p.guard.cooldown_remaining
	driver.check(p.receive_attack(1, "feel_mud_parry", p.position) == "blocked" and p.combat.bonus_ready and p.guard.cooldown_remaining == before_cd and p.combat.attack_interval_remaining == previous_interval, "泥成功格挡保留增伤，不获得水形态的CD/攻击间隔刷新")
	await driver.frames(3)
	Input.action_release("block")
	await driver.frames(3)
	driver.check(p.action.kind == MudActionState.Kind.FREE and not p.combat.start(p.config(), false), "提前松开泥格挡，不绕过尚未结束的攻击间隔")
	await driver.frames(40)
	driver.check(p.recovery.remaining == 0.0 and p.combat.is_ready(), "原攻击收势到期后正常恢复攻击")
	# 从后摇前段、中段、末段闪避，均替换攻击收势。
	for wait_ticks in [0, 35, 65]:
		await driver.reset_actor(Vector2(400, floor_y - 24), 1)
		p.combat.start(p.config(), false)
		await wait_for_recovery(p)
		await driver.frames(wait_ticks)
		driver.check(p.start_dodge(), "泥攻击后摇第%d帧允许Shift衔接" % wait_ticks)
		driver.check(p.combat.recovery_remaining == 0.0 and p.combat.attack_interval_remaining == 0.0 and p.recovery.source != MudMovementRecovery.Source.ATTACK, "Shift清除攻击收势及隐藏计时，不叠加后摇")
		for i in 40:
			if p.action.kind != MudActionState.Kind.DODGE:
				break
			await driver.frames(1)
		driver.check(p.recovery.source == MudMovementRecovery.Source.DODGE and absf(p.recovery.remaining - 1.0) < 0.04 and absf(p.penalty_multiplier() - 0.2) < 0.03, "Shift结束后由1秒曲线减速完整覆盖攻击减速")
		await driver.frames(30)
		driver.check(absf(p.penalty_multiplier() - 0.8) < 0.04, "实际物理帧：闪避后0.5秒恢复至约80%移速")
		await driver.frames(32)
		driver.check(p.recovery.remaining == 0.0 and p.combat.is_ready() and p.action.kind == MudActionState.Kind.FREE, "Shift收势结束后没有残留攻击后摇")
	# 水格挡冷却，从按下起算，失败保留，成功清零。
	await driver.reset_actor(Vector2(400, floor_y - 24), 0)
	Input.action_press("block")
	await driver.frames(2)
	driver.check(p.action.kind == MudActionState.Kind.BLOCK and p.guard.cooldown_remaining > 2.9 and p.guard.cooldown_remaining <= 3.0, "水格挡开始时启动3秒CD")
	Input.action_release("block")
	await driver.frames(3)
	driver.check(not p.start_block(), "水格挡未成功时不能提前再次格挡")
	var cd_before_switch: float = p.guard.cooldown_remaining
	driver.check(p.switch_form() and p.guard.cooldown_remaining == cd_before_switch and not p.start_block(), "切换泥形态不清除水格挡CD")
	await driver.frames(185)
	driver.check(p.guard.cooldown_remaining == 0.0, "格挡CD到期自然恢复")
	await driver.reset_actor(Vector2(400, floor_y - 24), 0)
	p.combat.attack_interval_remaining = 1.0
	Input.action_press("block")
	await driver.frames(2)
	driver.check(p.receive_attack(1, "feel_water_parry", p.position) == "blocked", "水格挡成功处理有效攻击")
	driver.check(p.guard.cooldown_remaining == 0.0 and p.combat.attack_interval_remaining == 0.0 and p.action.kind == MudActionState.Kind.FREE, "水成功格挡清零格挡CD与攻击间隔")
	await driver.frames(5)
	driver.check(p.action.kind == MudActionState.Kind.FREE, "水成功格挡后持续按住不会自动再次格挡")
	Input.action_release("block")
	driver.check(p.combat.start(p.config(), false), "水成功格挡后可立即发起反击")
	# 泥重压下砸也使用攻击收势规则，避免保留旧的硬直分支。
	await driver.reset_actor(Vector2(400, floor_y - 160), 1)
	p.action.enter(MudActionState.Kind.SLAM)
	await driver.frames(20)
	driver.check(p.action.kind == MudActionState.Kind.ATTACK_RECOVERY and p.combat.can_move_in_recovery() and p.recovery.source == MudMovementRecovery.Source.ATTACK and p.recovery.duration == 1.5, "泥重压下砸落地后使用同一1.5秒可移动攻击收势")
	driver.check(p.start_dodge() and p.combat.recovery_remaining == 0.0, "泥下砸攻击收势也可由Shift覆盖")
	for i in 40:
		if p.action.kind != MudActionState.Kind.DODGE:
			break
		await driver.frames(1)
	driver.check(p.recovery.source == MudMovementRecovery.Source.DODGE and p.recovery.duration == 1.0, "泥下砸后的Shift最终只保留1秒闪避收势")
	# 实际换场：冷却和曲线进度按时间继续，不重新开始或消失。
	await driver.reset_actor(Vector2(400, floor_y - 24), 1)
	p.recovery.start(MudMovementRecovery.Source.DODGE, 1.0, 0.2, p.mud_config.recovery_curve)
	p.recovery.remaining = 0.75
	p.guard.cooldown_remaining = 2.0
	p.combat.attack_interval_remaining = 0.6
	RoomTravel.enter_room("M01", "Start", true)
	await driver.frames(10)
	driver.room = driver.get_tree().current_scene
	driver.p = driver.room.get_node("Player")
	p = driver.p
	driver.check(driver.room.room_id == "M01" and p.guard.cooldown_remaining > 1.7 and p.guard.cooldown_remaining < 2.0 and p.combat.attack_interval_remaining > 0.3, "实际换场保留格挡CD与攻击间隔")
	driver.check(p.recovery.source == MudMovementRecovery.Source.DODGE and p.recovery.remaining > 0.45 and p.recovery.remaining < 0.75 and p.recovery.duration == 1.0, "实际换场保留曲线来源、剩余时间与总时长")
	# 目录、正式文案与七门事件均为当前实际资源检查。
	var world: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/data/whitebox/world.json"))
	var copy_is_clean := true
	var scenes_exist := true
	for id in world.rooms:
		if id == "TEST":
			continue
		scenes_exist = scenes_exist and ResourceLoader.exists("res://assets/scenes/whitebox/levels/%s.tscn" % id, "PackedScene")
		for entry in world.rooms[id].events:
			for forbidden in ["原型", "占位", "暂定", "接口", "PDF", "CG1", "CG3", "CG5", "尚未制作"]:
				copy_is_clean = copy_is_clean and not (entry.title + entry.text).contains(forbidden)
	driver.check(scenes_exist and not DirAccess.dir_exists_absolute("res://assets/scenes/mainline") and not DirAccess.dir_exists_absolute("res://assets/scenes/testing"), "23关卡与测试场仅存在于whitebox目录，旧位置已移除")
	driver.check(copy_is_clean and world.rooms.H01.events.size() == 7, "23关卡文案已清理开发说明，H01七门事件全部保留")
	driver.check(DirAccess.dir_exists_absolute("res://assets/art/characters/player") and DirAccess.dir_exists_absolute("res://assets/art/environment/tilesets") and DirAccess.dir_exists_absolute("res://assets/art/audio/sfx"), "正式角色、环境、音频资源目录已预留")
	await driver.snapshot_image("runtime_combat_feel")
	return true
