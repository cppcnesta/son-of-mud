extends Node2D
## 23个独立.tscn共享白盒构建逻辑；房间内容与连接保存在world.json。
@export var room_id: String = "M01"
var room_title: String = ""
var data: Dictionary
var room_catalog: Dictionary
var room_size: Vector2
var floor_y: float
var geometry := MudWhiteboxGeometry.new()
var events := MudWhiteboxEvents.new()
var player: CharacterBody2D
var hud: MudDebugHUD
var current_prompt: String = ""
var boss_active: bool = false
var boss: MudWhiteboxEnemy
var spawns: Node2D
var water_rects: Array[Rect2] = []
var water_solids: Array[StaticBody2D] = []
var trial_breakable: MudBreakable
var local_sequence: int = 0
var source_enabled: bool = false
var source_clock: float = 1.0
var source_id: int = 0
var source_active: float = 0.0
var source_x: float = 0.0
var floor_blocks: Array[Rect2] = []
var water_color := Color(0.22, 0.55, 0.65, 0.5)
var flood_timer: float = -1.0

func _ready() -> void:
	var world: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/data/whitebox/world.json"))
	data = world.rooms[room_id]
	room_catalog = world.rooms
	room_title = data.title
	room_size = Vector2(data.size[0], data.size[1])
	floor_y = room_size.y - 64
	if room_id == "TEST" and not Progress.test_mode:
		Progress.enter_test()
	spawns = Node2D.new()
	spawns.name = "Spawns"
	add_child(spawns)
	spawn_marker("Start", Vector2(208, floor_y - 24))
	geometry.room = self
	geometry.name = "WhiteboxGeometry"
	add_child(geometry)
	geometry.build_geometry()
	events.room = self
	build_connections()
	build_events()
	player = preload("res://assets/scenes/player/player.tscn").instantiate()
	player.name = "Player"
	player.collision_layer = 2
	player.collision_mask = 1
	add_child(player)
	player.position = spawns.get_node("Start").position
	var camera := Camera2D.new()
	camera.name = "Camera2D"
	camera.limit_left = 0
	camera.limit_top = 0
	camera.limit_right = int(room_size.x)
	camera.limit_bottom = int(room_size.y)
	camera.position = Vector2(0, -80)
	camera.position_smoothing_enabled = true
	camera.position_smoothing_speed = 8.0
	player.add_child(camera)
	hud = MudDebugHUD.new()
	hud.room = self
	add_child(hud)
	player.died.connect(on_player_died)
	if ["M04", "H02"].has(room_id):
		Progress.set_checkpoint(room_id, "Start")
	show_message(room_title, 2.5)
	queue_redraw()

func spawn_marker(key: String, at: Vector2) -> void:
	var marker := Marker2D.new()
	marker.name = key
	marker.position = at
	spawns.add_child(marker)

func build_connections() -> void:
	for info in data.ports:
		var portal := MudRoomPortal.new()
		portal.name = info.id
		portal.interface_id = info.id
		portal.target_room = RoomTravel.room_path(info.target)
		portal.target_spawn = StringName(info.spawn)
		portal.requirements = PackedStringArray(info.requires)
		portal.managed = true
		portal.room = self
		portal.title = "前往" + str(room_catalog[info.target].title).split("·")[0]
		portal.position = Vector2(info.position[0], info.position[1])
		portal.shortcut_flag = info.get("shortcut", "")
		portal.break_from_this_side = info.get("break_side", false)
		portal.bidirectional_after_break = info.get("bidirectional_after_break", true)
		add_child(portal)
		spawn_marker("From" + info.target, Vector2(info.arrival[0], info.arrival[1]))

func build_events() -> void:
	for info in data.events:
		var event := MudRoomEvent.new()
		event.name = info.id
		event.event_id = info.id
		event.title = info.title
		event.position = Vector2(info.position[0], info.position[1])
		event.room = self
		event.used = Progress.flag(room_id + ":" + info.id)
		add_child(event)

func show_message(text: String, duration: float = 8.0) -> void:
	if hud != null:
		hud.say(text, duration)

func perform_event(key: String, object: Node2D) -> void:
	events.perform(key, object)

func start_boss() -> void:
	if boss_active or Progress.flag("boss_" + room_id):
		show_message("此处战斗已完成。" if not boss_active else "战斗进行中：门已锁定。")
		return
	boss_active = true
	Progress.set_checkpoint(room_id, "Start")
	var kind: String = {"M05": "keeper", "T04": "dragon", "H03": "final"}.get(room_id, "keeper")
	var title: String = {"keeper": "守坑人", "dragon": "母神之兽", "final": "塔姆图"}[kind]
	boss = spawn_enemy(Vector2(room_size.x * 0.58, floor_y - 72), title, kind, 160 if kind == "keeper" else 220)
	boss.vulnerability_multiplier = player.rules.boss_vulnerability
	boss.defeated.connect(on_boss_defeated)

func spawn_enemy(at: Vector2, title: String, kind: String, hp: float) -> MudWhiteboxEnemy:
	var enemy := MudWhiteboxEnemy.new()
	enemy.position = at
	enemy.display_name = title
	enemy.boss_kind = kind
	enemy.max_health = hp
	add_child(enemy)
	if kind == "dummy":
		enemy.set_physics_process(false)
		enemy.label.text = "%s\n%d/%d" % [title, hp, hp]
	return enemy

func on_boss_defeated() -> void:
	boss_active = false
	Progress.mark("boss_" + room_id)
	show_message({"M05": "守坑人破碎，露出握着芦苇杖的老人：祂……醒了……", "T04": "龙蛇外壳破碎，工匠落下：祂……爱过我们……", "H03": "父亲的身体片片碎裂，下面露出的是自己的脸。"}.get(room_id, "战斗完成"))

func on_player_died() -> void:
	show_message("身躯破碎。按R重试。", 999)

func debug_unlock() -> void:
	if not Progress.test_mode:
		show_message("调试解锁仅允许在F2测试场使用。")
		return
	for key in ["block", "form_switch", "double_jump", "wall_climb", "heavy_slam", "dive"]:
		Progress.unlock(key)
	show_message("测试能力已开放，不会写入正式存档；未开放高跳升级。")

func open_menu() -> void:
	Progress.leave_test()
	get_tree().change_scene_to_file("res://assets/scenes/ui/main_menu.tscn")

func _process(delta: float) -> void:
	if not is_instance_valid(player):
		return
	if Input.is_action_just_pressed("retry"):
		RoomTravel.retry()
	if Input.is_action_just_pressed("menu"):
		open_menu()
	if Input.is_action_just_pressed("open_lab"):
		Progress.enter_test()
		RoomTravel.enter_room("TEST", "Start", false)
	if Input.is_action_just_pressed("restart_flow"):
		Progress.new_game()
		RoomTravel.enter_room("M01", "Start", false)
	if Input.is_action_just_pressed("debug_unlock"):
		debug_unlock()
	if Input.is_action_just_pressed("toggle_module"):
		if (Progress.test_mode or ["M04", "C03", "H02"].has(room_id)) and player.is_on_floor() and player.action.kind == MudActionState.Kind.FREE:
			Progress.mud_module = 1 - Progress.mud_module
			show_message("泥形态跳跃：%s" % ("重跳" if Progress.mud_module == 0 else "长按跳跃"))
		else:
			show_message("仅可在测试场或休整房间落地待机时切换模组。")
	var nearest: Node2D
	var distance := 100.0
	for object in get_tree().get_nodes_in_group("interactables"):
		if not object is Node2D:
			continue
		var d: float = player.global_position.distance_to(object.global_position)
		if d < distance:
			nearest = object
			distance = d
	current_prompt = "[E] " + nearest.title if nearest != null else ""
	if nearest != null and Input.is_action_just_pressed("interact") and player.action.kind == MudActionState.Kind.FREE:
		nearest.interact(player)
	if flood_timer >= 0:
		flood_timer -= delta
		water_color.a = 0.8
		if flood_timer <= 0:
			Progress.mark("flood_done")
			flood_timer = -1
			water_color.a = 0.3
			show_message("洪水退去，后续通路开放。可读取「洪水之后」。")
		queue_redraw()

func _physics_process(delta: float) -> void:
	if not is_instance_valid(player):
		return
	player.submerged = false
	for rect in water_rects:
		if rect.has_point(player.global_position):
			player.submerged = true
	for surface in water_solids:
		if is_instance_valid(surface):
			surface.collision_layer = 0 if Progress.has_ability("dive") and player.form == 0 else 1
	if room_id == "T01" and not Progress.flag("tutorial_double") and player.jumps_used >= 2 and player.global_position.y < floor_y - 165:
		Progress.mark("tutorial_double")
	if room_id == "T02" and not Progress.flag("tutorial_wall") and Progress.has_ability("wall_climb") and player.global_position.y < floor_y - 400:
		Progress.mark("tutorial_wall")
	if source_enabled:
		source_clock -= delta
		if source_clock <= 0:
			source_clock = 2.0
			source_active = 0.2
			source_id += 1
		if source_active > 0:
			source_active -= delta
			if absf(player.position.x - source_x) < 150 and absf(player.position.y - (floor_y - 24)) < 90:
				player.receive_attack(8, "source_%d" % source_id, Vector2(source_x, floor_y - 24))
		queue_redraw()
	if player.position.y > room_size.y + 100:
		player.receive_attack(999, "fall_%s" % Time.get_ticks_msec(), Vector2.ZERO)

func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, room_size), Color("17252d"))
	for x in range(0, int(room_size.x), 64):
		draw_line(Vector2(x, 0), Vector2(x, room_size.y), Color(0.25, 0.35, 0.38, 0.18))
	for y in range(0, int(room_size.y), 64):
		draw_line(Vector2(0, y), Vector2(room_size.x, y), Color(0.25, 0.35, 0.38, 0.18))
	for rect in water_rects:
		draw_rect(rect, water_color)
	if source_enabled:
		draw_rect(Rect2(source_x - 150, floor_y - 170, 300, 170), Color(1, 0.3, 0.2, 0.5 if source_active > 0 else 0.12))
