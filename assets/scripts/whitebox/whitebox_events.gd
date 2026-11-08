class_name MudWhiteboxEvents
extends RefCounted
## 房间交互与进度事件，保留房间自身作为公开调用入口。
var room: Node2D

func perform(key: String, object: Node2D) -> void:
	var info: Dictionary = {}
	for entry in room.data.events:
		if entry.id == key:
			info = entry
			break
	if info.is_empty():
		return
	if info.has("ability"):
		Progress.unlock(info.ability)
	if key.begins_with("gate_"):
		var number := int(key.trim_prefix("gate_"))
		if number > 1 and not Progress.flag("gate_%d" % (number - 1)):
			room.show_message("请从下方向上，先完成前一道门。")
			return
		Progress.mark(key)
		if number == 7:
			Progress.mark("seven_gates")
	match key:
		"rest":
			room.player.health = room.player.rules.player_health
			Progress.set_checkpoint(room.room_id, "Start")
		"arena":
			room.start_boss()
		"igi":
			Progress.mark("met_igi")
		"flood":
			if not Progress.flag("flood_done"):
				Progress.mark("flood_started")
				room.flood_timer = 3.0
		"after_flood":
			if not Progress.flag("flood_done"):
				room.show_message("先操作水闸，等待洪水退去。")
				return
		"omen_1":
			if not Progress.has_ability("dive") or room.player.form != 0 or room.player.global_position.y < room.floor_y - 140:
				room.show_message("需要潜游能力，并以水形态抵达池底。")
				return
			Progress.mark("district_C04")
		"omen_2":
			if not Progress.flag("obstacle_C05"):
				room.show_message("先用重压下砸打碎裂板。")
				return
			Progress.mark("district_C05")
		"sigil_1":
			room.local_sequence = 1
		"sigil_2":
			room.local_sequence = 2 if room.local_sequence == 1 else 0
			room.show_message("征兆②响应" if room.local_sequence == 2 else "顺序不符，请从①重新开始。")
			return
		"sigil_3":
			if room.local_sequence != 2:
				room.local_sequence = 0
				room.show_message("顺序不符，请从①重新开始。")
				return
			Progress.mark("district_C06")
		"truth", "ending_awake", "ending_sleep":
			if not Progress.flag("boss_H03"):
				room.show_message("先击败心脏前的敌人。")
				return
			if key == "truth":
				Progress.mark("truth")
			elif not Progress.flag("truth"):
				room.show_message("还有尚未拾起的记忆。")
				return
			elif key == "ending_sleep" and Progress.flag("ending_sleep"):
				Progress.new_game()
				RoomTravel.enter_room("M01", "Start", false)
				return
			else:
				Progress.mark(key)
		"test_all":
			room.debug_unlock()
		"test_clear":
			Progress.test_abilities.clear()
			for ability in ["double_jump", "wall_climb", "heavy_slam", "dive", "high_jump_upgrade"]:
				Progress.test_abilities[ability] = false
		"test_source":
			room.source_enabled = not room.source_enabled
		"test_target":
			for enemy in room.get_tree().get_nodes_in_group("damageables"):
				enemy.queue_free()
			room.spawn_enemy(Vector2(1940, room.floor_y - 32), "训练靶", "dummy", 400)
		"test_exit":
			room.open_menu()
	object.used = true
	object.queue_redraw()
	if not Progress.test_mode:
		Progress.mark(room.room_id + ":" + key)
	room.show_message(info.get("text", "征兆已响应。"))

