class_name MudPlayerGuard
extends RefCounted
## 格挡窗口和冷却独立于形态切换、攻击后摇与移动减速。
var actor: CharacterBody2D
var profile: MudFormConfig
var cooldown_remaining: float = 0.0

func tick(delta: float) -> void:
	cooldown_remaining = maxf(0.0, cooldown_remaining - delta)
	if cooldown_remaining < 0.000001:
		cooldown_remaining = 0.0
	if actor.action.kind == MudActionState.Kind.BLOCK and (actor.action.remaining <= 0.0 or not Input.is_action_pressed("block")):
		actor.action.enter(MudActionState.Kind.FREE)

func start() -> bool:
	if not actor.has_ability("block") or cooldown_remaining > 0.0:
		return false
	var state: MudActionState.Kind = actor.action.kind
	if state != MudActionState.Kind.FREE and state != MudActionState.Kind.ATTACK_RECOVERY:
		return false
	if actor.combat.recovery_remaining > 0.0:
		if not actor.combat.profile.soft_attack_recovery or actor.combat.recovery_remaining > actor.combat.profile.block_cancel_window + 0.000001:
			return false
	elif state == MudActionState.Kind.ATTACK_RECOVERY:
		return false
	profile = actor.config()
	cooldown_remaining = profile.block_cooldown
	actor.action.enter(MudActionState.Kind.BLOCK, profile.block_duration)
	return true

func covers(source_position: Vector2) -> bool:
	if actor.action.kind != MudActionState.Kind.BLOCK or profile == null:
		return false
	return actor.rules.omnidirectional_block or (source_position.x - actor.global_position.x) * actor.facing >= 0.0

func succeed() -> void:
	if profile.grants_block_bonus:
		actor.combat.bonus_ready = true
	if profile.reset_block_cooldown_on_success:
		cooldown_remaining = 0.0
	if profile.reset_attack_interval_on_block:
		actor.combat.reset_attack_interval()
	if profile.block_single_hit:
		actor.action.enter(MudActionState.Kind.FREE)
