extends Node
## 在实际图形/物理运行时执行。可复现的输入测试，不冒充人工全流程通关。
var failures: int = 0
var checks: int = 0
var metrics: Dictionary = {}
var lines: Array[String] = []
var room: Node2D
var p: CharacterBody2D
var world: Dictionary

func _ready() -> void:
	call_deferred("run")

func check(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures += 1
	var line := "Tests: %s %s" % ["PASS" if condition else "FAIL", message]
	lines.append(line)
	print(line)

func frames(count: int) -> void:
	for i in count:
		await get_tree().physics_frame

func release_inputs() -> void:
	for key in InputMap.get_actions():
		Input.action_release(key)

func load_room(id: String) -> void:
	release_inputs()
	RoomTravel.enter_room(id, "Start", false)
	await frames(8)
	room = get_tree().current_scene
	p = room.get_node("Player")
	await frames(3)

func reset_actor(at: Vector2, form: int = 0) -> void:
	release_inputs()
	p.restore_transient({"form": form})
	p.position = at
	p.health = 10000
	await frames(5)

func measure_jump(form: int, module: int, hold_frames: int) -> Dictionary:
	await reset_actor(Vector2(400, room.floor_y - 24), form)
	Progress.mud_module = module
	var origin := p.position
	var peak := 0.0
	var bursts: int = p.combat.landed_bursts
	Input.action_press("jump")
	Input.action_press("move_right")
	var launched := false
	for i in 160:
		if i == hold_frames:
			Input.action_release("jump")
		await frames(1)
		peak = maxf(peak, origin.y - p.position.y)
		if not p.is_on_floor():
			launched = true
		if launched and p.is_on_floor():
			break
	var result := {"height": peak, "distance": p.position.x - origin.x, "bursts": p.combat.landed_bursts - bursts}
	release_inputs()
	return result

func snapshot_image(name: String) -> void:
	if DisplayServer.get_name() == "headless":
		return
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png("res://Documentation/" + name + ".png")

func run() -> void:
	reparent(get_tree().root)
	Progress.allow_disk = false
	Progress.new_game(false)
	world = JSON.parse_string(FileAccess.get_file_as_string("res://assets/data/whitebox/world.json"))
	check(Engine.get_version_info().string.begins_with("4.7.2"), "实际Godot版本4.7.2")
	check(DisplayServer.get_name() != "headless", "实际图形运行：%s / %s" % [DisplayServer.get_name(), RenderingServer.get_current_rendering_method()])
	check(world.rooms.size() == 24, "23主线场景+1测试场，无旧版多余房间")
	if OS.get_cmdline_user_args().has("--scene-paths-only"):
		var scene_suite: RefCounted = load("res://tests/scene_migration_checks.gd").new()
		var scene_checks_done: Variant = await scene_suite.run(self)
		check(scene_checks_done == true, "场景迁移与旧入口回归完整执行")
		finish_report("scene_migration_results")
		return
	if OS.get_cmdline_user_args().has("--feel-only"):
		await combat_feel_checks()
		finish_report("combat_feel_results")
		return
	if OS.get_cmdline_user_args().has("--jump-landing-repro"):
		await reproduce_landing_bug()
		finish_report("jump_landing_repro_results")
		return
	if OS.get_cmdline_user_args().has("--jump-landing-only"):
		await jump_landing_checks()
		finish_report("jump_landing_results")
		return
	if OS.get_cmdline_user_args().has("--attacks-only"):
		await attack_checks()
		finish_report("attack_results")
		return
	if OS.get_cmdline_user_args().has("--stairs-only"):
		await climb_checks()
		await final_patch_checks()
		await attack_checks()
		finish_report("final_patch_results")
		return
	Progress.enter_test()
	await load_room("TEST")
	# 通过真实输入事件路径检查Q绑定，区别于直接调用切换方法。
	var key_event := InputEventKey.new()
	key_event.physical_keycode = KEY_Q
	key_event.pressed = true
	Input.parse_input_event(key_event)
	await frames(3)
	check(p.form == 1, "物理键Q通过InputMap触发形态切换")
	key_event.pressed = false
	Input.parse_input_event(key_event)
	await frames(2)
	for key in ["double_jump", "wall_climb", "heavy_slam", "dive"]:
		Progress.test_abilities[key] = false
	var water: MudFormConfig = p.water_config
	var mud: MudFormConfig = p.mud_config
	check(water.move_speed > mud.move_speed and water.dodge_cooldown < mud.dodge_cooldown and water.damage < mud.damage, "两形态配置相对关系")
	# 测量真实移动。
	for form in [0, 1]:
		await reset_actor(Vector2(400, room.floor_y - 24), form)
		var start: float = p.position.x
		Input.action_press("move_right")
		await frames(60)
		Input.action_release("move_right")
		metrics["move_%d" % form] = p.position.x - start
	check(metrics.move_0 > metrics.move_1 and metrics.move_1 > 250, "真实移动1秒：水%.1fpx，泥%.1fpx" % [metrics.move_0, metrics.move_1])
	for form in [0, 1]:
		await reset_actor(Vector2(400, room.floor_y - 24), form)
		var start: float = p.position.x
		p.start_dodge()
		var count := 0
		while p.action.kind == MudActionState.Kind.DODGE and count < 60:
			await frames(1)
			count += 1
		metrics["dodge_%d" % form] = p.position.x - start
		if form == 1:
			check(absf(p.penalty_remaining - 1.0) < 0.04 and absf(p.penalty_multiplier() - 0.2) < 0.03, "泥闪避结束：1秒曲线减速，初始移动倍率0.2")
			var cooldown: float = p.dodge_cooldown
			p.switch_form()
			check(p.dodge_cooldown == cooldown and p.penalty_remaining > 0.95, "切换不刷新冷却、不取消泥减速")
			await frames(30)
			check(absf(p.penalty_multiplier() - 0.8) < 0.04, "泥减速0.5秒：曲线恢复至约0.8")
			await frames(32)
			check(is_zero_approx(p.penalty_remaining) and is_equal_approx(p.penalty_multiplier(), 1), "泥减速1秒后恢复1.0，不反向")
	check(metrics.dodge_0 > metrics.dodge_1, "实际闪避距离：水%.1fpx，泥%.1fpx" % [metrics.dodge_0, metrics.dodge_1])
	# 水0.5秒、一次成功；按住不重开。
	await reset_actor(Vector2(400, room.floor_y - 24), 0)
	Input.action_press("block")
	await frames(2)
	check(p.action.kind == MudActionState.Kind.BLOCK, "水按下K进入格挡")
	check(p.receive_attack(10, "water_a", p.position) == "blocked", "水成功格挡一次")
	check(p.receive_attack(10, "water_a", p.position) == "duplicate", "同一攻击重复信号不重复计伤/格挡")
	check(p.receive_attack(10, "water_b", p.position) == "hurt", "水格挡成功即结束，第二个不同攻击可造成伤害")
	await frames(45)
	check(p.action.kind != MudActionState.Kind.BLOCK, "持续按住水格挡不自动续挡")
	await reset_actor(Vector2(400, room.floor_y - 24), 0)
	Input.action_press("block")
	await frames(2)
	await frames(25)
	check(p.action.kind == MudActionState.Kind.BLOCK and p.action.remaining <= 0.1, "水0.5秒窗口：第27帧仍处于末段格挡")
	await frames(7)
	check(p.action.kind == MudActionState.Kind.FREE, "水0.5秒窗口：第34帧已结束，无按住续挡")
	# 泥2秒多次，增伤不叠加，打空不消耗，命中消费。
	await reset_actor(Vector2(1800, room.floor_y - 24), 1)
	Input.action_press("block")
	await frames(2)
	check(p.receive_attack(10, "mud_a", p.position) == "blocked" and p.receive_attack(10, "mud_b", p.position) == "blocked", "泥可格挡不同有效攻击")
	check(p.combat.bonus_ready, "泥成功格挡发放不叠加的增伤资格")
	await frames(122)
	check(p.action.kind == MudActionState.Kind.FREE, "泥格挡最长2秒，持续按住不无限防御")
	release_inputs()
	p.combat.start(mud, false)
	await frames(120)
	check(p.combat.bonus_ready, "攻击挥空保留格挡增伤")
	p.position.x = 1855
	p.facing = 1
	Input.action_press("attack")
	await frames(120)
	Input.action_release("attack")
	check(not p.combat.bonus_ready and p.combat.last_damage > mud.damage, "实际攻击命中测试靶后消费增伤")
	# 两模组轨迹与落地资格。
	metrics.water_jump = await measure_jump(0, 0, 100)
	metrics.mud1_jump = await measure_jump(1, 0, 100)
	metrics.mud2_short = await measure_jump(1, 1, 18)
	metrics.mud2_full = await measure_jump(1, 1, 100)
	check(metrics.mud1_jump.bursts == 0 and metrics.water_jump.bursts == 0, "普通落地与模组1普通跳不产生范围攻击")
	check(metrics.mud2_full.height > metrics.mud2_short.height + 30 and metrics.mud2_full.height <= 176.5, "模组2长按更高，单跳硬上限176px")
	check(metrics.mud2_full.bursts == 1 and metrics.mud2_short.bursts == 0, "模组2仅达到上限的落地产生一次范围伤害")
	await reset_actor(Vector2(400, room.floor_y - 280), 1)
	await frames(80)
	check(not p.reached_cap, "普通跌落不获得长按跳跃上限资格")
	p.reached_cap = true
	p.jump_in_progress = true
	var bursts: int = p.combat.landed_bursts
	p.restore_transient(p.export_transient())
	await frames(5)
	check(not p.reached_cap and p.combat.landed_bursts == bursts, "过门重置空中上下文，不误触落地范围攻击")
	await reset_actor(Vector2(400, room.floor_y - 24), 0)
	Input.action_press("jump")
	await frames(15)
	var used: int = p.jumps_used
	p.switch_form()
	check(p.jumps_used == used and p.jump_form == 0, "空中切换保留跳跃次数和起跳纵向配置")
	release_inputs()
	await frames(80)
	await snapshot_image("runtime_lab")
	# focused用于修改后的动作/门控回归，避免重复已通过的全部接口检查。
	if OS.get_cmdline_user_args().has("--focused"):
		await progression_checks()
		await climb_checks()
		finish_report("focused_results")
		return
	# 解析/实例化所有房间、每个接口真实切换与出生点定位。
	Progress.leave_test()
	Progress.new_game(false)
	for id in world.rooms:
		if id == "TEST":
			continue
		await load_room(id)
		check(room.room_id == id and is_instance_valid(p), "主线场景运行 " + id)
		for connection in world.rooms[id].ports:
			var target_data: Dictionary = world.rooms[connection.target]
			var found := false
			for back in target_data.ports:
				if back.target == id:
					found = true
			check(found, "接口有独立目标出生点 " + connection.id)
	# 实际调用每个门，包括两个方向。解锁条件已单独测，不跳过出生点验证。
	for key in ["double_jump", "wall_climb", "heavy_slam", "dive"]:
		Progress.unlock(key)
	for key in ["boss_M05", "boss_T04", "boss_H03", "tutorial_double", "tutorial_wall", "flood_done", "met_igi", "district_C04", "district_C05", "district_C06", "seven_gates", "shortcut_T06_T05"]:
		Progress.mark(key)
	for id in world.rooms:
		if id == "TEST":
			continue
		for connection in world.rooms[id].ports:
			await load_room(id)
			RoomTravel.arrival_lock = 0
			var portal := room.get_node(connection.id)
			portal.interact(p)
			await frames(9)
			room = get_tree().current_scene
			p = room.get_node("Player")
			var spawn := room.get_node_or_null("Spawns/" + connection.spawn)
			check(room.room_id == connection.target and spawn != null and absf(p.position.x - spawn.position.x) < 2 and absf(p.position.y - spawn.position.y) < 15, "实际传送/出生 " + connection.id)
			await frames(3)
			check(get_tree().current_scene == room, "抵达后无传送循环 " + connection.id)
	await progression_checks()
	await climb_checks()
	await final_patch_checks()
	await attack_checks()
	await jump_landing_checks()
	await combat_feel_checks()
	finish_report("runtime_results")

func finish_report(filename: String) -> void:
	var report := FileAccess.open("res://Documentation/" + filename + ".json", FileAccess.WRITE)
	report.store_string(JSON.stringify({"checks": checks, "failures": failures, "rendering": RenderingServer.get_current_rendering_method(), "metrics": metrics, "results": lines}, "\t"))
	report.close()
	print("Tests: %s 总计%d项，失败%d项；仅报告实际执行项目" % ["PASS" if failures == 0 else "FAIL", checks, failures])
	get_tree().quit(0 if failures == 0 else 1)

func climb_checks() -> void:
	# 由实际跳跃和左右输入攀登单向阶梯，未使用位置传送或飞行。
	Progress.new_game(false)
	Progress.unlock("double_jump")
	for id in ["T05", "H01"]:
		await load_room(id)
		var max_height: float = p.position.y
		var target := Vector2(300, room.floor_y - 96)
		var stage_index := 0
		var steps: int = int((room.floor_y - 96 - 320) / 96) + 1
		var jumping := false
		var air_clock := 0
		for tick in 2400:
			var wave := stage_index % 10
			var fraction := float(wave if wave <= 5 else 10 - wave) / 5.0
			target = Vector2(lerpf(300, room.room_size.x - 300, fraction), room.floor_y - 96 - stage_index * 96)
			Input.action_release("move_left")
			Input.action_release("move_right")
			if absf(p.position.x - target.x) > 12:
				Input.action_press("move_right" if p.position.x < target.x else "move_left")
			if p.is_on_floor():
				if p.position.y + 24 <= target.y + 4:
					stage_index += 1
					if stage_index >= steps:
						break
				if not jumping:
					Input.action_press("jump")
					jumping = true
					air_clock = 0
			else:
				air_clock += 1
				if air_clock > 4:
					Input.action_release("jump")
					jumping = false
			await frames(1)
			max_height = minf(max_height, p.position.y)
		release_inputs()
		check(stage_index >= steps - 1, "%s实际输入登阶梯：%d/%d级，最高y=%.1f" % [id, stage_index, steps, max_height])
		# 顶部宽廊与出口也须实际走到；不只验证楼梯中间一格。
		var portal_x: float = room.room_size.x - 80
		var top_jump_held := false
		var best_portal_distance := 9999.0
		for tick in 800:
			Input.action_release("move_left")
			Input.action_release("move_right")
			if absf(p.position.x - portal_x) > 25:
				Input.action_press("move_right" if p.position.x < portal_x else "move_left")
			if p.is_on_floor() and p.action.kind == MudActionState.Kind.FREE and not top_jump_held:
				Input.action_press("jump")
				top_jump_held = true
			elif not p.is_on_floor():
				Input.action_release("jump")
				top_jump_held = false
			best_portal_distance = minf(best_portal_distance, p.position.distance_to(Vector2(portal_x, 320)))
			if absf(p.position.x - portal_x) < 70 and absf(p.position.y - 320) < 80:
				break
			await frames(1)
		release_inputs()
		check(absf(p.position.x - portal_x) < 70 and absf(p.position.y - 320) < 80, "%s顶部出口实际可达且避开HUD，最近距离%.1fpx" % [id, best_portal_distance])
		await snapshot_image("runtime_" + id + "_stairs")

func progression_checks() -> void:
	Progress.new_game(false)
	await load_room("T05")
	check(room.get_node("T05_to_T06").lock_reason() == "另一侧封死", "首次T05不可提前进入最终区域")
	await load_room("T06")
	RoomTravel.arrival_lock = 0
	room.get_node("T06_to_T05").interact(p)
	check(Progress.flag("shortcut_T06_T05"), "T06侧破门永久标记")
	await load_room("T05")
	check(room.get_node("T05_to_T06").lock_reason().is_empty(), "破门后T05侧可双向返回")
	await load_room("C03")
	check(not room.get_node("C03_to_C07").lock_reason().is_empty(), "三街区未完成时档案室关闭")
	Progress.mark("district_C04")
	Progress.mark("district_C05")
	Progress.mark("met_igi")
	check(not room.get_node("C03_to_C07").lock_reason().is_empty(), "只完成两个街区仍关闭")
	Progress.mark("district_C06")
	check(room.get_node("C03_to_C07").lock_reason().is_empty(), "三街区完成后档案室可通行")
	# 序列谜题与七门顺序。
	await load_room("C06")
	Progress.flags.erase("district_C06")
	room.perform_event("sigil_3", room.get_node("sigil_3"))
	check(not Progress.flag("district_C06"), "街区征兆乱序不完成")
	for key in ["sigil_1", "sigil_2", "sigil_3"]:
		room.perform_event(key, room.get_node(key))
	check(Progress.flag("district_C06"), "街区征兆正确顺序完成")
	await load_room("H01")
	room.perform_event("gate_7", room.get_node("gate_7"))
	check(not Progress.flag("seven_gates"), "H01不能跳过前六道门")
	for i in range(1, 8):
		room.perform_event("gate_%d" % i, room.get_node("gate_%d" % i))
	check(Progress.flag("seven_gates") and room.get_node("H01_to_H02").lock_reason().is_empty(), "H01七道门顺序完成后开放H02")
	# 二段跳门槛：未解锁的最强单跳/闪避组合也不可越过。
	Progress.new_game(false)
	await load_room("T01")
	for form in [0, 1]:
		await reset_actor(Vector2(room.room_size.x - 540, room.floor_y - 24), form)
		Progress.mud_module = 1
		Input.action_press("jump")
		Input.action_press("move_right")
		await frames(24)
		p.switch_form()
		p.start_dodge()
		await frames(95)
		release_inputs()
		check(p.position.x < room.room_size.x - 390 and p.position.y > room.floor_y - 180, "未解锁二段跳：形态%d单跳/长闪避/空中切换不可绕过高台" % form)
	Progress.unlock("double_jump")
	await reset_actor(Vector2(room.room_size.x - 540, room.floor_y - 24), 0)
	Input.action_press("move_right")
	Input.action_press("jump")
	await frames(23)
	Input.action_release("jump")
	await frames(2)
	Input.action_press("jump")
	await frames(45)
	release_inputs()
	check(p.position.x > room.room_size.x - 390 and p.position.y < room.floor_y - 180 and Progress.flag("tutorial_double"), "二段跳解锁后实际跃上192px高台并完成教学")
	await snapshot_image("runtime_T01_gate")
	await load_room("T02")
	Progress.unlock("wall_climb")
	await reset_actor(Vector2(room.room_size.x - 460, room.floor_y - 24), 0)
	Input.action_press("move_right")
	Input.action_press("jump")
	Input.action_press("move_up")
	await frames(190)
	release_inputs()
	check(p.position.y < room.floor_y - 400 and Progress.flag("tutorial_wall"), "攀墙解锁后实际爬上T02出口")
	# 潜游水面门控及可下砸障碍。
	await load_room("C04")
	await reset_actor(Vector2(room.room_size.x * 0.6, room.floor_y - 280), 0)
	await frames(55)
	check(p.position.y < room.floor_y - 200, "水形态未解锁潜游时被水面门槛阻挡")
	Progress.unlock("dive")
	Input.action_press("move_down")
	await frames(75)
	release_inputs()
	check(p.position.y > room.floor_y - 140, "潜游解锁后实际抵达池底")
	room.perform_event("omen_1", room.get_node("omen_1"))
	check(Progress.flag("district_C04"), "池底征兆完成第一街区")
	await load_room("C05")
	Progress.unlock("heavy_slam")
	var obstacle: MudBreakable = room.trial_breakable
	check(not obstacle.receive_hit(99, "ordinary", {"heavy": false}), "普通攻击不能打破下砸障碍")
	await reset_actor(Vector2(obstacle.position.x, obstacle.position.y - 180), 1)
	p.action.enter(MudActionState.Kind.SLAM)
	await frames(40)
	check(Progress.flag("obstacle_C05") and not is_instance_valid(obstacle), "重压下砸实际命中、破坏障碍并永久记录")
	room.perform_event("omen_2", room.get_node("omen_2"))
	check(Progress.flag("district_C05"), "破坏后第二街区征兆可完成")
	# Boss封门、格挡易伤、死亡重试、两阶段与结局。
	await load_room("M05")
	room.start_boss()
	check(room.boss_active and room.get_node("M05_to_M04").lock_reason() == "战斗封锁", "Boss开战封锁两侧门")
	var boss: MudWhiteboxEnemy = room.boss
	await reset_actor(boss.position + Vector2(-75, 48), 1)
	Input.action_press("block")
	await frames(100)
	check(boss.phase == 3 and p.combat.bonus_ready, "实际格挡守坑人攻击触发僵直/增伤")
	release_inputs()
	await frames(2)
	p.receive_attack(99999, "death", boss.position)
	check(p.action.kind == MudActionState.Kind.DEAD, "死亡状态正确")
	RoomTravel.retry()
	await frames(10)
	room = get_tree().current_scene
	p = room.get_node("Player")
	check(room.room_id == "M05" and not room.boss_active and not Progress.flag("boss_M05"), "死亡回到Boss入口，战斗完整重置")
	room.start_boss()
	room.boss.receive_hit(9999, "finish_keeper", {})
	await frames(3)
	check(Progress.flag("boss_M05") and not room.boss_active and room.get_node("M05_to_M06").lock_reason().is_empty(), "Boss击败后开门且状态持久")
	await load_room("H03")
	room.start_boss()
	room.boss.receive_hit(9999, "phase1", {})
	check(room.boss.stage == 2 and room.boss_active and not Progress.flag("boss_H03"), "塔姆图第一阶段后进入伊姆杜，门仍封锁")
	room.boss.receive_hit(9999, "phase2", {})
	await frames(3)
	check(Progress.flag("boss_H03") and not room.boss_active, "伊姆杜第二阶段击败后开放真相/结局")
	room.perform_event("truth", room.get_node("truth"))
	room.perform_event("ending_awake", room.get_node("ending_awake"))
	check(Progress.flag("truth") and Progress.flag("ending_awake"), "真相与醒来结局可触发")
	await snapshot_image("runtime_H03")
	# 真正磁盘保存/读回。此进程使用独立APPDATA，保证用户正式存档不受影响。
	Progress.allow_disk = true
	check(Progress.save_game(), "实际磁盘保存成功")
	Progress.flags.clear()
	check(Progress.load_game() and Progress.flag("boss_M05") and Progress.flag("boss_H03"), "实际磁盘读回Boss/街区/捷径进度")
	Progress.allow_disk = false
	Progress.enter_test()
	Progress.unlock("high_jump_upgrade")
	Progress.new_game(false)
	check(not Progress.test_mode and not Progress.has_ability("high_jump_upgrade") and not Progress.has_ability("dive"), "重开正式流程不残留测试能力")

func final_patch_checks() -> void:
	await load_room("M03")
	await frames(100)
	var all_passive := true
	for enemy in get_tree().get_nodes_in_group("damageables"):
		all_passive = all_passive and enemy.passive and enemy.phase == 0
	check(all_passive, "朝圣者按PDF保持不主动攻击")
	await load_room("C01")
	check(get_tree().get_nodes_in_group("damageables").size() == 1, "城门复用普通敌人白盒守卫，未新增城市Boss")
	await load_room("TEST")
	await reset_actor(Vector2(600, room.floor_y - 24), 0)
	var start: float = p.position.x
	var event := InputEventKey.new()
	event.physical_keycode = KEY_A
	event.pressed = true
	Input.parse_input_event(event)
	await frames(10)
	event.pressed = false
	Input.parse_input_event(event)
	check(p.position.x < start - 30, "原有物理键A绑定保留且实际驱动移动")
	await snapshot_image("runtime_final_lab")
	Progress.leave_test()
	get_tree().change_scene_to_file("res://assets/scenes/ui/main_menu.tscn")
	await frames(8)
	check(get_tree().current_scene.name == "MainMenu", "F5主入口菜单实际运行")
	await snapshot_image("runtime_main_menu")

func attack_checks() -> void:
	Progress.new_game(false)
	await load_room("TEST")
	var target: MudWhiteboxEnemy = get_tree().get_first_node_in_group("damageables")
	var samples: Array[Dictionary] = []
	for form in [0, 1]:
		await reset_actor(Vector2(1855, room.floor_y - 24), form)
		p.facing = 1
		var before: float = target.health
		var counts := {"windup": 0, "active": 0, "recovery": 0}
		Input.action_press("attack")
		for i in 125:
			await frames(1)
			if i == 2:
				Input.action_release("attack")
			match p.action.kind:
				MudActionState.Kind.ATTACK_WINDUP: counts.windup += 1
				MudActionState.Kind.ATTACK_ACTIVE: counts.active += 1
				MudActionState.Kind.ATTACK_RECOVERY: counts.recovery += 1
		counts.damage = before - target.health
		samples.append(counts)
	check(is_equal_approx(samples[0].damage, 10) and is_equal_approx(samples[1].damage, 24), "真实攻击判定：水10伤害，泥24伤害，同一攻击不多帧重复伤害")
	check(samples[1].windup > samples[0].windup and samples[1].recovery > samples[0].recovery * 2 and samples[0].active > 0, "实际攻击三阶段：泥前摇更长，后摇显著增加")
	for module in [0, 1]:
		await reset_actor(Vector2(1940, room.floor_y - 150), 1)
		Progress.mud_module = module
		p.velocity.y = 200
		var before: float = target.health
		p.combat.start(p.mud_config, true)
		await frames(125)
		var expected: float = 43.2 if module == 0 else 24.0
		check(absf(before - target.health - expected) < 0.1, "泥模组%d向下攻击实际伤害%.1f，模组1强化互斥" % [module + 1, before - target.health])
	metrics.attack_samples = samples

func combat_feel_checks() -> void:
	var suite: RefCounted = load("res://tests/combat_feel_checks.gd").new()
	var completed: Variant = await suite.run(self)
	check(completed == true, "手感与目录回归完整执行")

func reproduce_landing_bug() -> void:
	Progress.new_game(false)
	Progress.enter_test()
	Progress.unlock("double_jump")
	await load_room("TEST")
	await reset_actor(Vector2(400, room.floor_y - 24), 1)
	# 已离地、正在下落时预约二段跳；落地发生于泥跳0.24秒前摇内。
	p.position = Vector2(400, room.floor_y - 64)
	p.velocity = Vector2(0, 360)
	await frames(2)
	check(not p.is_on_floor(), "复现前置：泥形态处于下落阶段")
	check(p.request_jump(), "复现前置：下落时接受二段跳起跳前摇")
	var broken := false
	for i in 12:
		await frames(1)
		if p.action.kind == MudActionState.Kind.JUMP_WINDUP and p.pending_jump_profile == null:
			broken = true
			# 在实际崩溃前截获原状态，不让运行持续报错。
			p.action.enter(MudActionState.Kind.FREE)
			break
	check(not broken, "下落中起跳前摇遇到落地后，待起跳配置仍有效")

func jump_landing_checks() -> void:
	Progress.new_game(false)
	Progress.enter_test()
	Progress.unlock("double_jump")
	await load_room("TEST")
	for variant in [Vector2i(0, 0), Vector2i(1, 0), Vector2i(1, 1)]:
		var form: int = variant.x
		var module: int = variant.y
		var prefix := "%s/泥模组%d" % ["水" if form == 0 else "泥", module + 1]
		await reset_actor(Vector2(400, room.floor_y - 24), form)
		Progress.mud_module = module
		p.position = Vector2(400, room.floor_y - 50)
		p.velocity = Vector2(0, 600)
		await frames(2)
		# 水形态固定下降速度较慢；将三种变体统一至距地2px的同一时序。
		p.position.y = room.floor_y - 26
		check(not p.is_on_floor() and p.request_jump(), prefix + "下落贴地前接受起跳请求")
		var frozen: MudFormConfig = p.pending_jump_profile
		var crossed_landing := false
		var pending_survived := true
		var launched := false
		var launched_valid := false
		var launch_count := -1
		var bursts_before: int = p.combat.landed_bursts
		var no_fall_burst := true
		Input.action_press("jump")
		for tick in 125:
			await frames(1)
			if p.is_on_floor() and p.action.kind == MudActionState.Kind.JUMP_WINDUP:
				crossed_landing = true
				pending_survived = pending_survived and p.pending_jump_profile == frozen and p.jump_profile == null
				no_fall_burst = no_fall_burst and p.combat.landed_bursts == bursts_before
			if p.jump_in_progress and not launched:
				launched = true
				launched_valid = p.jump_profile == frozen and p.pending_jump_profile == null
				launch_count = p.jumps_used
			if launched and p.is_on_floor():
				break
		release_inputs()
		check(crossed_landing and pending_survived, prefix + "前摇中落地：仅清旧轨迹，保留待起跳配置")
		check(launched and launched_valid and launch_count == 1, prefix + "落地后按冻结配置正常起跳，只消耗一次跳跃")
		check(no_fall_burst, prefix + "前摇中普通落地不误触范围攻击")
		var expected_bursts := 1 if form == 1 and module == 1 else 0
		check(p.combat.landed_bursts - bursts_before == expected_bursts, prefix + "下一跳的范围攻击资格独立且不重复")
	# 没有有效待起跳请求时，不允许旧状态发起跳跃。
	await reset_actor(Vector2(400, room.floor_y - 24), 1)
	p.action.enter(MudActionState.Kind.JUMP_WINDUP)
	p.launch_jump()
	check(p.action.kind == MudActionState.Kind.FREE and p.jumps_used == 0, "待起跳配置为空时安全取消，不读取Nil或增加跳数")
	# 受伤取消前摇；计时结束后不得凭旧请求弹起。
	check(p.request_jump(), "受伤分支：接受泥起跳前摇")
	p.receive_attack(1, "pending_jump_hurt", p.position)
	check(p.action.kind == MudActionState.Kind.HURT and p.pending_jump_profile == null, "受伤立即取消待起跳请求")
	await frames(35)
	check(p.is_on_floor() and not p.jump_in_progress and p.jumps_used == 0, "受伤前摇结束后没有幽灵起跳")
	# 真实换场路径必须取消请求，不把前摇带到出生点。
	check(p.request_jump(), "换场分支：接受泥起跳前摇")
	RoomTravel.enter_room("M01", "Start", true)
	await frames(25)
	room = get_tree().current_scene
	p = room.get_node("Player")
	check(room.room_id == "M01" and p.is_on_floor() and p.pending_jump_profile == null and not p.jump_in_progress and p.jumps_used == 0, "实际过门取消待起跳、空中轨迹与落地伤害资格")
