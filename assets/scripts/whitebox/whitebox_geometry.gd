class_name MudWhiteboxGeometry
extends Node2D
## 白盒地形与教学设施；由房间显式构建。
var room: Node2D

func solid(at: Vector2, dimensions: Vector2, color: Color = Color("3f4c52")) -> StaticBody2D:
	var body := StaticBody2D.new()
	body.position = at
	body.collision_layer = 1
	body.collision_mask = 2
	var shape := RectangleShape2D.new()
	shape.size = dimensions
	var collision := CollisionShape2D.new()
	collision.shape = shape
	body.add_child(collision)
	var polygon := Polygon2D.new()
	var half := dimensions * 0.5
	polygon.polygon = PackedVector2Array([Vector2(-half.x, -half.y), Vector2(half.x, -half.y), half, Vector2(-half.x, half.y)])
	polygon.color = color
	body.add_child(polygon)
	add_child(body)
	room.floor_blocks.append(Rect2(at - half, dimensions))
	return body

func platform(x: float, y: float, width: float, one_way: bool = false) -> void:
	var body := solid(Vector2(x, y + 10), Vector2(width, 20), Color("59686b"))
	if one_way:
		(body.get_child(0) as CollisionShape2D).one_way_collision = true

func sign_at(text: String, at: Vector2, width: float = 360) -> void:
	var label := Label.new()
	label.text = text
	label.position = at
	label.size = Vector2(width, 100)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_font_size_override("font_size", 19)
	label.add_theme_color_override("font_color", Color("b9cbc9"))
	add_child(label)

func build_geometry() -> void:
	# 城市第一街区和测试场：真实凹入水池，池底征兆不会靠岸误触。
	if room.room_id == "C04":
		var left: float = room.room_size.x * 0.48
		var right: float = room.room_size.x * 0.75
		var surface: float = room.floor_y - 200
		solid(Vector2(left * 0.5, surface + 150), Vector2(left, 300))
		solid(Vector2((right + room.room_size.x) * 0.5, surface + 150), Vector2(room.room_size.x - right, 300))
		solid(Vector2((left + right) * 0.5, room.floor_y + 32), Vector2(right - left, 64))
		add_water(Rect2(left, surface - 20, right - left, room.floor_y - surface + 20), true)
		for p in room.data.ports:
			p.position[1] = surface - 24
			p.arrival[1] = surface - 24
		room.spawns.get_node("Start").position.y = surface - 24
		for event in room.data.events:
			if event.id != "omen_1":
				event.position[1] = surface - 24
	else:
		solid(Vector2(room.room_size.x * 0.5, room.floor_y + 32), Vector2(room.room_size.x, 64))
	solid(Vector2(0, room.room_size.y * 0.5), Vector2(32, room.room_size.y))
	solid(Vector2(room.room_size.x, room.room_size.y * 0.5), Vector2(32, room.room_size.y))
	solid(Vector2(room.room_size.x * 0.5, 0), Vector2(room.room_size.x, 32))
	match room.room_id:
		"M01":
			sign_at("泥坑\n按Q切换形态\n按K格挡，按J反击", Vector2(64, 220))
			room.spawn_enemy(Vector2(room.room_size.x * 0.9, room.floor_y - 32), "格挡训练", "", 200)
		"M02":
			add_water(Rect2(room.room_size.x * 0.55, room.floor_y - 45, room.room_size.x * 0.32, 45), false)
			sign_at("两河\n暗红色的水流入泥坑", Vector2(room.room_size.x * 0.5, room.floor_y - 220))
			platform(room.room_size.x * 0.35, room.floor_y - 192, 256)
		"M03":
			for x in [1100, 2200]:
				var pilgrim: MudWhiteboxEnemy = room.spawn_enemy(Vector2(x, room.floor_y - 32), "朝圣者", "", 45)
				pilgrim.passive = true
		"M06":
			sign_at("两河源头\n远处传来歌声", Vector2(128, 180))
		"C01":
			room.spawn_enemy(Vector2(room.room_size.x * 0.62, room.floor_y - 32), "塔姆图之子·守卫", "", 60)
		"T01":
			platform(room.room_size.x - 189, room.floor_y - 192, 378)
			solid(Vector2(room.room_size.x - 390, room.floor_y - 96), Vector2(24, 192))
			sign_at("高台通往壁画长廊\n空中再次按下跳跃", Vector2(room.room_size.x - 600, room.floor_y - 380))
		"T02":
			solid(Vector2(room.room_size.x - 360, room.floor_y - 224), Vector2(32, 448))
			platform(room.room_size.x - 176, room.floor_y - 448, 320)
			sign_at("可攀墙面 →\n贴墙+朝墙方向+W", Vector2(room.room_size.x - 660, room.floor_y - 330))
		"T03":
			add_water(Rect2(room.room_size.x * 0.4, room.floor_y - 64, room.room_size.x * 0.4, 64), false)
		"T04":
			platform(300, room.floor_y - 192, 220)
			platform(room.room_size.x - 300, room.floor_y - 192, 220)
			solid(Vector2(130, room.floor_y - 220), Vector2(32, 440))
			solid(Vector2(room.room_size.x - 130, room.floor_y - 220), Vector2(32, 440))
		"T05":
			build_stairs(320, room.floor_y - 96, 96)
			sign_at("上层通往城市\n沿阶梯上行", Vector2(300, 250), 700)
		"C05":
			var center: float = roundf(room.room_size.x * 0.58 / 64) * 64
			platform(center - 200, room.floor_y - 160, 160)
			platform(center + 200, room.floor_y - 160, 160)
			room.trial_breakable = add_breakable(Vector2(center, room.floor_y - 110), Vector2(260, 32), "obstacle_C05")
			sign_at("黄色裂板：空中S+J重压下砸\n征兆需要先破坏裂板，普通攻击无效", Vector2(center - 320, room.floor_y - 350), 640)
		"H01":
			build_stairs(320, room.floor_y - 96, 96)
			for event in room.data.events:
				platform(event.position[0], event.position[1] + 24, 384, true)
			sign_at("七道门\n往上走的人，什么也不能带", Vector2(64, room.floor_y - 360), 600)
		"H02":
			platform(room.room_size.x * 0.55, room.floor_y - 192, 220)
			add_water(Rect2(room.room_size.x * 0.65, room.floor_y - 50, 250, 50), false)
			add_breakable(Vector2(room.room_size.x * 0.5, room.floor_y - 30), Vector2(100, 40), "obstacle_H02")
		"TEST":
			build_lab()

func build_stairs(top: float, bottom: float, step: float) -> void:
	var index := 0
	var y := bottom
	while y >= top:
		var wave := index % 10
		var fraction := float(wave if wave <= 5 else 10 - wave) / 5.0
		platform(lerpf(300, room.room_size.x - 300, fraction), y, 384, true)
		y -= step
		index += 1
	platform(room.room_size.x - 230, top + 24, 420, true)
	platform(room.room_size.x * 0.5, top + 120, room.room_size.x - 128, true)

func add_water(rect: Rect2, gated: bool) -> void:
	room.water_rects.append(rect)
	if gated:
		# 固体水面只在缺潜游能力或非水形态时存在。不可直接站在池底读征兆。
		var surface := solid(Vector2(rect.get_center().x, rect.position.y + 8), Vector2(rect.size.x, 16), Color(0.3, 0.6, 0.7, 0.25))
		room.water_solids.append(surface)

func add_breakable(at: Vector2, dimensions: Vector2, flag_key: String) -> MudBreakable:
	if not Progress.test_mode and Progress.flag(flag_key):
		return null
	var block := MudBreakable.new()
	block.position = at
	block.dimensions = dimensions
	block.flag_key = "" if Progress.test_mode else flag_key
	add_child(block)
	return block

func build_lab() -> void:
	sign_at("训练场\nB调整跳跃，F6开放训练能力\n沿地面向右前往格挡与武器训练区", Vector2(100, 220), 650)
	room.source_x = 1450
	platform(2360, room.floor_y - 192, 220)
	solid(Vector2(2500, room.floor_y - 230), Vector2(32, 460))
	room.trial_breakable = add_breakable(Vector2(2720, room.floor_y - 70), Vector2(150, 32), "")
	add_water(Rect2(2860, room.floor_y - 125, 320, 125), false)
	room.spawn_enemy(Vector2(1940, room.floor_y - 32), "训练靶", "dummy", 400)
	for height in [64, 128, 176, 192, 224, 256, 384]:
		sign_at("%dpx %s" % [height, "单跳门控" if height == 192 else ""], Vector2(2080, room.floor_y - height - 26), 260)
		solid(Vector2(2060, room.floor_y - height), Vector2(100, 2), Color("749195"))

