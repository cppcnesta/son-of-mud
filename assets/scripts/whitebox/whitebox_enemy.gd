class_name MudWhiteboxEnemy
extends StaticBody2D
## 全部为白盒占位，未使用正式敌人资源。
signal defeated
@export var display_name: String = "持锤泥人"
@export var max_health: float = 60.0
@export var damage: float = 12.0
@export var telegraph_time: float = 0.8
@export var attack_time: float = 0.18
@export var recovery_time: float = 1.15
@export var attack_range: float = 135.0
@export var vulnerability_multiplier: float = 2.0
@export var boss_kind: String = ""
@export var passive: bool = false
var health: float
var phase: int = 0
var clock: float = 0.6
var attack_id: String = ""
var sequence: int = 0
var stage: int = 1
var locked_x: float = 0.0
var ledger := MudAttackLedger.new()
var label: Label
var body_size := Vector2(46, 64)

func _ready() -> void:
	add_to_group("damageables")
	collision_layer = 4
	collision_mask = 0
	health = max_health
	if not boss_kind.is_empty() and boss_kind != "dummy":
		body_size = Vector2(100, 144)
	var shape := CollisionShape2D.new()
	var rectangle := RectangleShape2D.new()
	rectangle.size = body_size
	shape.shape = rectangle
	add_child(shape)
	label = Label.new()
	label.position = Vector2(-80, -body_size.y * 0.5 - 70)
	label.size = Vector2(160, 70)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size", 16)
	add_child(label)

func receive_hit(amount: float, id: String, _info: Dictionary) -> bool:
	if phase == 4 or not ledger.accept(id):
		return false
	health -= amount * (vulnerability_multiplier if phase == 3 else 1.0)
	if label != null:
		label.text = "%s\n%d/%d" % [display_name, maxi(0, ceili(health)), int(max_health)]
	if health <= 0:
		if boss_kind == "final" and stage == 1:
			stage = 2
			display_name = "伊姆杜"
			health = max_health
			phase = 3
			clock = 2.0
		else:
			phase = 4
			defeated.emit()
			queue_free()
	queue_redraw()
	return true

func _physics_process(delta: float) -> void:
	var player := get_tree().get_first_node_in_group("player")
	if player == null or phase == 4:
		return
	label.text = "%s\n%d/%d  %s" % [display_name, ceili(health), int(max_health), ["", "蓄势", "出手", "失衡", ""][phase]]
	if passive:
		return
	clock -= delta
	if phase == 0 and absf(player.global_position.x - global_position.x) > 600:
		return
	if phase == 2:
		var center: float = locked_x if boss_kind == "dragon" else global_position.x
		if absf(player.global_position.x - center) < attack_range and absf(player.global_position.y - global_position.y) < 115:
			var result: String = player.receive_attack(damage, attack_id, global_position)
			if result == "blocked" and boss_kind == "keeper":
				phase = 3
				clock = player.rules.boss_stagger_duration
	if clock <= 0:
		match phase:
			0:
				phase = 1
				clock = telegraph_time
				locked_x = player.global_position.x
				sequence += 1
				attack_id = "enemy_%s_%d" % [get_instance_id(), sequence]
			1:
				phase = 2
				clock = attack_time
			2:
				phase = 0
				clock = recovery_time
				# 龙蛇攻击锁定玩家原位置；向墙/高台诱导可获得失衡窗口。
				if boss_kind == "dragon" and (player.global_position.y < global_position.y - 100 or absf(player.global_position.x - locked_x) > 180):
					phase = 3
					clock = 2.0
			3:
				phase = 0
				clock = recovery_time
	queue_redraw()

func _draw() -> void:
	var color := Color("b15a55")
	if phase == 1:
		color = Color("ffd471")
	elif phase == 2:
		color = Color("f45050")
	elif phase == 3:
		color = Color("a5d0ce")
	draw_rect(Rect2(-body_size * 0.5, body_size), color)
	if phase == 1 or phase == 2:
		var center := locked_x - global_position.x if boss_kind == "dragon" else 0.0
		draw_rect(Rect2(center - attack_range, -100, attack_range * 2, 200), Color(1, 0.3, 0.2, 0.18))
