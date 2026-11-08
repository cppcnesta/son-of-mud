class_name MudDebugHUD
extends CanvasLayer
var room: Node2D
var status: Label
var message: Label
var prompt: Label
var controls: Label
var lifetime: float = 0.0
func _ready() -> void:
	layer = 10
	var background := ColorRect.new()
	background.color = Color(0.035, 0.055, 0.075, 0.92)
	background.size = Vector2(1152, 178)
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(background)
	status = make_label(Vector2(22, 12), Vector2(1100, 135), 17)
	controls = make_label(Vector2(22, 145), Vector2(1100, 28), 16)
	controls.text = "A/D移动　空格/W跳跃　Q形态　Shift闪避　J攻击　K格挡　S+J空中下砸　E交互　R重试　Esc菜单"
	message = make_label(Vector2(26, 518), Vector2(1100, 95), 20)
	prompt = make_label(Vector2(26, 614), Vector2(1100, 30), 17)
	message.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	message.add_theme_color_override("font_color", Color("ffe2a0"))

func make_label(at: Vector2, dimensions: Vector2, font_size: int) -> Label:
	var label := Label.new()
	label.position = at
	label.size = dimensions
	label.add_theme_font_size_override("font_size", font_size)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(label)
	return label

func say(text: String, duration: float = 7.0) -> void:
	message.text = text
	lifetime = duration

func _process(delta: float) -> void:
	if room == null or not is_instance_valid(room.player):
		return
	lifetime -= delta
	if lifetime <= 0:
		message.text = ""
	var p: CharacterBody2D = room.player
	var unlocked: Array[String] = []
	var names := {"block": "格挡", "form_switch": "形态切换", "double_jump": "二段跳", "wall_climb": "攀墙", "heavy_slam": "重压下砸", "dive": "潜游", "high_jump_upgrade": "高跳升级"}
	for key in names:
		if Progress.has_ability(key):
			unlocked.append(names[key])
	var recovery_name: String = ["无", "闪避", "攻击"][p.recovery.source]
	status.text = "%s　%s　｜　%s形态　泥跳%d　%s　生命%d\n闪避CD %.2fs　格挡CD %.2fs　攻击间隔 %.2fs　格挡窗口 %.2fs\n收势：%s %.2fs　速度%d%%　格挡增伤：%s　能力：%s\n街区 %d/3　%s　｜　F2训练场　B调整跳跃　F6训练能力　F8重新开始" % [room.room_id, room.room_title, p.config().display_name, Progress.mud_module + 1, p.action.label(), ceili(p.health), p.dodge_cooldown, p.guard.cooldown_remaining, p.combat.attack_interval_remaining, p.action.remaining if p.action.kind == MudActionState.Kind.BLOCK else 0.0, recovery_name, p.recovery.remaining, roundi(p.penalty_multiplier() * 100), "就绪" if p.combat.bonus_ready else "无", "、".join(unlocked), int(Progress.flag("district_C04")) + int(Progress.flag("district_C05")) + int(Progress.flag("district_C06")), "独立训练" if Progress.test_mode else "旅程已记录"]
	prompt.text = room.current_prompt
