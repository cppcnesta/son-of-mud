extends CharacterBody2D
## 复用原玩家场景；唯一玩家控制器。
signal died
signal attack_blocked(attack_id: String)
@export_group("配置资源（.tres）")
@export var water_config: MudFormConfig = preload("res://assets/data/player/water.tres")
@export var mud_config: MudFormConfig = preload("res://assets/data/player/mud.tres")
@export var rules: MudPrototypeRules = preload("res://assets/data/player/prototype_rules.tres")
@export_group("调试")
@export var inspection_speed: float = 500.0
var action := MudActionState.new()
var combat := MudPlayerCombat.new()
var recovery := MudMovementRecovery.new()
var guard := MudPlayerGuard.new()
var incoming := MudAttackLedger.new()
var form: int = 0
var facing: float = 1.0
var health: float = 120.0
var dodge_cooldown: float = 0.0
var switch_remaining: float = 0.0
var penalty_remaining: float:
	get: return recovery.remaining
var penalty_total: float:
	get: return recovery.duration
var penalty_initial: float:
	get: return recovery.initial_multiplier
var dodge_profile: MudFormConfig
var block_profile: MudFormConfig:
	get: return guard.profile
var jump_profile: MudFormConfig
## 待执行的起跳独立保存，不能被上一跳的落地清理清空。
var pending_jump_profile: MudFormConfig
var pending_jump_form: int = 0
var pending_jump_module: int = 0
var jump_form: int = 0
var jump_module: int = 0
var jumps_used: int = 0
var jump_origin_y: float = 0.0
var hold_time: float = 0.0
var hold_eligible: bool = false
var reached_cap: bool = false
var jump_in_progress: bool = false
var submerged: bool = false
var peak_height: float = 0.0

func _ready() -> void:
	add_to_group("player")
	form = rules.initial_form
	health = rules.player_health
	combat.actor = self
	guard.actor = self
	add_child(combat)
	if has_node("Sprite2D"):
		$Sprite2D.hide()

func config() -> MudFormConfig:
	return water_config if form == 0 else mud_config

func has_ability(key: String) -> bool:
	return Progress.has_ability(key)

func penalty_multiplier() -> float:
	return recovery.multiplier()

func switch_form() -> bool:
	if not has_ability("form_switch") or switch_remaining > 0.0:
		return false
	if submerged and form == 0:
		return false
	if combat.recovery_remaining > 0.0 and not rules.allow_action_cancel:
		return false
	if not is_on_floor() and not rules.allow_air_switch:
		return false
	if action.kind != MudActionState.Kind.FREE:
		if not rules.allow_action_cancel or action.kind == MudActionState.Kind.DEAD:
			return false
		if action.kind == MudActionState.Kind.DODGE:
			finish_dodge()
		cancel_pending_jump()
		action.enter(MudActionState.Kind.FREE)
	form = 1 - form
	switch_remaining = 0.0 if Progress.test_mode and rules.test_fast_switch else rules.switch_cooldown
	queue_redraw()
	return true

func start_dodge() -> bool:
	var cancel_recovery: bool = combat.can_dodge_cancel() and (action.kind == MudActionState.Kind.FREE or action.kind == MudActionState.Kind.ATTACK_RECOVERY)
	if (action.kind != MudActionState.Kind.FREE and not cancel_recovery) or dodge_cooldown > 0.0:
		return false
	if cancel_recovery:
		combat.reset_attack_interval()
	dodge_profile = config()
	dodge_cooldown = dodge_profile.dodge_cooldown
	action.enter(MudActionState.Kind.DODGE, dodge_profile.dodge_duration)
	return true

func finish_dodge() -> void:
	if dodge_profile != null and dodge_profile.penalty_duration > 0:
		recovery.start(MudMovementRecovery.Source.DODGE, dodge_profile.penalty_duration, dodge_profile.penalty_initial_multiplier, dodge_profile.recovery_curve)
	action.enter(MudActionState.Kind.FREE)

func start_block() -> bool:
	return guard.start()

func request_jump() -> bool:
	if action.kind != MudActionState.Kind.FREE:
		return false
	var wall_jump: bool = has_ability("wall_climb") and is_on_wall() and not is_on_floor()
	if not is_on_floor() and not wall_jump and (not has_ability("double_jump") or jumps_used >= 2):
		return false
	if not is_on_floor() and not wall_jump and jumps_used == 0:
		jumps_used = 1
	pending_jump_profile = config()
	pending_jump_form = form
	pending_jump_module = Progress.mud_module
	action.enter(MudActionState.Kind.JUMP_WINDUP, pending_jump_profile.jump_windup)
	return true

func launch_jump() -> void:
	if action.kind != MudActionState.Kind.JUMP_WINDUP or pending_jump_profile == null:
		cancel_pending_jump()
		return
	jump_profile = pending_jump_profile
	jump_form = pending_jump_form
	jump_module = pending_jump_module
	pending_jump_profile = null
	jumps_used += 1
	velocity.y = -jump_profile.jump_speed
	jump_origin_y = global_position.y
	hold_time = 0.0
	hold_eligible = jump_form == 1 and jump_module == 1
	reached_cap = false
	jump_in_progress = true
	peak_height = 0.0
	action.enter(MudActionState.Kind.FREE)

func cancel_pending_jump() -> void:
	pending_jump_profile = null
	pending_jump_form = 0
	pending_jump_module = 0
	if action.kind == MudActionState.Kind.JUMP_WINDUP:
		action.enter(MudActionState.Kind.FREE)

func clear_air_context(preserve_pending_jump: bool = false) -> void:
	jump_in_progress = false
	reached_cap = false
	hold_eligible = false
	jumps_used = 0
	jump_profile = null
	if not preserve_pending_jump:
		cancel_pending_jump()

func receive_attack(damage: float, attack_id: String, source: Vector2) -> String:
	if not incoming.accept(attack_id) or action.kind == MudActionState.Kind.DEAD:
		return "duplicate"
	if guard.covers(source):
		guard.succeed()
		attack_blocked.emit(attack_id)
		queue_redraw()
		return "blocked"
	if action.kind == MudActionState.Kind.DODGE and rules.dodge_invulnerability > 0.0 and dodge_profile.dodge_duration - action.remaining <= rules.dodge_invulnerability:
		return "invulnerable"
	health = maxf(0, health - damage)
	if action.kind == MudActionState.Kind.DODGE:
		finish_dodge()
	cancel_pending_jump()
	if health <= 0:
		action.enter(MudActionState.Kind.DEAD)
		died.emit()
	else:
		action.enter(MudActionState.Kind.HURT, rules.hurt_duration)
	queue_redraw()
	return "hurt"

func export_transient() -> Dictionary:
	return {"form": form, "health": health, "dodge": dodge_cooldown, "switch": switch_remaining, "recovery": recovery.snapshot(), "block_cooldown": guard.cooldown_remaining, "combat_timing": combat.snapshot(), "bonus": combat.bonus_ready}

func restore_transient(data: Dictionary) -> void:
	form = int(data.get("form", rules.initial_form))
	health = float(data.get("health", rules.player_health))
	dodge_cooldown = float(data.get("dodge", 0))
	switch_remaining = float(data.get("switch", 0))
	if data.has("recovery"):
		recovery.restore(data.recovery, mud_config.recovery_curve)
	else:
		recovery.restore({"source": 1, "remaining": data.get("penalty", 0.0), "duration": data.get("penalty_total", 1.0), "initial": data.get("penalty_initial", 0.2)}, mud_config.recovery_curve)
	guard.cooldown_remaining = maxf(0.0, float(data.get("block_cooldown", 0.0)))
	combat.restore(data.get("combat_timing", {}))
	combat.bonus_ready = bool(data.get("bonus", false))
	clear_air_context()
	velocity = Vector2.ZERO
	action.enter(MudActionState.Kind.FREE)
	queue_redraw()

func _physics_process(delta: float) -> void:
	dodge_cooldown = maxf(0, dodge_cooldown - delta)
	switch_remaining = maxf(0, switch_remaining - delta)
	recovery.tick(delta)
	action.tick(delta)
	guard.tick(delta)
	if action.kind == MudActionState.Kind.DEAD:
		velocity = Vector2.ZERO
		return
	if Input.is_action_just_pressed("switch_form"):
		switch_form()
	if Input.is_action_just_pressed("dodge"):
		start_dodge()
	if Input.is_action_just_pressed("block"):
		start_block()
	if Input.is_action_just_pressed("jump"):
		request_jump()
	if Input.is_action_just_pressed("attack") and action.kind == MudActionState.Kind.FREE and combat.is_ready():
		if Input.is_action_pressed("move_down") and not is_on_floor() and has_ability("heavy_slam"):
			action.enter(MudActionState.Kind.SLAM)
			reached_cap = false
		else:
			combat.start(config(), not is_on_floor() and Input.is_action_pressed("move_down"))
	if action.kind == MudActionState.Kind.JUMP_WINDUP and action.remaining <= 0:
		launch_jump()
	if action.kind == MudActionState.Kind.DODGE and action.remaining <= 0:
		finish_dodge()
	if action.kind == MudActionState.Kind.HURT and action.remaining <= 0:
		action.enter(MudActionState.Kind.FREE)
	combat.tick(delta)
	var axis := Input.get_axis("move_left", "move_right")
	var can_move: bool = action.kind == MudActionState.Kind.FREE or (action.kind == MudActionState.Kind.ATTACK_RECOVERY and combat.can_move_in_recovery())
	if axis != 0 and can_move:
		facing = signf(axis)
	var old_floor := is_on_floor()
	if action.kind == MudActionState.Kind.DODGE:
		velocity.x = facing * dodge_profile.dodge_speed
	else:
		velocity.x = axis * config().move_speed * penalty_multiplier() if can_move else 0.0
	var active_profile: MudFormConfig = jump_profile if jump_profile != null else config()
	if not is_on_floor():
		if active_profile.constant_descent and velocity.y >= 0:
			velocity.y = active_profile.descent_speed
		else:
			var gravity := active_profile.gravity
			if hold_eligible and velocity.y < 0:
				if not Input.is_action_pressed("jump"):
					hold_eligible = false
					velocity.y = maxf(velocity.y, -130.0)
				elif hold_time < active_profile.hold_max_time:
					hold_time += delta
					gravity -= active_profile.hold_acceleration
			velocity.y = minf(velocity.y + gravity * delta, active_profile.terminal_speed)
		if has_ability("wall_climb") and is_on_wall() and axis != 0 and Input.is_action_pressed("move_up") and action.kind == MudActionState.Kind.FREE:
			velocity.y = -180.0
	if submerged and has_ability("dive") and form == 0:
		velocity.y = Input.get_axis("move_up", "move_down") * 200.0
	if action.kind == MudActionState.Kind.SLAM:
		velocity = Vector2(0, rules.slam_speed)
	move_and_slide()
	if jump_in_progress:
		var height := jump_origin_y - global_position.y
		peak_height = maxf(peak_height, height)
		var cap: float = rules.upgraded_hold_height if has_ability("high_jump_upgrade") else active_profile.hold_height_cap
		if jump_form == 1 and jump_module == 1 and height >= cap:
			global_position.y = jump_origin_y - cap
			if hold_eligible:
				reached_cap = true
			velocity.y = maxf(0, velocity.y)
	if is_on_ceiling():
		hold_eligible = false
		reached_cap = false
	if is_on_floor() and not old_floor:
		if action.kind == MudActionState.Kind.SLAM:
			combat.burst(rules.slam_damage, rules.landing_radius, true)
			if form == 1:
				combat.begin_recovery(mud_config)
			else:
				action.enter(MudActionState.Kind.ATTACK_RECOVERY, 0.25)
		elif reached_cap and jump_in_progress:
			combat.burst(rules.landing_damage, rules.landing_radius, false)
		clear_air_context(true)
	elif is_on_floor() and not jump_in_progress:
		jumps_used = 0
	queue_redraw()

func _draw() -> void:
	var tint := config().tint
	if action.kind == MudActionState.Kind.HURT:
		tint = Color("ff6868")
	draw_rect(Rect2(-20, -24, 40, 48), tint)
	draw_line(Vector2(0, -8), Vector2(facing * 25, -8), Color.WHITE, 4)
	if action.kind == MudActionState.Kind.BLOCK:
		draw_arc(Vector2.ZERO, 36, 0, TAU, 32, Color("fff3a3"), 4)
	if combat.bonus_ready:
		draw_circle(Vector2(0, -36), 6, Color("ffdc64"))
	if action.kind == MudActionState.Kind.ATTACK_ACTIVE:
		draw_rect(Rect2(Vector2(18 if facing > 0 else -108, -32), Vector2(90, 64)), Color(1, 0.85, 0.35, 0.45))
