class_name MudBreakable
extends StaticBody2D
@export var requires_heavy: bool = true
@export var flag_key: String = ""
@export var dimensions := Vector2(128, 32)
var ledger := MudAttackLedger.new()
func _ready() -> void:
	collision_layer = 5
	collision_mask = 0
	var shape := CollisionShape2D.new()
	var box := RectangleShape2D.new()
	box.size = dimensions
	shape.shape = box
	add_child(shape)
	queue_redraw()
func receive_hit(_amount: float, attack_id: String, info: Dictionary) -> bool:
	if not ledger.accept(attack_id) or (requires_heavy and not bool(info.get("heavy", false))):
		return false
	if not flag_key.is_empty():
		Progress.mark(flag_key)
	queue_free()
	return true
func _draw() -> void:
	draw_rect(Rect2(-dimensions * 0.5, dimensions), Color("c4a04f"))
	draw_line(-dimensions * 0.5, dimensions * 0.5, Color("4b352b"), 4)
