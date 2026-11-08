extends Node
## 正式状态与测试覆盖层分离；测试不写正式存档。
signal changed
const SAVE_PATH := "user://mudchild_whitebox_v1.json"
const RULES = preload("res://assets/data/player/prototype_rules.tres")
var abilities: Dictionary = {}
var flags: Dictionary = {}
var test_abilities: Dictionary = {}
var test_mode: bool = false
var mud_module: int = 0
var checkpoint_room: String = "M01"
var checkpoint_spawn: String = "Start"
var last_room: String = "M01"
var last_spawn: String = "Start"
var allow_disk: bool = true

func _ready() -> void:
	new_game(false)
	load_game()

func has_ability(key: String) -> bool:
	return bool(test_abilities.get(key, abilities.get(key, false))) if test_mode else bool(abilities.get(key, false))

func unlock(key: String) -> void:
	if test_mode:
		test_abilities[key] = true
	else:
		abilities[key] = true
		save_game()
	changed.emit()

func flag(key: String) -> bool:
	return bool(flags.get(key, false))

func mark(key: String) -> void:
	flags[key] = true
	save_game()
	changed.emit()

func districts_complete() -> bool:
	return flag("district_C04") and flag("district_C05") and flag("district_C06")

func enter_test() -> void:
	test_mode = true
	test_abilities.clear()
	mud_module = RULES.mud_jump_module

func leave_test() -> void:
	test_mode = false
	test_abilities.clear()
	mud_module = RULES.mud_jump_module

func new_game(persist: bool = true) -> void:
	leave_test()
	abilities.clear()
	flags.clear()
	for key in RULES.initial_abilities:
		abilities[key] = true
	checkpoint_room = "M01"
	checkpoint_spawn = "Start"
	last_room = "M01"
	last_spawn = "Start"
	if persist:
		save_game()
	changed.emit()

func set_checkpoint(room: String, spawn: String) -> void:
	if test_mode:
		return
	checkpoint_room = room
	checkpoint_spawn = spawn
	save_game()

func snapshot() -> Dictionary:
	return {"version": 1, "abilities": abilities.duplicate(true), "flags": flags.duplicate(true), "checkpoint_room": checkpoint_room, "checkpoint_spawn": checkpoint_spawn, "last_room": last_room, "last_spawn": last_spawn}

func apply_snapshot(data: Dictionary) -> bool:
	if int(data.get("version", 0)) != 1 or not data.get("abilities") is Dictionary or not data.get("flags") is Dictionary:
		return false
	abilities = data.abilities.duplicate(true)
	flags = data.flags.duplicate(true)
	checkpoint_room = str(data.get("checkpoint_room", "M01"))
	checkpoint_spawn = str(data.get("checkpoint_spawn", "Start"))
	last_room = str(data.get("last_room", checkpoint_room))
	last_spawn = str(data.get("last_spawn", checkpoint_spawn))
	leave_test()
	changed.emit()
	return true

func save_game() -> bool:
	if test_mode or not allow_disk:
		return false
	var file := FileAccess.open(SAVE_PATH + ".tmp", FileAccess.WRITE)
	if file == null:
		push_error("Tests: FAIL 存档写入失败")
		return false
	file.store_string(JSON.stringify(snapshot(), "\t"))
	file.close()
	return DirAccess.rename_absolute(SAVE_PATH + ".tmp", SAVE_PATH) == OK

func load_game() -> bool:
	if not allow_disk or not FileAccess.file_exists(SAVE_PATH):
		return false
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(SAVE_PATH))
	if not data is Dictionary:
		return false
	return apply_snapshot(data)
