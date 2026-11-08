extends Control
func _ready() -> void:
	var bg := ColorRect.new()
	bg.color = Color("17252d")
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var title := Label.new()
	title.position = Vector2(100, 80)
	title.text = "泥之子"
	title.add_theme_font_size_override("font_size", 42)
	add_child(title)
	var subtitle := Label.new()
	subtitle.position = Vector2(100, 210)
	subtitle.text = "白盒测试版本"
	subtitle.add_theme_font_size_override("font_size", 21)
	add_child(subtitle)
	button("开始旅程", 310, start_new)
	button("继续旅程", 375, resume)
	button("训练场", 440, lab)
	var hint := Label.new()
	hint.position = Vector2(100, 540)
	hint.text = "A/D移动　空格/W跳跃　Q切换　Shift闪避　J攻击　K格挡　E交互\nF2训练场　B调整泥跳跃　F8重新开始"
	hint.add_theme_font_size_override("font_size", 18)
	add_child(hint)
func button(text: String, y: float, callback: Callable) -> void:
	var control := Button.new()
	control.text = text
	control.position = Vector2(100, y)
	control.size = Vector2(600, 50)
	control.add_theme_font_size_override("font_size", 23)
	control.pressed.connect(callback)
	add_child(control)
func start_new() -> void:
	Progress.new_game()
	RoomTravel.enter_room("M01", "Start", false)
func resume() -> void:
	Progress.leave_test()
	var room: String = Progress.last_room
	if not ResourceLoader.exists(RoomTravel.room_path(room)):
		room = "M01"
	RoomTravel.enter_room(room, Progress.last_spawn if room != "M01" else "Start", false)
func lab() -> void:
	Progress.enter_test()
	RoomTravel.enter_room("TEST", "Start", false)
