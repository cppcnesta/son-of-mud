class_name MudMovementRecovery
extends RefCounted
## 单一移动减速通道。新动作替换旧后摇，不叠乘、不保留隐藏计时。
enum Source { NONE, DODGE, ATTACK }
var source: Source = Source.NONE
var remaining: float = 0.0
var duration: float = 0.0
var initial_multiplier: float = 0.2
var curve: Curve

func start(kind: Source, seconds: float, initial: float, recovery_curve: Curve) -> void:
	source = kind
	duration = maxf(0.0, seconds)
	remaining = duration
	initial_multiplier = clampf(initial, 0.0, 1.0)
	curve = recovery_curve
	if duration <= 0.0:
		clear()

func tick(delta: float) -> void:
	remaining = maxf(0.0, remaining - delta)
	if remaining < 0.000001:
		clear()

func multiplier() -> float:
	if remaining <= 0.0 or duration <= 0.0:
		return 1.0
	var elapsed := clampf(1.0 - remaining / duration, 0.0, 1.0)
	var recovered: float = curve.sample(elapsed) if curve != null else 1.0 - pow(1.0 - elapsed, 2.0)
	return lerpf(initial_multiplier, 1.0, clampf(recovered, 0.0, 1.0))

func clear() -> void:
	source = Source.NONE
	remaining = 0.0
	duration = 0.0

func snapshot() -> Dictionary:
	return {"source": int(source), "remaining": remaining, "duration": duration, "initial": initial_multiplier, "curve_path": curve.resource_path if curve != null else ""}

func restore(data: Dictionary, fallback_curve: Curve) -> void:
	var restored_curve := fallback_curve
	var path := str(data.get("curve_path", ""))
	if not path.is_empty() and ResourceLoader.exists(path, "Curve"):
		restored_curve = load(path) as Curve
	var kind: Source = Source.ATTACK if int(data.get("source", 0)) == 2 else Source.DODGE if int(data.get("source", 0)) == 1 else Source.NONE
	start(kind, float(data.get("duration", 0.0)), float(data.get("initial", 0.2)), restored_curve)
	remaining = clampf(float(data.get("remaining", 0.0)), 0.0, duration)
	if remaining <= 0.0:
		clear()
