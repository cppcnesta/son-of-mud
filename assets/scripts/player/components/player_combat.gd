class_name MudPlayerCombat
extends Node
var actor: CharacterBody2D
var profile: MudFormConfig
var attack_id: String = ""
var sequence: int = 0
var victims: Dictionary = {}
var bonus_ready: bool = false
var down_attack: bool = false
var last_damage: float = 0.0
var landed_bursts: int = 0
var attack_multiplier: float = 1.0
var down_damage_multiplier: float = 1.0
var attack_interval_remaining: float = 0.0
var recovery_remaining: float = 0.0

func is_ready() -> bool:
	return attack_interval_remaining <= 0.0

func start(config: MudFormConfig, downward: bool) -> bool:
	if not is_ready() or actor.action.kind != MudActionState.Kind.FREE:
		return false
	profile = config
	down_attack = downward
	down_damage_multiplier = config.down_attack_multiplier if downward and actor.form == 1 and Progress.mud_module == 0 and actor.velocity.y >= 0 else 1.0
	sequence += 1
	attack_id = "player_%s_%d" % [actor.get_instance_id(), sequence]
	victims.clear()
	attack_multiplier = 1.0
	attack_interval_remaining = config.attack_windup + config.attack_active + config.attack_recovery
	recovery_remaining = 0.0
	actor.action.enter(MudActionState.Kind.ATTACK_WINDUP, config.attack_windup)
	return true

func tick(delta: float = 0.0) -> void:
	attack_interval_remaining = maxf(0.0, attack_interval_remaining - delta)
	recovery_remaining = maxf(0.0, recovery_remaining - delta)
	if attack_interval_remaining < 0.000001:
		attack_interval_remaining = 0.0
	if recovery_remaining < 0.000001:
		recovery_remaining = 0.0
	var action: MudActionState = actor.action
	if action.kind == MudActionState.Kind.ATTACK_ACTIVE:
		var shape := RectangleShape2D.new()
		shape.size = Vector2(profile.attack_reach, 70.0)
		var offset := Vector2(actor.facing * (profile.attack_reach * 0.5 + 16.0), 0.0)
		if down_attack:
			shape.size = Vector2(72, profile.attack_reach)
			offset = Vector2(0, profile.attack_reach * 0.5)
		strike(shape, actor.global_position + offset, profile.damage * down_damage_multiplier, false)
	if action.remaining > 0.0:
		return
	match action.kind:
		MudActionState.Kind.ATTACK_WINDUP:
			action.enter(MudActionState.Kind.ATTACK_ACTIVE, profile.attack_active)
		MudActionState.Kind.ATTACK_ACTIVE:
			begin_recovery(profile)
		MudActionState.Kind.ATTACK_RECOVERY:
			action.enter(MudActionState.Kind.FREE)

func can_move_in_recovery() -> bool:
	return recovery_remaining > 0.0 and profile != null and profile.soft_attack_recovery

func begin_recovery(config: MudFormConfig) -> void:
	profile = config
	recovery_remaining = profile.attack_recovery
	attack_interval_remaining = profile.attack_recovery
	if profile.soft_attack_recovery:
		actor.recovery.start(MudMovementRecovery.Source.ATTACK, profile.attack_recovery, profile.penalty_initial_multiplier, profile.recovery_curve)
	actor.action.enter(MudActionState.Kind.ATTACK_RECOVERY, profile.attack_recovery)

func can_dodge_cancel() -> bool:
	return recovery_remaining > 0.0 and profile != null and profile.dodge_cancel_recovery

func reset_attack_interval() -> void:
	attack_interval_remaining = 0.0
	recovery_remaining = 0.0
	if actor.recovery.source == MudMovementRecovery.Source.ATTACK:
		actor.recovery.clear()
	if actor.action.kind == MudActionState.Kind.ATTACK_RECOVERY:
		actor.action.enter(MudActionState.Kind.FREE)

func snapshot() -> Dictionary:
	return {"interval": attack_interval_remaining, "recovery": recovery_remaining, "profile_path": profile.resource_path if profile != null else ""}

func restore(data: Dictionary) -> void:
	attack_interval_remaining = maxf(0.0, float(data.get("interval", 0.0)))
	recovery_remaining = maxf(0.0, float(data.get("recovery", 0.0)))
	var path := str(data.get("profile_path", ""))
	profile = load(path) as MudFormConfig if not path.is_empty() and ResourceLoader.exists(path, "Resource") else actor.config()
func burst(damage: float, radius: float, heavy: bool) -> void:
	sequence += 1
	attack_id = "landing_%s_%d" % [actor.get_instance_id(), sequence]
	victims.clear()
	attack_multiplier = 1.0
	landed_bursts += 1
	var shape := CircleShape2D.new()
	shape.radius = radius
	strike(shape, actor.global_position, damage, heavy)
func strike(shape: Shape2D, at: Vector2, damage: float, heavy: bool) -> void:
	var query := PhysicsShapeQueryParameters2D.new()
	query.shape = shape
	query.transform = Transform2D(0, at)
	query.collision_mask = 4
	for hit in actor.get_world_2d().direct_space_state.intersect_shape(query, 32):
		var target: Object = hit.collider
		if not target.has_method("receive_hit") or victims.has(target.get_instance_id()):
			continue
		victims[target.get_instance_id()] = true
		var multiplier: float = actor.mud_config.block_bonus_multiplier if bonus_ready else attack_multiplier
		if target.receive_hit(damage * multiplier, attack_id, {"heavy": heavy, "source": actor.global_position}):
			last_damage = damage * multiplier
			attack_multiplier = multiplier
			bonus_ready = false
			actor.queue_redraw()
