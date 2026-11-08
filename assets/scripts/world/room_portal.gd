class_name MudRoomPortal
extends Area2D
## 独立房间接口；保留target_room/target_spawn兼容已有场景。
@export var interface_id: String = ""
@export_file("*.tscn") var target_room: String = ""
@export var target_spawn: StringName = &""
@export var requirements: PackedStringArray = []
@export var break_from_this_side: bool = false
@export var bidirectional_after_break: bool = true
@export var shortcut_flag: String = ""
@export var managed: bool = false
@export var debug_output: bool = false
var room: Node2D
var title: String = ""
var label: Label

func _ready() -> void:
	add_to_group("interactables")
	collision_layer = 0
	collision_mask = 2
	label = Label.new()
	label.position = Vector2(-100, -100 if position.y < 350 else -164)
	label.size = Vector2(200, 85)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_font_size_override("font_size", 16)
	add_child(label)
	if get_child_count() == 1:
		var collision := CollisionShape2D.new()
		var shape := RectangleShape2D.new()
		shape.size = Vector2(70, 110)
		collision.shape = shape
		collision.position.y = -30
		add_child(collision)

func lock_reason() -> String:
	if room != null and room.boss_active:
		return "战斗封锁"
	for key in requirements:
		if key.begins_with("ability:"):
			if not Progress.has_ability(key.trim_prefix("ability:")):
				return "需要" + {"double_jump": "二段跳", "wall_climb": "攀墙", "dive": "潜游", "heavy_slam": "重压下砸"}.get(key.trim_prefix("ability:"), "对应能力")
		elif key == "districts":
			if not Progress.districts_complete():
				return "三个街区尚未完成"
		elif not Progress.flag(key):
			return {"tutorial_double": "先完成二段跳教学", "tutorial_wall": "先完成攀墙教学", "flood_done": "先操作水闸并等待退水", "boss_M05": "先击败守坑人", "boss_T04": "先击败龙蛇泥人", "seven_gates": "先完成七道门", "met_igi": "先与伊吉交谈"}.get(key, "先完成前置事件")
	if not shortcut_flag.is_empty():
		if not Progress.flag(shortcut_flag):
			return "从此侧破门：按E" if break_from_this_side else "另一侧封死"
		if not break_from_this_side and not bidirectional_after_break:
			return "单向出口"
	return ""

func interact(_actor: CharacterBody2D) -> void:
	if RoomTravel.arrival_lock > 0:
		return
	var reason := lock_reason()
	if not shortcut_flag.is_empty() and not Progress.flag(shortcut_flag) and break_from_this_side and (room == null or not room.boss_active):
		Progress.mark(shortcut_flag)
		if room != null:
			room.show_message("破门完成：捷径已永久开启，再按E通行。")
		return
	if not reason.is_empty():
		if room != null:
			room.show_message(reason)
		return
	RoomTravel.go_to_room(target_room, target_spawn)

func _process(_delta: float) -> void:
	if label != null:
		var reason := lock_reason()
		label.text = title + ("\n[E] 通行" if reason.is_empty() else "\n" + reason)
	queue_redraw()
	if not managed and Input.is_action_just_pressed("interact"):
		for body in get_overlapping_bodies():
			if body.is_in_group("player"):
				interact(body)

func _draw() -> void:
	var color := Color("64c9b0") if lock_reason().is_empty() else Color("b87863")
	draw_rect(Rect2(-28, -82, 56, 106), color.darkened(0.65))
	draw_rect(Rect2(-28, -82, 56, 106), color, false, 4)
