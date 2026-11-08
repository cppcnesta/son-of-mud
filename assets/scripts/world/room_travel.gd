extends Node
## 保留 go_to_room 接口；真实路径校验、延迟切换和出生点定位。
var _is_changing_room: bool = false
var arrival_lock: float = 0.0
var transient: Dictionary = {}

func _process(delta: float) -> void:
	arrival_lock = maxf(0.0, arrival_lock - delta)

func room_path(room_id: String) -> String:
	if room_id == "TEST":
		return "res://assets/scenes/whitebox/tests/form_lab.tscn"
	return "res://assets/scenes/whitebox/levels/%s.tscn" % room_id

func enter_room(room_id: String, spawn_name: String = "Start", carry: bool = true) -> void:
	if not carry:
		transient.clear()
	go_to_room(room_path(room_id), StringName(spawn_name), carry)

func go_to_room(scene_path: String, spawn_name: StringName, carry: bool = true) -> void:
	if _is_changing_room:
		return
	if scene_path.is_empty() or spawn_name.is_empty() or not ResourceLoader.exists(scene_path, "PackedScene"):
		push_error("Tests: FAIL 房间路径/出生点无效 %s / %s" % [scene_path, spawn_name])
		return
	_is_changing_room = true
	if carry:
		var current := get_tree().get_first_node_in_group("player")
		if current != null and current.has_method("export_transient"):
			transient = current.export_transient()
	_perform_change.call_deferred(scene_path, spawn_name)

func _perform_change(scene_path: String, spawn_name: StringName) -> void:
	var packed := load(scene_path) as PackedScene
	var next := packed.instantiate()
	# 新房间在_ready构建白盒及Spawns；先进入树，再校验目标。
	var error := get_tree().change_scene_to_node(next)
	if error != OK:
		push_error("Tests: FAIL 切场失败 %s" % error_string(error))
		next.free()
		_is_changing_room = false
		return
	await get_tree().scene_changed
	var player := next.get_node_or_null("Player") as CharacterBody2D
	var spawn := next.get_node_or_null("Spawns/" + String(spawn_name)) as Marker2D
	if player == null or spawn == null:
		push_error("Tests: FAIL 缺少Player或出生点 %s / %s" % [scene_path, spawn_name])
		_is_changing_room = false
		return
	player.global_position = spawn.global_position
	player.velocity = Vector2.ZERO
	if player.has_method("restore_transient") and not transient.is_empty():
		player.restore_transient(transient)
	elif player.has_method("clear_air_context"):
		player.clear_air_context()
	transient.clear()
	player.reset_physics_interpolation()
	var camera := player.get_node_or_null("Camera2D") as Camera2D
	if camera != null:
		camera.reset_smoothing()
		camera.force_update_scroll()
	arrival_lock = 0.45
	if "room_id" in next and next.room_id != "TEST":
		Progress.last_room = next.room_id
		Progress.last_spawn = String(spawn_name)
		Progress.save_game()
	print("Tests: PASS 房间载入 %s / %s" % [scene_path.get_file(), spawn_name])
	_is_changing_room = false

func retry() -> void:
	transient.clear()
	if Progress.test_mode:
		enter_room("TEST", "Start", false)
	else:
		enter_room(Progress.checkpoint_room, Progress.checkpoint_spawn, false)
