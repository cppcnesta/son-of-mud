class_name MudFormConfig
extends Resource
## 时间和比例按设计配置；其余数值仍为白盒测试参数。
@export_group("形态（白盒参数，非正式平衡）")
@export var display_name: String = "水"
@export var tint: Color = Color("64d8ef")
## 像素/秒；水高于泥。
@export var move_speed: float = 360.0
@export_group("闪避")
@export var dodge_speed: float = 760.0
@export var dodge_duration: float = 0.28
@export var dodge_cooldown: float = 0.65
@export var penalty_duration: float = 0.0
@export var penalty_initial_multiplier: float = 0.2
## 0→1的恢复进度；默认为2t-t²，前段恢复快，末段趋于稳定。
@export var recovery_curve: Curve = preload("res://assets/data/player/curves/recovery_ease_out.tres")
@export_group("格挡")
@export var block_duration: float = 0.5
@export var block_single_hit: bool = true
@export var grants_block_bonus: bool = false
@export var block_bonus_multiplier: float = 1.6
## 按下格挡时开始计时。泥形态3秒为暂定值，水形态3秒已确认。
@export var block_cooldown: float = 3.0
@export var reset_block_cooldown_on_success: bool = true
@export var reset_attack_interval_on_block: bool = true
@export_group("攻击：前摇、有效判定、后摇")
@export var damage: float = 10.0
@export var attack_windup: float = 0.08
@export var attack_active: float = 0.10
@export var attack_recovery: float = 0.16
@export var attack_reach: float = 90.0
@export var down_attack_multiplier: float = 1.0
@export_group("攻击后摇衔接")
## 启用后，后摇期间允许移动，移动倍率由恢复曲线控制。
@export var soft_attack_recovery: bool = false
## 攻击后摇最后多少秒允许格挡。
@export var block_cancel_window: float = 0.0
@export var dodge_cancel_recovery: bool = false
@export_group("跳跃（起跳时冻结配置，空中切换不拼接纵向轨迹）")
@export var jump_windup: float = 0.05
@export var jump_speed: float = 600.0
@export var gravity: float = 1600.0
@export var constant_descent: bool = true
@export var descent_speed: float = 300.0
@export var terminal_speed: float = 880.0
@export var hold_acceleration: float = 850.0
@export var hold_max_time: float = 0.24
## 未升级长按跳跃的高度硬上限（像素）。
@export var hold_height_cap: float = 176.0
