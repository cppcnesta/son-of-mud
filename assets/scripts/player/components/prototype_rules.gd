class_name MudPrototypeRules
extends Resource
@export_group("正式进度（测试场解锁独立）")
@export_enum("水", "泥") var initial_form: int = 0
@export var initial_abilities: PackedStringArray = ["block", "form_switch"]
@export var test_fast_switch: bool = true
@export_group("尚未定案的动作规则")
@export var switch_cooldown: float = 0.15
@export var allow_air_switch: bool = true
@export var allow_action_cancel: bool = false
## 暂定全方向格挡；关闭后仅正面。
@export var omnidirectional_block: bool = true
## 暂定闪避无无敌帧。
@export var dodge_invulnerability: float = 0.0
@export var hurt_duration: float = 0.18
@export_group("泥跳跃：互斥原型模组")
@export_enum("模组1：重跳", "模组2：长按") var mud_jump_module: int = 0
@export var upgraded_hold_height: float = 224.0
@export var landing_radius: float = 150.0
@export var landing_damage: float = 30.0
@export var slam_speed: float = 1000.0
@export var slam_damage: float = 44.0
@export_group("白盒战斗")
@export var player_health: float = 120.0
@export var boss_stagger_duration: float = 1.6
@export var boss_vulnerability: float = 2.0
