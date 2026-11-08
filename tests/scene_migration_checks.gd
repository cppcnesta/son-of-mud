extends RefCounted

func run(driver: Node) -> bool:
	Progress.new_game(false)
	Progress.enter_test()
	var paths: Array[String] = [
		"res://assets/scenes/whitebox/mp_01_pit.tscn",
		"res://assets/scenes/whitebox/mp_02_pilgrim_path.tscn",
		"res://assets/scenes/whitebox/mp_03_altar.tscn",
		"res://assets/scenes/whitebox/legacy/mp_01_pit.tscn",
		"res://assets/scenes/whitebox/legacy/mp_02_pilgrim_path.tscn",
		"res://assets/scenes/whitebox/legacy/mp_03_altar.tscn",
		"res://assets/scenes/whitebox/legacy/Room01.tscn",
		"res://assets/scenes/whitebox/tests/form_lab.tscn",
		"res://assets/scenes/ui/main_menu.tscn",
	]
	var directory := DirAccess.open("res://assets/scenes/whitebox/levels")
	var level_count := 0
	for filename in directory.get_files():
		if filename.ends_with(".tscn"):
			paths.append("res://assets/scenes/whitebox/levels/" + filename)
			level_count += 1
	driver.check(level_count == 23, "主线仍为23场景，兼容入口不加入主线")
	for path in paths:
		var packed := load(path) as PackedScene
		driver.check(packed != null, "场景依赖加载 " + path)
		if packed == null:
			return false
		var instance := packed.instantiate()
		driver.get_tree().change_scene_to_node(instance)
		await driver.frames(10)
		var is_menu: bool = path.ends_with("main_menu.tscn")
		driver.check(driver.get_tree().current_scene == instance and (is_menu or instance.get_node_or_null("Player") != null), "实际运行 " + path)
		if path.contains("/whitebox/mp_"):
			driver.check(instance.get_node_or_null("Ground") != null and instance.get_node_or_null("Spawns") != null, "兼容入口继承完整地形和出生点 " + path.get_file())
	driver.get_tree().change_scene_to_file("res://assets/scenes/ui/main_menu.tscn")
	await driver.frames(8)
	await driver.snapshot_image("runtime_scene_paths")
	return true
