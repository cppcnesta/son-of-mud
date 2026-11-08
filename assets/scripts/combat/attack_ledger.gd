class_name MudAttackLedger
extends RefCounted
## 攻击ID全生命周期去重；连续碰撞不重复受伤或格挡。
var seen: Dictionary = {}
func accept(attack_id: String) -> bool:
	if attack_id.is_empty() or seen.has(attack_id):
		return false
	seen[attack_id] = true
	return true
