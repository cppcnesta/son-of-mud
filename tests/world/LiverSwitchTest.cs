using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player.States;
using SonOfMud.World;

/// <summary>
/// 泥肝开关（World.LiverSwitch）+ 关联的门，从正式入口 main_game.tscn 启动、真实玩家、真实按键。
/// 开关还没摆进任何正式房间，测试把 tests/world/liver_switch_fixture.tscn（开关 + 一扇没配条件的门）放进当前房间：
/// ① 摆好后：开关受击框在敌人受击层、只认 player_liver 标签；门自动配上「需要 test_liver_switch」，关着、真的挡路；
/// ② 敌人的投掷物（哪怕带着 player_liver 标签）飞过开关：不触发；
/// ③ 玩家真实按 J 近战打它：结果是 Ignored，不触发；
/// ④ 玩家真实按 R 扔泥肝打中：旗标写入、Activated 一次、门立即打开，玩家能走过去；
/// ⑤ 存档后读档：旗标还在，重新摆进房间的开关一开始就是已触发，门一开始就是开的；
/// ⑥ 重开一局：旗标清空，开关回到未触发，门关着。
/// 结果写到 tests/output/enemy_liver_switch_results.json。
/// </summary>
public partial class LiverSwitchTest : EnemyFlowBase
{
    protected override string SuiteName => "liver_switch";

    private const string FixtureScene = "res://tests/world/liver_switch_fixture.tscn";
    private static readonly StringName Flag = "test_liver_switch";

    private Node2D _fixture;
    private LiverSwitch _switch;
    private GateBarrier _gate;
    private int _activated;

    protected override async Task RunChecks()
    {
        ListenEnemy();
        try
        {
            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            await WaitSettled();
            Check(finished && Starts.Count == 1 && Starts[0].success && Player != null && RoomId == "S1_01", "新游戏：从正式入口开局，玩家在 S1_01 出生");
            if (Player == null) return;
            await PlaceChecks();
            await EnemyProjectileChecks();
            await MeleeChecks();
            await LiverChecks();
            await SaveChecks();
            await RestartChecks();
        }
        finally
        {
            UnlistenEnemy();
        }
    }

    // ---------- 工具 ----------

    /// <summary>把开关 + 门放进当前房间，原点放在玩家脚下（开关在右边 220，门在右边 420）。</summary>
    private async Task Place()
    {
        _fixture = GD.Load<PackedScene>(FixtureScene).Instantiate<Node2D>();
        World.CurrentRoom.AddChild(_fixture);
        _fixture.GlobalPosition = Player.GlobalPosition + new Vector2(0f, 24f);
        _switch = _fixture.GetNode<LiverSwitch>("Switch");
        _gate = _fixture.GetNode<GateBarrier>("Gate");
        _activated = 0;
        _switch.Activated += () => _activated++;
        await Frames(2);
    }

    private bool GateClosed => _gate != null && !_gate.IsOpen && _gate.Visible && _gate.CollisionLayer == Layers.World;
    private bool GateOpen => _gate != null && _gate.IsOpen && !_gate.Visible && _gate.CollisionLayer == 0;

    private async Task Settle()
    {
        RealKeys.ReleaseAll();
        await WaitUntil(() => Player.IsOnFloor() && PlayerFree && Mathf.Abs(Player.Velocity.X) < 1f, 300);
        await Frames(2);
    }

    private async Task TapKey(Key key)
    {
        RealKeys.Down(key);
        await Frames(1);
        RealKeys.Up(key);
        await Frames(1);
    }

    private float DxToSwitch => _switch.GlobalPosition.X - Player.GlobalPosition.X;

    /// <summary>用真实方向键走到开关左边 [min, max] 像素处并面朝它。</summary>
    private async Task<bool> StandLeftOfSwitch(float min, float max)
    {
        for (int i = 0; i < 6; i++)
        {
            if (DxToSwitch > max) await HoldKeyUntil(RealKeys.Right, () => DxToSwitch <= (min + max) * 0.5f, 300);
            else if (DxToSwitch < min) await HoldKeyUntil(RealKeys.Left, () => DxToSwitch >= (min + max) * 0.5f, 300);
            await Settle();
            if (Player.Facing < 0f)
            {
                await TapKey(RealKeys.Right);
                await Settle();
            }
            if (DxToSwitch >= min && DxToSwitch <= max && Player.Facing > 0f) return true;
        }
        return false;
    }

    // ---------- ① 摆放 ----------

    private async Task PlaceChecks()
    {
        await Settle();
        await Place();
        Check(_switch.HurtBox != null && _switch.HurtBox.CollisionLayer == Layers.EnemyHurt && _switch.HurtBox.RequiredTag == LiverSwitch.DefaultTag,
            "开关：受击框在敌人受击层（只有玩家方的攻击查得到），只认 player_liver 标签");
        Check(_gate.Requirement != null && _gate.Requirement.Flags.Contains(Flag) && !_switch.Triggered && !GameSession.Progress.HasVar(Flag) && GateClosed,
            "门：没配条件的门由开关自动配上「需要 test_liver_switch」；开关未触发，门关着");

        // 门真的挡路：一直往右走，停在门左边
        float gateLeft = _gate.GlobalPosition.X - 32f;
        await HoldKeyUntil(RealKeys.Right, () => false, 150);
        float right = Player.GlobalPosition.X + 24f;
        Check(right <= gateLeft + 1f && right > gateLeft - 8f, $"门关着时挡路：玩家一直往右走，停在门左边（玩家右边缘 {right:F0}，门左边 {gateLeft:F0}）");
        await HoldKeyUntil(RealKeys.Left, () => DxToSwitch >= 220f, 300);
        await Settle();
    }

    // ---------- ② 敌人的投掷物 ----------

    private async Task EnemyProjectileChecks()
    {
        Vector2 from = _switch.GlobalPosition + new Vector2(-90f, -28f);
        var spec = new HitSpec { Damage = 15f, Tags = new[] { LiverSwitch.DefaultTag } };
        var flat = new ProjectileSpec { Gravity = 0f, MaxLifetime = 1f };
        Projectile p = Projectile.Launch(World.CurrentRoom, from, new Vector2(500f, 0f), spec, "test_enemy_liver_1", null, ProjectileTeam.Enemy, flat);
        HurtBox target = null;
        float endX = 0f;
        p.Finished += (reason, position) => { target = p.Target; endX = position.X; };
        await WaitUntil(() => !GodotObject.IsInstanceValid(p), 120);
        await Frames(2);
        Check(target == null && endX > _switch.GlobalPosition.X + 20f && !_switch.Triggered && _activated == 0 && GateClosed,
            $"敌人的投掷物（带着 player_liver 标签）飞过开关（一直飞到 {endX - _switch.GlobalPosition.X:F0} 像素外）：不结算、不触发，门还关着");
    }

    // ---------- ③ 近战 ----------

    private async Task MeleeChecks()
    {
        bool near = await StandLeftOfSwitch(45f, 75f);
        HitOutcome? outcome = null;
        void OnLanded(HurtBox target, Hit hit, int value)
        {
            if (target == _switch.HurtBox) outcome = (HitOutcome)value;
        }
        Player.AttackHitBox.HitLanded += OnLanded;
        RealKeys.Down(RealKeys.Attack);
        bool attacked = await WaitUntil(() => Player.State is AttackState, 10);
        RealKeys.Up(RealKeys.Attack);
        await WaitUntil(() => PlayerFree, 120);
        Player.AttackHitBox.HitLanded -= OnLanded;
        await Frames(5);
        Check(near && attacked && outcome == HitOutcome.Ignored && !_switch.Triggered && _activated == 0 && !GameSession.Progress.HasVar(Flag) && GateClosed,
            $"玩家真实按 J 近战砍中开关：结算结果是 Ignored（实际 {outcome?.ToString() ?? "没碰到"}），不触发，门还关着");
    }

    // ---------- ④ 泥肝 ----------

    private async Task LiverChecks()
    {
        ItemCounts.Set("泥肝", 1);
        bool ready = await StandLeftOfSwitch(150f, 250f);
        ThrowState throwState = Player.Machine.Get<ThrowState>();
        await WaitUntil(() => throwState.IntervalReady, 60);
        Projectile p = null;
        void OnThrown(Projectile thrown) => p = thrown;
        throwState.ProjectileThrown += OnThrown;
        RealKeys.Down(RealKeys.Throw);
        await WaitUntil(() => p != null, 5);
        RealKeys.Up(RealKeys.Throw);
        throwState.ProjectileThrown -= OnThrown;
        HurtBox target = null;
        HitOutcome? outcome = null;
        if (p != null && GodotObject.IsInstanceValid(p))
            p.Finished += (reason, position) => { target = p.Target; outcome = p.Outcome; };
        await WaitUntil(() => p == null || !GodotObject.IsInstanceValid(p), 120);
        await Frames(2);
        Check(ready && p != null && target == _switch.HurtBox && outcome == HitOutcome.Damaged && ItemCounts.Get("泥肝") == 0,
            $"玩家真实按 R 扔泥肝：打中开关（结果 {outcome?.ToString() ?? "没打中"}），泥肝用掉");
        Check(GameSession.Progress.Flag(Flag) && GameSession.Progress.GetVar(Flag).VariantType == Variant.Type.Bool && _switch.Triggered && _switch.Activations == 1 && _activated == 1,
            "泥肝打中开关：进度旗标 test_liver_switch 写成 true，开关变成已触发，Activated 只发一次");
        Check(GateOpen, "泥肝打中开关：关联的门立即打开（去掉碰撞、隐藏）");
        bool passed = await HoldKeyUntil(RealKeys.Right, () => Player.GlobalPosition.X > _gate.GlobalPosition.X + 60f, 300);
        Check(passed, "门打开后玩家真实往右走，能穿过原来门的位置");
        await Settle();
    }

    // ---------- ⑤ 存档 / 读档 ----------

    private async Task SaveChecks()
    {
        await Teleport("S1_04", SpawnPoint.SaveId);
        await Settle();
        await TapKey(RealKeys.Interact);
        await Frames(3);
        Check(FileAccess.FileExists(SavePath) && Saves.CheckpointRoom == "S1_04", "在 S1_04 存档点真实按 E 存档");

        bool finished = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        Check(finished && Starts[^1].success && GameSession.Progress.Flag(Flag), "读档：进度里 test_liver_switch 仍是 true");
        await Settle();
        await Place();
        Check(_switch.Triggered && _switch.Activations == 0 && GateOpen,
            "读档后重新进房间（重新摆放开关和门）：开关一开始就是已触发，门一开始就是开的");
    }

    // ---------- ⑥ 重开一局 ----------

    private async Task RestartChecks()
    {
        Node oldScene = MainGame;
        int before = Starts.Count;
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        bool restarted = await WaitUntil(() => Starts.Count > before && GetTree().CurrentScene != oldScene, 600);
        BindMainGame();
        BindNarrative();
        await WaitSettled();
        await Settle();
        await Place();
        Check(restarted && Starts[^1].success && !GameSession.Progress.HasVar(Flag) && !_switch.Triggered && GateClosed,
            "重开一局：旗标清空，开关回到未触发，门关着");
    }
}
