class_name MudActionState
extends RefCounted
enum Kind { FREE, JUMP_WINDUP, ATTACK_WINDUP, ATTACK_ACTIVE, ATTACK_RECOVERY, BLOCK, DODGE, HURT, SLAM, DEAD }
const NAMES := ["移动/待机", "起跳前摇", "攻击前摇", "攻击判定", "攻击后摇", "格挡", "闪避", "受伤", "重压下砸", "死亡"]
var kind: Kind = Kind.FREE
var remaining: float = 0.0
func enter(next: Kind, duration: float = 0.0) -> void:
	kind = next
	remaining = duration
func tick(delta: float) -> void:
	remaining = maxf(0.0, remaining - delta)
	if remaining < 0.000001:
		remaining = 0.0
func label() -> String:
	return NAMES[kind]
