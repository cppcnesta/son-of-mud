using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.Tools;
using Source = SonOfMud.Player.MovementRecovery.Source;

/// <summary>
/// 计划 v2.1 第 10 步：把旧 tests/MudCombatFeelChecks.cs 的手感断言改写到新玩家上（只新增，不改旧测试）。
/// 每条断言的文字保持旧断言原文，前面加「[迁移]」，方便和旧测试逐条对照。
/// 旧测试里检查关卡目录、文案和美术目录的 3 条不属于玩家手感，不在这里迁移。
/// 结果写到 tests/output/player_feel_results.json。
/// </summary>
public partial class FeelChecks : LabSuiteBase
{
    private const float FloorY = 576f;

    protected override string ReportName => "player_feel_results";

    protected override async Task RunChecks()
    {
        await LoadLab("res://assets/scenes/tests/combat_lab.tscn");
        // 假人移到远处，避免干扰手感测试。
        Lab.GetNode<Node2D>("DummyA").GlobalPosition = new Vector2(2600, 552);
        Lab.GetNode<Node2D>("DummyB").GlobalPosition = new Vector2(2700, 552);
        GameSession.Progress.Unlock(PlayerAbilities.HeavySlam);

        RecoveryCurveChecks();
        await AttackRecoveryChecks();
        await DodgeCancelChecks();
        await WaterBlockChecks();
        await SlamRecoveryChecks();
        await RoomChangeChecks();
    }

    private void Ok(bool condition, string legacyMessage) => Check(condition, "[迁移] " + legacyMessage);

    private async Task WaitForRecovery()
    {
        for (int i = 0; i < 45 && Player.State is not AttackRecoveryState; i++) await Frames(1);
    }

    private void RecoveryCurveChecks()
    {
        Curve curve = Player.Forms.MudConfig.RecoveryCurve;
        foreach (float duration in new[] { 1f, 1.5f })
        {
            var recovery = new MovementRecovery();
            recovery.Start(duration == 1f ? Source.Dodge : Source.Attack, duration, 0.2f, curve);
            Ok(Mathf.Abs(recovery.Multiplier() - 0.2f) < 0.001f, $"{duration:F1}秒后摇从80%减速开始");
            bool correct = true;
            foreach (float sample in new[] { 0.55f, 0.8f, 0.95f, 1f })
            {
                recovery.Tick(duration * 0.25f);
                correct &= Mathf.Abs(recovery.Multiplier() - sample) < 0.005f;
            }
            Ok(correct, $"{duration:F1}秒实际曲线20%→55%→80%→95%→100%");
        }
    }

    private async Task AttackRecoveryChecks()
    {
        await Reset(new Vector2(400, FloorY), 1);
        await Tap(PlayerActions.Attack);
        Ok(Player.State is AttackState { Current: AttackState.Phase.Windup }, "泥普通攻击进入前摇");
        await WaitForRecovery();
        Ok(Player.State is AttackRecoveryState && Mathf.Abs(Player.Timers.Recovery.Remaining - 1.5f) < 0.04f && Player.Timers.Recovery.RecoverySource == Source.Attack,
            "有效判定后进入1.5秒曲线减速后摇");
        float startX = Player.GlobalPosition.X;
        Press(PlayerActions.Right);
        await Frames(15);
        Release(PlayerActions.Right);
        Ok(Player.GlobalPosition.X > startX + 8 && Player.GlobalPosition.X < startX + 70, "攻击后摇期间实际可移动且受减速");
        await Tap(PlayerActions.Attack);
        Ok(Player.State is AttackRecoveryState, "攻击后摇不可连点重开攻击");
        await Tap(PlayerActions.Block);
        Ok(Player.State is AttackRecoveryState && Player.Timers.BlockCooldown == 0f, "攻击后摇前1秒拒绝格挡且不消耗CD");
        for (int i = 0; i < 80 && Player.State is AttackRecoveryState { Remaining: > 0.45f }; i++) await Frames(1);
        Press(PlayerActions.Block);
        await Frames(2);
        Ok(Player.State is BlockState, "泥攻击后摇最后0.5秒允许格挡");
        float previousInterval = Player.Timers.AttackInterval;
        float previousCd = Player.Timers.BlockCooldown;
        HitOutcome outcome = Player.HurtBox.Receive(MakeHit("feel_mud_parry", 1f, Player.GlobalPosition));
        Ok(outcome == HitOutcome.Blocked && Player.Timers.Bonus.Ready && Player.Timers.BlockCooldown == previousCd && Player.Timers.AttackInterval == previousInterval,
            "泥格挡保留增伤但不刷新水形态专属CD/攻击间隔");
        await Frames(3);
        Release(PlayerActions.Block);
        await Frames(3);
        bool free = Player.State is IdleState or RunState;
        await Tap(PlayerActions.Attack);
        Ok(free && Player.State is not AttackState, "提前松开泥格挡不能绕过剩余攻击间隔");
        await Frames(40);
        Ok(Player.Timers.Recovery.Remaining == 0f && Player.Timers.AttackReady, "攻击收势到期后可再次攻击");
    }

    private async Task DodgeCancelChecks()
    {
        foreach (int ticks in new[] { 0, 35, 65 })
        {
            await Reset(new Vector2(400, FloorY), 1);
            await Tap(PlayerActions.Attack);
            await WaitForRecovery();
            await Frames(ticks);
            await Tap(PlayerActions.Dodge);
            Ok(Player.State is DodgeState, $"泥攻击后摇第{ticks}帧可Shift衔接");
            Ok(Player.Timers.AttackInterval == 0f && Player.Timers.Recovery.RecoverySource != Source.Attack, "Shift清除攻击收势及隐藏计时");
            for (int i = 0; i < 40 && Player.State is DodgeState; i++) await Frames(1);
            Ok(Player.Timers.Recovery.RecoverySource == Source.Dodge && Mathf.Abs(Player.Timers.Recovery.Remaining - 1f) < 0.04f &&
               Mathf.Abs(Player.Timers.Recovery.Multiplier() - 0.2f) < 0.03f, "Shift结束后1秒闪避曲线覆盖攻击减速");
            await Frames(30);
            Ok(Mathf.Abs(Player.Timers.Recovery.Multiplier() - 0.8f) < 0.04f, "真实物理帧：闪避后0.5秒约80%移速");
            await Frames(32);
            Ok(Player.Timers.Recovery.Remaining == 0f && Player.Timers.AttackReady && Player.State is IdleState, "闪避收势结束后无残留攻击计时");
        }
    }

    private async Task WaterBlockChecks()
    {
        await Reset(new Vector2(400, FloorY));
        Press(PlayerActions.Block);
        await Frames(2);
        Ok(Player.State is BlockState && Player.Timers.BlockCooldown > 2.9f && Player.Timers.BlockCooldown <= 3f, "水格挡按下时启动3秒CD");
        Release(PlayerActions.Block);
        await Frames(3);
        await Tap(PlayerActions.Block);
        Ok(Player.State is not BlockState, "水未格挡成功时不能提前再挡");
        float cd = Player.Timers.BlockCooldown;
        await Tap(PlayerActions.SwitchForm);
        bool switched = Player.Forms.Form == FormController.Mud;
        bool cdKept = Mathf.Abs(Player.Timers.BlockCooldown - (cd - 2 * Dt)) < 0.0001f;
        await Tap(PlayerActions.Block);
        Ok(switched && cdKept && Player.State is not BlockState, "形态切换不清除格挡CD");
        await Frames(185);
        Ok(Player.Timers.BlockCooldown == 0f, "格挡CD自然到期");

        await Reset(new Vector2(400, FloorY));
        Player.Timers.AttackInterval = 1f;
        Press(PlayerActions.Block);
        await Frames(2);
        Ok(Player.HurtBox.Receive(MakeHit("feel_water_parry", 1f, Player.GlobalPosition)) == HitOutcome.Blocked, "水成功处理格挡");
        Ok(Player.Timers.BlockCooldown == 0f && Player.Timers.AttackInterval == 0f && Player.State is IdleState, "水成功格挡清零格挡CD及攻击间隔");
        await Frames(5);
        Ok(Player.State is IdleState, "水成功格挡后按住不自动续挡");
        Release(PlayerActions.Block);
        await Tap(PlayerActions.Attack);
        Ok(Player.State is AttackState, "水格挡成功可立即反击");
    }

    private async Task SlamRecoveryChecks()
    {
        await Reset(new Vector2(400, FloorY - 160), 1);
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        Release(PlayerActions.Down);
        for (int i = 0; i < 20 && Player.State is SlamState; i++) await Frames(1);
        Ok(Player.State is AttackRecoveryState { Soft: true } && Player.Timers.Recovery.RecoverySource == Source.Attack && Player.Timers.Recovery.Duration == 1.5f,
            "泥下砸落地使用1.5秒可移动攻击收势");
        await Tap(PlayerActions.Dodge);
        Ok(Player.State is DodgeState && Player.Timers.AttackInterval == 0f, "泥下砸后摇也可被Shift覆盖");
        for (int i = 0; i < 40 && Player.State is DodgeState; i++) await Frames(1);
        Ok(Player.Timers.Recovery.RecoverySource == Source.Dodge && Player.Timers.Recovery.Duration == 1f, "泥下砸衔接Shift后仅保留1秒闪避收势");
    }

    /// <summary>
    /// 旧断言用真实换场（MudRoomTravel）；World 不在本分支，按换房契约模拟：过门时 World 先发 RoomExiting，再发 PlayerTeleported。
    /// </summary>
    private async Task RoomChangeChecks()
    {
        await Reset(new Vector2(400, FloorY), 1);
        Player.Timers.Recovery.Start(Source.Dodge, 1f, 0.2f, Player.Forms.MudConfig.RecoveryCurve);
        Player.Timers.Recovery.Remaining = 0.75f;
        Player.Timers.BlockCooldown = 2f;
        Player.Timers.AttackInterval = 0.6f;
        Events.Instance.EmitSignal(Events.SignalName.RoomExiting, "S1_01");
        Player.GlobalPosition = new Vector2(900, FloorY);
        Events.Instance.EmitSignal(Events.SignalName.PlayerTeleported, "Start");
        await Frames(10);
        Ok(Player.Timers.BlockCooldown > 1.7f && Player.Timers.BlockCooldown < 2f && Player.Timers.AttackInterval > 0.3f, "真实换场保留格挡CD及攻击间隔");
        Ok(Player.Timers.Recovery.RecoverySource == Source.Dodge && Player.Timers.Recovery.Remaining > 0.45f && Player.Timers.Recovery.Remaining < 0.75f &&
           Player.Timers.Recovery.Duration == 1f, "真实换场保留曲线来源、剩余时间及总时长");
    }
}
