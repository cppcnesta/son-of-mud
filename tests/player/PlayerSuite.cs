using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.Tools;

/// <summary>
/// 新玩家（player_lab.tscn）的验收测试：计划 v2.1 第 1、2、4（下砸）、8、9 步。
/// 结果写到 tests/output/player_results.json。
/// </summary>
public partial class PlayerSuite : LabSuiteBase
{
    private const float FloorY = 576f;
    private const string LabPath = "res://assets/scenes/tests/player_lab.tscn";

    protected override string ReportName => "player_results";

    private readonly List<Node2D> _spawned = new();
    private readonly List<int> _forms = new();
    private int _shakes;

    protected override async Task RunChecks()
    {
        Events.Instance.PlayerSpawned += p => _spawned.Add(p);
        Events.Instance.PlayerFormChanged += f => _forms.Add(f);
        Events.Instance.CameraShakeRequested += (_, _) => _shakes++;

        await LoadLab(LabPath);
        SpawnChecks();
        await CoyoteChecks();
        await JumpBufferChecks();
        await DescentChecks();
        await WindupChecks();
        await SwitchChecks();
        await DodgeChecks();
        await SlamChecks();
        await WallClimbChecks();
        await TeleportChecks();
        await LockChecks();
        await AnimationChecks();
        await DebugFlyChecks();
    }

    private void SpawnChecks()
    {
        Check(_spawned.Count >= 1 && _spawned[^1] == Player, "出生：发出 Events.PlayerSpawned(玩家)");
        Check(Player.Stats.Health == 70 && Player.Stats.MaxHealth == 70, $"出生：HP 70/70（起始上限）（实际 {Player.Stats.Health}/{Player.Stats.MaxHealth}）");
        Check(Player.State is IdleState && Player.Forms.Form == FormController.Water, "出生：水形态、Idle 状态");
        Check(Player.HasAbility(PlayerAbilities.Block) && Player.HasAbility(PlayerAbilities.SwitchForm) &&
              !Player.HasAbility(PlayerAbilities.WallClimb) && !Player.HasAbility(PlayerAbilities.HeavySlam),
              "能力：开局只有 block 和 switch_form");

        var timers = new PlayerTimers { DodgeCooldown = 1.25f, BlockCooldown = 2f, AttackInterval = 0.5f, SwitchCooldown = 0.1f };
        timers.Recovery.Start(MovementRecovery.Source.Dodge, 1f, 0.2f, Player.Forms.MudConfig.RecoveryCurve);
        timers.Recovery.Remaining = 0.6f;
        timers.Bonus.Grant(1.6f);
        var restored = new PlayerTimers();
        restored.Restore(GD.StrToVar(GD.VarToStr(timers.Snapshot())).AsGodotDictionary(), Player.Forms.MudConfig.RecoveryCurve);
        Check(restored.DodgeCooldown == 1.25f && restored.BlockCooldown == 2f && restored.AttackInterval == 0.5f && restored.SwitchCooldown == 0.1f &&
              restored.Recovery.RecoverySource == MovementRecovery.Source.Dodge && Mathf.Abs(restored.Recovery.Remaining - 0.6f) < 0.0001f &&
              restored.Recovery.Duration == 1f && restored.Bonus.Ready && Mathf.Abs(restored.Bonus.Multiplier - 1.6f) < 0.0001f,
            "PlayerTimers.Snapshot / Restore：冷却、减速、增伤经 VarToStr 往返后不变（留给存档用）");
    }

    // ---------- 第 1 步：土狼时间、跳跃缓冲、下落、前摇、切形态 ----------

    private async Task<bool> WalkOffPlatform()
    {
        await Reset(new Vector2(560, 376));
        Press(PlayerActions.Right);
        for (int i = 0; i < 60 && Player.State is not FallState; i++) await Frames(1);
        Release(PlayerActions.Right);
        return Player.State is FallState;
    }

    private async Task CoyoteChecks()
    {
        bool fell = await WalkOffPlatform();
        Check(fell && Player.Coyote.Active, "土狼时间：走下平台进入 Fall，并开始计土狼时间");
        await Frames(3);
        await Tap(PlayerActions.Jump);
        Check(Player.State is JumpWindupState or RiseState, $"土狼时间：离开平台后第 5 帧（约 0.08 秒）按跳仍能起跳（实际 {Player.State?.Name}）");

        await WalkOffPlatform();
        await Frames(6);
        await Tap(PlayerActions.Jump);
        await Frames(2);
        Check(Player.State is FallState, $"土狼时间：离开平台后第 8 帧（约 0.13 秒）再按跳不能起跳（实际 {Player.State?.Name}）");
    }

    /// <summary>从空中下落（水形态恒速 300），在离落地还有 leadSeconds 时按跳，返回落地后的状态。</summary>
    private async Task<State> DropAndPressJump(float leadSeconds)
    {
        await Reset(new Vector2(900, 250));
        float pressAt = FloorY - 300f * leadSeconds;
        for (int i = 0; i < 120 && Player.GlobalPosition.Y < pressAt; i++) await Frames(1);
        await Tap(PlayerActions.Jump);
        for (int i = 0; i < 60 && Player.State is FallState; i++) await Frames(1);
        return Player.State;
    }

    private async Task JumpBufferChecks()
    {
        State after = await DropAndPressJump(0.08f);
        Check(after is JumpWindupState, $"跳跃缓冲：落地前约 0.08 秒按跳，落地即起跳（实际 {after?.Name}）");
        after = await DropAndPressJump(0.25f);
        await Frames(2);
        Check(after is IdleState && Player.State is IdleState, $"跳跃缓冲：落地前约 0.25 秒按跳（超过 0.12 秒窗口），落地不起跳（实际 {after?.Name}）");
    }

    private async Task<(float height, List<float> fallSpeeds)> MeasureJump(int form, int samples)
    {
        await Reset(new Vector2(900, FloorY), form);
        float startY = Player.GlobalPosition.Y;
        float minY = startY;
        await Tap(PlayerActions.Jump);
        for (int i = 0; i < 90 && Player.State is not FallState; i++)
        {
            await Frames(1);
            minY = Mathf.Min(minY, Player.GlobalPosition.Y);
        }
        var speeds = new List<float>();
        for (int i = 0; i < samples && Player.State is FallState; i++)
        {
            await Frames(1);
            if (Player.State is FallState) speeds.Add(Player.Velocity.Y);
        }
        return (startY - minY, speeds);
    }

    private async Task DescentChecks()
    {
        var (waterHeight, waterSpeeds) = await MeasureJump(0, 8);
        Metric("water_jump_height", waterHeight);
        Check(waterSpeeds.Count >= 6 && waterSpeeds.TrueForAll(v => Mathf.Abs(v - 300f) < 0.01f),
            $"水形态：过顶点后恒速下落 300（采样 {string.Join(",", waterSpeeds)}）");
        Check(waterHeight > 100f && waterHeight < 125f, $"水形态：跳高与白盒一致约 117px（实际 {waterHeight:F1}）");

        var (mudHeight, mudSpeeds) = await MeasureJump(1, 8);
        Metric("mud_jump_height", mudHeight);
        bool accelerating = mudSpeeds.Count >= 4;
        for (int i = 1; i < mudSpeeds.Count; i++) accelerating &= mudSpeeds[i] > mudSpeeds[i - 1] && mudSpeeds[i] <= 880f;
        Check(accelerating, $"泥形态（模组 1）：下落按重力加速且不超过终端速度 880（采样 {string.Join(",", mudSpeeds)}）");
    }

    private async Task WindupChecks()
    {
        foreach (var (form, expected) in new[] { (0, 0.05f), (1, 0.24f) })
        {
            await Reset(new Vector2(900, FloorY), form);
            await Tap(PlayerActions.Jump);
            int frames = 0;
            while (Player.State is JumpWindupState && frames < 60)
            {
                frames++;
                await Frames(1);
            }
            float measured = frames * Dt;
            Metric($"windup_{form}", measured);
            Check(Mathf.Abs(measured - expected) <= Dt + 0.001f && Player.State is RiseState,
                $"{(form == 0 ? "水" : "泥")}形态起跳前摇 {expected} 秒（实测 {measured:F3} 秒，{frames} 帧）");
        }
    }

    private async Task SwitchChecks()
    {
        await Reset(new Vector2(900, FloorY));
        FormConfig mud = Player.Forms.MudConfig;
        Player.Timers.DodgeCooldown = 2f;
        Player.Timers.BlockCooldown = 1.5f;
        Player.Timers.Recovery.Start(MovementRecovery.Source.Dodge, 1f, 0.2f, mud.RecoveryCurve);
        Player.Timers.Bonus.Grant(1.6f);
        int formEvents = _forms.Count;
        await Tap(PlayerActions.SwitchForm);
        Check(Player.Forms.Form == FormController.Mud && _forms.Count == formEvents + 1 && _forms[^1] == 1,
            "按 Q 切到泥形态，并发出 Events.PlayerFormChanged(1)");
        Check(Player.Timers.DodgeCooldown > 1.9f && Player.Timers.DodgeCooldown < 2f && Player.Timers.BlockCooldown > 1.4f && Player.Timers.BlockCooldown < 1.5f,
            $"切形态不刷新闪避 / 格挡冷却（闪避 {Player.Timers.DodgeCooldown:F3}，格挡 {Player.Timers.BlockCooldown:F3}）");
        Check(Player.Timers.Recovery.RecoverySource == MovementRecovery.Source.Dodge && Player.Timers.Recovery.Remaining > 0.9f && Player.Timers.Bonus.Ready,
            "切形态不解除减速、不清掉也不重复发放增伤");
        await Tap(PlayerActions.SwitchForm);
        Check(Player.Forms.Form == FormController.Mud, "切换冷却 0.15 秒内再按 Q 不切换");
        await Frames(10);
        await Tap(PlayerActions.SwitchForm);
        Check(Player.Forms.Form == FormController.Water, "切换冷却结束后可以再切");

        await Frames(10);
        var key = new InputEventKey { PhysicalKeycode = Key.Q, Keycode = Key.Q, Pressed = true };
        Input.ParseInputEvent(key);
        await Frames(2);
        key.Pressed = false;
        Input.ParseInputEvent(key);
        await Frames(1);
        Check(Player.Forms.Form == FormController.Mud, "物理键 Q 通过 InputMap 触发切形态");

        await Reset(new Vector2(900, FloorY), 1);
        await Tap(PlayerActions.Jump);
        await Tap(PlayerActions.SwitchForm);
        Check(Player.Forms.Form == FormController.Mud && Player.State is JumpWindupState, "起跳前摇中不能切形态");

        await Reset(new Vector2(900, FloorY), 0);
        await Tap(PlayerActions.Jump);
        for (int i = 0; i < 30 && Player.State is not RiseState; i++) await Frames(1);
        await Tap(PlayerActions.SwitchForm);
        FormConfig profile = Player.JumpProfile;
        for (int i = 0; i < 90 && Player.State is not FallState; i++) await Frames(1);
        await Frames(3);
        Check(Player.Forms.Form == FormController.Mud && profile == Player.Forms.WaterConfig && Mathf.Abs(Player.Velocity.Y - 300f) < 0.01f,
            "上升中可以切形态，但这次跳跃的轨迹不变（仍按水形态恒速 300 下落）");
    }

    // ---------- 第 2 步：闪避、减速恢复、冷却 ----------

    private async Task DodgeChecks()
    {
        await Reset(new Vector2(600, FloorY), 0);
        float startX = Player.GlobalPosition.X;
        await Tap(PlayerActions.Dodge);
        Check(Player.State is DodgeState && Mathf.Abs(Player.Timers.DodgeCooldown - 0.65f) < Dt + 0.001f,
            $"水闪避：进入闪避，冷却 0.65 秒（实际 {Player.Timers.DodgeCooldown:F3}）");
        for (int i = 0; i < 40 && Player.State is DodgeState; i++) await Frames(1);
        float waterDistance = Player.GlobalPosition.X - startX;
        Metric("dodge_water_distance", waterDistance);
        Check(Player.Timers.Recovery.RecoverySource == MovementRecovery.Source.None, "水闪避结束后没有减速");
        await Tap(PlayerActions.Dodge);
        Check(Player.State is not DodgeState, "水闪避冷却中不能再次闪避");
        for (int i = 0; i < 60 && !Player.Timers.DodgeReady; i++) await Frames(1);
        await Tap(PlayerActions.Dodge);
        Check(Player.State is DodgeState, "水闪避冷却结束后可以再次闪避");

        await Reset(new Vector2(600, FloorY), 1);
        startX = Player.GlobalPosition.X;
        await Tap(PlayerActions.Dodge);
        Check(Player.State is DodgeState && Mathf.Abs(Player.Timers.DodgeCooldown - 4f) < Dt + 0.001f,
            $"泥闪避：冷却 4 秒（实际 {Player.Timers.DodgeCooldown:F3}）");
        for (int i = 0; i < 40 && Player.State is DodgeState; i++) await Frames(1);
        float mudDistance = Player.GlobalPosition.X - startX;
        Metric("dodge_mud_distance", mudDistance);
        Check(waterDistance > mudDistance && mudDistance > 50f, $"闪避距离：水大于泥（水 {waterDistance:F0}，泥 {mudDistance:F0}）");
        Check(Player.Timers.Recovery.RecoverySource == MovementRecovery.Source.Dodge && Mathf.Abs(Player.Timers.Recovery.Multiplier() - 0.2f) < 0.03f,
            $"泥闪避结束：开始 1 秒减速，起始倍率约 0.2（实际 {Player.Timers.Recovery.Multiplier():F3}）");
        await Frames(30);
        Check(Mathf.Abs(Player.Timers.Recovery.Multiplier() - 0.8f) < 0.04f, $"泥闪避后 0.5 秒约恢复到 80% 移速（实际 {Player.Timers.Recovery.Multiplier():F3}）");
        float x0 = Player.GlobalPosition.X;
        Press(PlayerActions.Right);
        await Frames(6);
        Release(PlayerActions.Right);
        float slowSpeed = (Player.GlobalPosition.X - x0) / (6 * Dt);
        Check(slowSpeed > 200f && slowSpeed < 280f, $"减速期间实际移速低于泥形态正常 280（实测 {slowSpeed:F0}px/s）");
        await Frames(32);
        Check(Player.Timers.Recovery.Multiplier() == 1f && Player.Timers.Recovery.RecoverySource == MovementRecovery.Source.None, "泥闪避减速 1 秒后完全恢复");
        await Tap(PlayerActions.Dodge);
        Check(Player.State is not DodgeState, "泥闪避 4 秒冷却中不能再次闪避");
    }

    // ---------- 第 4 步：下砸砸开脆弱地面 ----------

    private async Task SlamChecks()
    {
        var floor = Lab.GetNode<LabBreakable>("FragileFloor");
        var outcomes = new List<HitOutcome>();
        Player.AttackHitBox.HitLanded += (_, _, o) => outcomes.Add((HitOutcome)o);

        await Reset(new Vector2(2096, 520), 1);
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        Check(Player.State is AttackState { Down: true }, "没有下砸能力时，空中下+攻击是下劈（AttackState.Down）");
        for (int i = 0; i < 40 && Player.State is AttackState; i++) await Frames(1);
        Check(GodotObject.IsInstanceValid(floor) && !floor.Broken && outcomes.Contains(HitOutcome.Ignored),
            $"普通下劈打到脆弱地面：结果 Ignored，地面不碎（结果 {string.Join(",", outcomes)}）");

        GameSession.Progress.Unlock(PlayerAbilities.HeavySlam);
        await Reset(new Vector2(2096, 300), 1);
        int shakes = _shakes;
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        Check(Player.State is SlamState, "有 heavy_slam 时，空中下+攻击进入下砸");
        for (int i = 0; i < 60 && Player.State is SlamState; i++) await Frames(1);
        Check(Player.State is AttackRecoveryState { Soft: true } && _shakes == shakes + 1,
            "泥形态下砸落地：进入 1.5 秒可移动后摇，并请求镜头震动");
        await Frames(2);
        Check(!GodotObject.IsInstanceValid(floor) || floor.Broken, "下砸（重击 44）砸开脆弱地面");
        for (int i = 0; i < 90 && Player.GlobalPosition.Y < 800f; i++) await Frames(1);
        Check(Player.GlobalPosition.Y > 800f, $"脆弱地面碎了之后玩家掉进下面的坑（y={Player.GlobalPosition.Y:F0}）");

        await Reset(new Vector2(600, 200), 0);
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        for (int i = 0; i < 60 && Player.State is SlamState; i++) await Frames(1);
        Check(Player.State is AttackRecoveryState { Soft: false }, "水形态下砸落地：站定后摇（不可移动）");
        for (int i = 0; i < 30 && Player.State is AttackRecoveryState; i++) await Frames(1);
        Check(Player.State is IdleState, "水形态下砸后摇 0.25 秒后回到 Idle");
    }

    // ---------- 第 8 步：攀墙能力门、传送清理、输入锁 ----------

    private async Task<(float minY, bool climbed, bool sawClimb)> TryClimb()
    {
        await Reset(new Vector2(1300, FloorY), 0);
        Press(PlayerActions.Right);
        Press(PlayerActions.Up);
        await Tap(PlayerActions.Jump);
        float minY = Player.GlobalPosition.Y;
        bool sawClimb = false;
        for (int i = 0; i < 240; i++)
        {
            await Frames(1);
            minY = Mathf.Min(minY, Player.GlobalPosition.Y);
            sawClimb |= Player.State is WallClimbState;
            if (Player.GlobalPosition.X > 1420f && Player.IsOnFloor()) break;
        }
        ReleaseInputs();
        await Frames(2);
        bool climbed = Player.GlobalPosition.X > 1420f && Player.GlobalPosition.Y < 152f;
        return (minY, climbed, sawClimb);
    }

    private async Task WallClimbChecks()
    {
        var (minY, climbed, sawClimb) = await TryClimb();
        Check(!climbed && !sawClimb && minY > 152f + 200f, $"没有攀墙能力：448px 的墙上不去（最高 y={minY:F0}，墙顶 152）");
        GameSession.Progress.Unlock(PlayerAbilities.WallClimb);
        (minY, climbed, sawClimb) = await TryClimb();
        Check(climbed && sawClimb, $"有攀墙能力：贴墙按上爬上 448px 的墙（最终 {Player.GlobalPosition}）");
        Check(Player.State is not WallClimbState, "爬上墙顶后离开攀墙状态，回到地面状态");
    }

    private async Task TeleportChecks()
    {
        await Reset(new Vector2(900, FloorY), 1);
        Player.Timers.DodgeCooldown = 2f;
        Player.Timers.BlockCooldown = 1f;
        Player.Timers.Recovery.Start(MovementRecovery.Source.Dodge, 1f, 0.2f, Player.Forms.MudConfig.RecoveryCurve);
        Player.Timers.Bonus.Grant(1.6f);
        await Tap(PlayerActions.Attack);
        for (int i = 0; i < 20 && !Player.AttackHitBox.Active; i++) await Frames(1);
        bool wasActive = Player.AttackHitBox.Active;
        Player.Buffer.Press(PlayerActions.Jump);
        Player.Buffer.Press(PlayerActions.Dodge);
        Player.Velocity = new Vector2(123f, -50f);
        Events.Instance.EmitSignal(Events.SignalName.PlayerTeleported, "door_test");
        Check(wasActive && Player.State is IdleState && Player.Velocity == Vector2.Zero && !Player.AttackHitBox.Active,
            "PlayerTeleported：回 Idle、速度清零、关掉正在生效的 HitBox");
        Check(!Player.Buffer.Has(PlayerActions.Jump, 10f) && !Player.Buffer.Has(PlayerActions.Dodge, 10f) && Player.JumpProfile == null && !Player.Coyote.Active,
            "PlayerTeleported：清空跳跃 / 动作缓冲和起跳参数");
        Check(Player.Timers.DodgeCooldown > 1.5f && Player.Timers.BlockCooldown > 0.5f &&
              Player.Timers.Recovery.RecoverySource == MovementRecovery.Source.Dodge && Player.Timers.Bonus.Ready,
            "PlayerTeleported：冷却、闪避减速、泥增伤保留");
        await Frames(3);
        Check(Player.State is IdleState && !Player.AttackHitBox.Active, "传送后不会残留攻击判定，也不会被缓冲的跳 / 闪避触发动作");

        await Reset(new Vector2(600, 200), 1);
        int shakes = _shakes;
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        bool slamming = Player.State is SlamState;
        Events.Instance.EmitSignal(Events.SignalName.PlayerTeleported, "door_test");
        for (int i = 0; i < 60 && !Player.IsOnFloor(); i++) await Frames(1);
        await Frames(2);
        Check(slamming && Player.State is IdleState or RunState && _shakes == shakes, "PlayerTeleported：清除下砸资格（传送后落地不再触发下砸）");
    }

    private async Task LockChecks()
    {
        await Reset(new Vector2(900, FloorY), 0);
        InputLock.Request(InputLock.Narrative, true);
        await Frames(1);
        Check(Player.State is LockedState && !Player.HurtBox.Receiving, "输入锁：进入 Locked，期间不受击");
        float x = Player.GlobalPosition.X;
        Press(PlayerActions.Right);
        await Tap(PlayerActions.Jump);
        await Frames(10);
        Release(PlayerActions.Right);
        Check(Mathf.Abs(Player.GlobalPosition.X - x) < 1f && Player.State is LockedState, "输入锁：按键无效，不移动不起跳");
        InputLock.Request(InputLock.Narrative, false);
        await Frames(2);
        Check(Player.State is IdleState && Player.HurtBox.Receiving, "输入锁解除：回到 Idle，恢复受击");
    }

    // ---------- 第 9 步：动画占位、调试 HUD、调试飞行 ----------

    private async Task AnimationChecks()
    {
        var hud = Lab.GetNode<PlayerDebugHud>("DebugHud");
        await Reset(new Vector2(900, FloorY), 0);
        hud.Refresh();
        Check(hud.PlayingAnimation == "water_idle" && hud.StateName == "待机", $"HUD：水形态待机显示动画 water_idle（实际 {hud.PlayingAnimation} / {hud.StateName}）");
        await Tap(PlayerActions.SwitchForm);
        await Frames(1);
        hud.Refresh();
        Check(hud.PlayingAnimation == "mud_idle" && hud.Form == 1, $"HUD：切到泥形态后动画为 mud_idle（实际 {hud.PlayingAnimation}）");
        await Tap(PlayerActions.Jump);
        hud.Refresh();
        float expected = 4f / (12f * 0.24f);
        Check(hud.PlayingAnimation == "mud_jump_windup" && Mathf.Abs(Player.Animator.CurrentSpeedScale - expected) < 0.001f,
            $"动画：泥起跳前摇 4 帧、12FPS、0.24 秒 → SpeedScale={expected:F3}（实际 {hud.PlayingAnimation} {Player.Animator.CurrentSpeedScale:F3}）");
        Check(Mathf.Abs(PlayerAnimator.SpeedScaleFor(6, 12f, 0.5f) - 1f) < 0.0001f, "动画：SpeedScale = 帧数 ÷ (基础 FPS × 目标时长)，6 帧 12FPS 0.5 秒 = 1");
        await Reset(new Vector2(900, FloorY), 0);
        await Tap(PlayerActions.Attack);
        hud.Refresh();
        Check(hud.PlayingAnimation == "water_attack" && hud.StateName == "攻击前摇", $"HUD：水攻击显示 water_attack / 攻击前摇（实际 {hud.PlayingAnimation} / {hud.StateName}）");
        await Frames(30);
        Check(hud.Health == Player.Stats.Health && hud.MaxHealth == 70, "HUD：血量来自 Events.PlayerHealthChanged（起始上限 70）");
    }

    private async Task ToggleFly()
    {
        var key = new InputEventKey { PhysicalKeycode = Key.F, Keycode = Key.F, Pressed = true };
        Input.ParseInputEvent(key);
        await Frames(2);
        key.Pressed = false;
        Input.ParseInputEvent(key);
        await Frames(2);
    }

    private async Task DebugFlyChecks()
    {
        await Reset(new Vector2(2600, FloorY), 0);
        Press(PlayerActions.Right);
        await Frames(30);
        Release(PlayerActions.Right);
        Check(Player.GlobalPosition.X < 2700f - 23f, $"没有调试飞行时被墙挡住（x={Player.GlobalPosition.X:F0}）");
        await ToggleFly();
        Check(Player.State is DebugFlyState && !Player.HurtBox.Receiving, "按 F 进入调试飞行（Events.DebugFlyToggled），期间不受击");
        Press(PlayerActions.Right);
        Press(PlayerActions.Up);
        await Frames(20);
        ReleaseInputs();
        Check(Player.GlobalPosition.X > 2764f + 24f && Player.GlobalPosition.Y < FloorY - 50f, $"调试飞行：可以向上飞并穿过墙（{Player.GlobalPosition}）");
        await ToggleFly();
        Check(Player.State is FallState or IdleState && Player.CollisionMask == Layers.World, "再按 F 退出调试飞行，恢复身体碰撞");
    }
}
