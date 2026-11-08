# 起跳前摇中落地导致jump_speed空引用的修复

错误：`Invalid access to property or key 'jump_speed' on a base object of type 'Nil'.`

## 原因与复现

玩家下落时发起二段跳，进入起跳前摇；若在前摇结束前落地，旧代码的`clear_air_context()`会把`jump_profile`置空，但动作仍为`JUMP_WINDUP`。计时结束后`launch_jump()`读取空配置的`jump_speed`，触发本次错误。

已在实际Godot4.7.2兼容渲染窗口中复现这一状态。修复前用断言在崩溃前截获，证据是`jump_landing_before_fix.log`及`jump_landing_before_fix_results.json`；其中FAIL是修复前的预期复现记录，不是当前回归结果。上一轮239项检查遗漏了这个边界时序。

## 修改

- `assets/scripts/player/player.gd`新增独立的待起跳配置及对应形态/模组快照。接受请求时保存，真正离地时才转为当前空中轨迹配置。
- 普通落地清理上一跳的空中上下文，保留有效的待起跳请求，因此前摇可以正常结束；不靠临时改用当前形态资源掩盖空引用。
- 受伤、过门恢复、重置和允许的动作取消会取消待起跳请求，防止旧前摇在之后自动弹起。
- 无有效请求时的`launch_jump()`安全退出，不读取Nil，也不消耗跳跃次数。
- `tests/runtime_suite.gd`增加回归矩阵并纳入完整检查；`--jump-landing-only`可单独复测。

## 实际回归

单独回归：**Tests: PASS，24项，失败0项**，Godot4.7.2 / Windows / gl_compatibility。

- 水、泥模组1、泥模组2均实际执行“下落→起跳前摇→先落地→再起跳”。
- 落地只清理旧轨迹，待起跳配置保留；新起跳只消耗一次跳跃。
- 前摇中的普通落地不产生范围攻击；泥模组2新跳跃达到上限后的伤害仍只触发一次。
- 受伤取消后没有幽灵起跳；真实换场后不残留起跳前摇或空中范围伤害资格。
- 空请求入口不会崩溃或增加跳数。

最新完整运行结果见`Test_Report.md`及`runtime_results.json`。本次没有调整形态平衡参数或23房间连接。
