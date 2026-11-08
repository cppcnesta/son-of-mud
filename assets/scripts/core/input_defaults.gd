extends Node
## 仅补充不存在的动作；已有绑定不清空。
func _ready() -> void:
	var defaults := {"switch_form": KEY_Q, "dodge": KEY_SHIFT, "block": KEY_K, "attack": KEY_J, "toggle_module": KEY_B, "debug_unlock": KEY_F6, "restart_flow": KEY_F8, "open_lab": KEY_F2, "retry": KEY_R, "menu": KEY_ESCAPE}
	for key in defaults:
		if InputMap.has_action(key):
			continue
		InputMap.add_action(key)
		var event := InputEventKey.new()
		event.physical_keycode = defaults[key]
		InputMap.action_add_event(key, event)
