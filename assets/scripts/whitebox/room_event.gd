class_name MudRoomEvent
extends Node2D
var event_id: String = ""
var title: String = ""
var room: Node2D
var color := Color("c9aa6a")
var used: bool = false
func _ready() -> void:
	add_to_group("interactables")
	var label := Label.new()
	label.text = title
	label.position = Vector2(-100, -100)
	label.size.x = 200
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size", 17)
	add_child(label)
func interact(_actor: CharacterBody2D) -> void:
	room.perform_event(event_id, self)
func _draw() -> void:
	draw_rect(Rect2(-18, -55, 36, 55), color.darkened(0.45) if used else color)
	draw_circle(Vector2(0, -62), 7, Color("ffe9a4"))
