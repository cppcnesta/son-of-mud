using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Enemy.States;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.Save;
using SonOfMud.Tools;
using GDictionary = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;

/// <summary>
/// PR B（计划 v6）验收：
/// ① 两段连招：水第 1 下后摇里按攻击接第 2 下（14 伤害、0.06/0.12/0.24、播 attack_down），窗口外按从第 1 下开始；
///    泥收势前 0.35 秒里接第 2 下（30 伤害），之后收势先停在 attack_down 最后一帧；空中下劈不算连招。
/// ② 手感：A 前摇里能转身、判定后锁住；B 水攻击中 60% 移速不改朝向、泥站定；C 攻击缓冲 0.25 秒（格挡、闪避仍 0.15）；
///    D 泥收势 1.2 秒后能重新出第 1 下，收势第 0.40 秒按的不出（反例），到点前 0.2 秒按的到点就出。
/// ③ G5：敌人出招中被水打只扣血不进反应、招照常出完；被泥打进硬直；不在出招时水也能打出反应。
/// ④ 下砸落地：收势前 0.25 秒播 slam_land，之后照原来的收势；总时长不变。
/// ⑤ 开局泥、打赢守坑人学会水（提示一次）、读档迁移（两种旧存档、不弹提示）、读档后从泥开始、试玩补变量后核对。
/// </summary>
public partial class ComboFeelTest : LabSuiteBase
{
    private const string CombatLab = "res://assets/scenes/tests/combat_lab.tscn";
    private const string EnemyLab = "res://assets/scenes/tests/enemy_lab.tscn";
    private static readonly float FloorY = 600f - PlayerBody.HalfH;
    private const int Water = FormController.Water;
    private const int Mud = FormController.Mud;

    protected override string ReportName => "combo_feel_results";

    private TrainingDummy _dummy;

    protected override async Task RunChecks()
    {
        DataChecks();
        await LoadLab(CombatLab);
        _dummy = Lab.GetNode<TrainingDummy>("DummyA");
        await WaterComboChecks();
        await MudComboChecks();
        await TurnChecks();
        await DriftChecks();
        await BufferChecks();
        await ReattackChecks();
        await MashMetrics();
        await SlamLandChecks();
        await UnlockChecks();
        await LoadLab(EnemyLab);
        await InterruptChecks();
    }

    // ---------- 数据 ----------

    private void DataChecks()
    {
        var water = GD.Load<FormConfig>("res://assets/data/player_v2/water.tres");
        var mud = GD.Load<FormConfig>("res://assets/data/player_v2/mud.tres");
        var rules = GD.Load<PlayerRules>("res://assets/data/player_v2/rules.tres");
        var table = GD.Load<AbilityTable>("res://assets/data/core/ability_table.tres");
        Check(water.Attack2Spec?.Damage == 14f && Mathf.IsEqualApprox(water.Attack2Windup, 0.06f) && Mathf.IsEqualApprox(water.Attack2Active, 0.12f)
              && Mathf.IsEqualApprox(water.Attack2Recovery, 0.24f) && Mathf.IsEqualApprox(water.ComboWindow, 0.16f),
            "水第 2 下：伤害 14，前摇 / 判定 / 后摇 0.06 / 0.12 / 0.24，接招窗口 = 第 1 下整段后摇 0.16");
        Check(mud.Attack2Spec?.Damage == 30f && Mathf.IsEqualApprox(mud.Attack2Windup, 0.12f) && Mathf.IsEqualApprox(mud.Attack2Active, 0.14f)
              && Mathf.IsEqualApprox(mud.ComboWindow, 0.35f),
            "泥第 2 下：伤害 30，前摇 / 判定 0.12 / 0.14，收势前 0.35 秒接");
        Check(Mathf.IsEqualApprox(water.Attack2Spec.Feel.HitStop, 0.08f) && Mathf.IsEqualApprox(water.Attack2Spec.Feel.ShakeStrength, 3f)
              && Mathf.IsEqualApprox(mud.Attack2Spec.Feel.HitStop, 0.11f) && Mathf.IsEqualApprox(mud.Attack2Spec.Feel.ShakeStrength, 6f),
            "第 2 下打击感加一档：水顿帧 0.08、震屏 3；泥顿帧 0.11、震屏 6");
        Check(Mathf.IsEqualApprox(water.AttackMoveMultiplier, 0.6f) && mud.AttackMoveMultiplier == 0f
              && Mathf.IsEqualApprox(mud.ReattackAfter, 1.2f) && water.ReattackAfter == 0f,
            "攻击中移速：水 0.6、泥 0；泥收势 1.2 秒后能重新出刀");
        Check(Mathf.IsEqualApprox(rules.AttackBuffer, 0.25f) && Mathf.IsEqualApprox(rules.ActionBuffer, 0.15f)
              && Mathf.IsEqualApprox(rules.SlamLandAnimSeconds, 0.25f),
            "攻击缓冲 0.25 秒，格挡 / 闪避缓冲仍 0.15 秒；下砸落地动画 0.25 秒");
        Check(!water.AttackSpec.InterruptsActions && !water.Attack2Spec.InterruptsActions && mud.AttackSpec.InterruptsActions
              && mud.Attack2Spec.InterruptsActions && rules.SlamSpec.InterruptsActions,
            "G5：水的两下不能打断敌人出招，泥的两下和下砸能");
        Check(rules.InitialForm == Mud && !table.Initial.Contains(FormUnlockRule.Ability) && table.Initial.Contains("block") && table.Initial.Contains("wall_climb"),
            "开局泥形态；初始能力没有切换形态（格挡、攀墙照旧）");
    }

    // ---------- ① 连招 ----------

    private async Task<int> FramesUntil(System.Func<bool> condition, int max = 240)
    {
        for (int i = 0; i < max; i++)
        {
            if (condition()) return i;
            await Frames(1);
        }
        return -1;
    }

    /// <summary>等到条件成立，返回经过的物理帧数（打中时的顿帧会停住物理，所以计时按物理帧算）。</summary>
    private async Task<int> PhysicsFramesUntil(System.Func<bool> condition, int max = 240)
    {
        ulong start = Engine.GetPhysicsFrames();
        for (int i = 0; i < max && !condition(); i++) await Frames(1);
        return condition() ? (int)(Engine.GetPhysicsFrames() - start) : -1;
    }

    /// <summary>真实按跳，等到离地往上升。</summary>
    private async Task JumpUp()
    {
        await Tap(PlayerActions.Jump);
        await FramesUntil(() => !Player.IsOnFloor() && Player.Velocity.Y < 0f, 40);
        await Frames(2);
    }

    /// <summary>离靶子很远（不会打中、没有顿帧），用来量时间。</summary>
    private static readonly Vector2 Far = new(2400, FloorY);

    private AttackState Attack => Player.State as AttackState;

    private async Task WaterComboChecks()
    {
        await Reset(new Vector2(400, FloorY), Water);
        int before = _dummy.DamageEvents;
        await Tap(PlayerActions.Attack);
        // 第 1 下判定里就按（走缓冲），后摇一开始就接第 2 下
        await FramesUntil(() => Attack is { Current: AttackState.Phase.Active });
        await Tap(PlayerActions.Attack);
        int waited = await FramesUntil(() => Attack is { ComboStep: 2 }, 30);
        Check(waited >= 0 && Attack.AnimName == "attack_down" && Player.Animator.CurrentAnimation == "water_attack_down",
            $"水：第 1 下判定里按攻击 → 后摇一开始接第 2 下，播 water_attack_down（{Player.Animator.CurrentAnimation}）");
        await FramesUntil(() => Player.State is not AttackState, 120);
        Check(_dummy.DamageEvents - before == 2 && _dummy.LastDealt == 14, $"两下都打中，第 2 下扣 14（{_dummy.DamageEvents - before} 次，最后 {_dummy.LastDealt}）");
        await Tap(PlayerActions.Attack);
        await Frames(1);
        Check(Attack is { ComboStep: 1 }, "第 2 下打完，再按从第 1 下开始");
        await FramesUntil(() => Player.State is not AttackState, 60);
        // 时长在远处量（打不到靶子，没有顿帧）
        await Reset(Far, Water);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Attack is { Current: AttackState.Phase.Active });
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Attack is { ComboStep: 2 }, 30);
        int frames = await FramesUntil(() => Player.State is not AttackState, 120);
        Check(Mathf.Abs(frames - FramesFor(0.42f)) <= 2, $"水第 2 下整段 0.06 + 0.12 + 0.24 = 0.42 秒（{frames} 帧）");
        // 窗口外：第 1 下整段收完再按，是第 1 下
        await Frames(10);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Player.State is not AttackState, 60);
        await Frames(2);
        await Tap(PlayerActions.Attack);
        await Frames(1);
        Check(Attack is { ComboStep: 1 }, "第 1 下后摇结束以后再按：从第 1 下重新开始，不接第 2 下");
        await FramesUntil(() => Player.State is not AttackState, 60);
        // 空中下劈不算连招
        await Reset(Far, Water);
        await JumpUp();
        Press(PlayerActions.Down);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Attack is { Current: AttackState.Phase.Recovery }, 30);
        await Tap(PlayerActions.Attack);
        await Frames(2);
        Release(PlayerActions.Down);
        Check(Attack is not { ComboStep: 2 }, "空中下劈的后摇里按攻击不接第 2 下");
        await Frames(60);
    }

    private async Task MudComboChecks()
    {
        await Reset(new Vector2(400, FloorY), Mud);
        int before = _dummy.DamageEvents;
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Player.State is AttackRecoveryState, 60);
        await Frames(FramesFor(0.2f));
        await Tap(PlayerActions.Attack);
        int waited = await FramesUntil(() => Attack is { ComboStep: 2 }, 10);
        Check(waited >= 0 && Player.Animator.CurrentAnimation == "mud_attack_down", $"泥：收势第 0.2 秒按攻击 → 接第 2 下，播 mud_attack_down（{Player.Animator.CurrentAnimation}）");
        await FramesUntil(() => Player.State is AttackRecoveryState, 60);
        await Frames(2);
        var rec = Player.State as AttackRecoveryState;
        Check(rec is { EndedStep: 2 } && Player.Animator.RecoverFallbackActive && Player.Animator.CurrentAnimation == "mud_attack_down"
              && Player.Animator.Sprite.Frame == Player.Animator.Sprite.SpriteFrames.GetFrameCount("mud_attack_down") - 1,
            "泥第 2 下之后的收势：先停在 attack_down 最后一帧");
        Check(_dummy.DamageEvents - before == 2 && _dummy.LastDealt == 30, $"两下都打中，第 2 下扣 30（最后 {_dummy.LastDealt}）");
        await Frames(FramesFor(1.6f));

        // 窗口外（0.35 秒以后）按：不接第 2 下
        await Reset(new Vector2(400, FloorY), Mud);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Player.State is AttackRecoveryState, 60);
        await Frames(FramesFor(0.45f));
        await Tap(PlayerActions.Attack);
        await Frames(2);
        Check(Player.State is AttackRecoveryState, "泥收势第 0.45 秒（窗口外）按攻击：不接第 2 下");
        await Frames(FramesFor(1.5f));
    }

    // ---------- ② A：前摇里转身 ----------

    private async Task TurnChecks()
    {
        foreach (int form in new[] { Water, Mud })
        {
            string f = form == Water ? "水" : "泥";
            await Reset(new Vector2(400, FloorY), form);
            Player.Facing = -1f;
            await Frames(2);
            Press(PlayerActions.Attack);
            await Frames(1);
            Release(PlayerActions.Attack);
            await Frames(1);
            Press(PlayerActions.Right);   // J 先到，D 晚 2 帧
            await FramesUntil(() => Attack is { Current: AttackState.Phase.Active }, 30);
            float boxX = Player.AttackHitBox.GetNode<CollisionShape2D>("CollisionShape2D").Position.X;
            Check(Player.Facing > 0f && boxX > 0f, $"{f}：先按 J、2 帧后按右 → 前摇里转过来，判定框在右边（朝向 {Player.Facing}，框 x {boxX:F0}）");
            Release(PlayerActions.Right);
            Press(PlayerActions.Left);
            await Frames(3);
            Check(Player.Facing > 0f, $"{f}：进入判定后按左，朝向不变");
            Release(PlayerActions.Left);
            await Frames(FramesFor(1.8f));
        }
    }

    // ---------- ② B：攻击中移动 ----------

    private async Task DriftChecks()
    {
        await Reset(new Vector2(400, FloorY), Water);
        Press(PlayerActions.Right);
        await Frames(10);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Attack is { Current: AttackState.Phase.Active }, 30);
        float vx = Player.Velocity.X;
        Check(Mathf.Abs(vx - 360f * 0.6f) < 1f, $"水攻击中按住右：水平速度 = 360 × 0.6 = 216（{vx:F0}）");
        Release(PlayerActions.Right);
        Press(PlayerActions.Left);
        await Frames(2);
        Check(Player.State is AttackState && Player.Velocity.X < -200f && Player.Facing > 0f, $"水攻击判定里按左：往左走（{Player.Velocity.X:F0}），朝向不变");
        Release(PlayerActions.Left);
        await Frames(40);

        await Reset(new Vector2(400, FloorY), Mud);
        Press(PlayerActions.Right);
        await Frames(10);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Attack is { Current: AttackState.Phase.Active }, 30);
        Check(Player.State is AttackState && Player.Velocity.X == 0f, $"泥攻击前摇、判定中按住右：站定（{Player.Velocity.X:F0}）");
        Release(PlayerActions.Right);
        await Frames(FramesFor(1.6f));

        // 空中同样
        await Reset(Far, Water);
        await JumpUp();
        Press(PlayerActions.Right);
        await Tap(PlayerActions.Attack);
        await Frames(3);
        Check(Player.State is AttackState && !Player.IsOnFloor() && Mathf.Abs(Player.Velocity.X - 216f) < 1f, $"水空中攻击按住右：同样 216（{Player.Velocity.X:F0}）");
        Release(PlayerActions.Right);
        await Frames(60);
    }

    // ---------- ② C：攻击缓冲 ----------

    private async Task BufferChecks()
    {
        // 水第 2 下没有连招，攻击间隔 = 0.42 秒；在结束前不同时刻按一次，看结束时出不出第 1 下
        foreach ((float before, bool expect) in new[] { (0.2f, true), (0.3f, false) })
        {
            await Reset(Far, Water);
            await Tap(PlayerActions.Attack);
            await FramesUntil(() => Attack is { Current: AttackState.Phase.Recovery }, 30);
            await Tap(PlayerActions.Attack);
            await FramesUntil(() => Attack is { ComboStep: 2 }, 10);
            // 第 2 下从现在起 0.42 秒结束；等到结束前 before 秒按
            await Frames(FramesFor(0.42f - before) - 2);
            await Tap(PlayerActions.Attack);
            int frames = await FramesUntil(() => Player.State is not AttackState, 60);
            await Frames(1);
            bool attacked = Attack is { ComboStep: 1 };
            Check(attacked == expect, $"能出刀前 {before:F2} 秒按的攻击：{(expect ? "出" : "不出")}（实际 {(attacked ? "出了" : "没出")}）");
            await Frames(40);
        }
    }

    // ---------- ② D：泥收势后段重新出刀 ----------

    private async Task ReattackChecks()
    {
        // 反例：收势第 0.40 秒按，到 1.2 秒时已过 0.8 秒，超过 0.25 秒缓冲，不出刀
        await Reset(new Vector2(400, FloorY), Mud);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Player.State is AttackRecoveryState, 60);
        await Frames(FramesFor(0.40f) - 2);
        await Tap(PlayerActions.Attack);
        int ended = await FramesUntil(() => Player.State is not AttackRecoveryState, FramesFor(1.6f));
        Check(Player.State is not AttackState && ended >= FramesFor(1.0f), $"泥收势第 0.40 秒按攻击：到 1.2 秒不出刀（收势照常走完，{ended} 帧后结束）");
        await Frames(10);

        // 到点前 0.2 秒按：到 1.2 秒出第 1 下
        await Reset(new Vector2(400, FloorY), Mud);
        await Tap(PlayerActions.Attack);
        await FramesUntil(() => Player.State is AttackRecoveryState, 60);
        await Frames(FramesFor(1.0f) - 2);
        await Tap(PlayerActions.Attack);
        int f = await FramesUntil(() => Player.State is AttackState, 40);
        float at = (FramesFor(1.0f) + f) / 60f;
        Check(Attack is { ComboStep: 1 } && Mathf.Abs(at - 1.2f) <= 0.05f, $"泥收势第 1.0 秒按攻击：第 {at:F2} 秒重新出第 1 下（应 1.2）");
        await Frames(FramesFor(1.8f));
    }

    private async Task MashMetrics()
    {
        foreach (int form in new[] { Water, Mud })
        {
            await Reset(new Vector2(400, FloorY), form);
            int attacks = 0, presses = 0;
            string last = "";
            for (int i = 0; i < 180; i++)
            {
                if (i % 6 == 0) { Press(PlayerActions.Attack); presses++; }
                else if (i % 6 == 1) Release(PlayerActions.Attack);
                await Frames(1);
                if (Attack is { } a && a.AttackId != last) { last = a.AttackId; attacks++; }
            }
            Release(PlayerActions.Attack);
            string f = form == Water ? "水" : "泥";
            Metric($"mash_{(form == Water ? "water" : "mud")}_attacks_3s", attacks);
            GD.Print($"每 6 帧点一次攻击，3 秒（{presses} 次）：{f}出了 {attacks} 刀（含连招第 2 下；首尾不完整，只作参考）");
            Check(attacks > 0, $"{f}：连点 3 秒能出刀（{attacks} 刀，只作参考）");
            await Frames(FramesFor(1.8f));
        }
    }

    // ---------- ④ 下砸落地 ----------

    private async Task SlamLandChecks()
    {
        GameSession.Progress.Unlock("heavy_slam");
        foreach (int form in new[] { Water, Mud })
        {
            string f = form == Water ? "水" : "泥";
            await Reset(new Vector2(700, FloorY - 200), form);
            Press(PlayerActions.Down);
            await Tap(PlayerActions.Attack);
            Release(PlayerActions.Down);
            await FramesUntil(() => Player.State is AttackRecoveryState, 120);
            await Frames(1);
            var rec = Player.State as AttackRecoveryState;
            string anim = Player.Animator.CurrentAnimation;
            Check(rec is { FromSlam: true } && Player.Animator.SlamLandActive && anim.EndsWith("slam_land"),
                $"{f}下砸落地：收势开头播落地动画（{anim}）");
            int frames = 0;
            while (Player.Animator.SlamLandActive && frames < 60) { await Frames(1); frames++; }
            Check(Mathf.Abs(frames - FramesFor(0.25f)) <= 2, $"{f}：落地动画播 0.25 秒（{frames} 帧）");
            if (form == Mud)
            {
                await Frames(2);
                Check(Player.State is AttackRecoveryState && !Player.Animator.CurrentAnimation.EndsWith("slam_land"),
                    $"泥：之后照原来的收势（{Player.Animator.CurrentAnimation}）");
                int rest = await FramesUntil(() => Player.State is not AttackRecoveryState, 120);
                float total = (FramesFor(0.25f) + 2 + rest) / 60f;
                Check(Mathf.Abs(total - 1.5f) <= 0.05f, $"泥收势总时长不变：1.5 秒（{total:F2}）");
            }
            else
            {
                await Frames(2);
                Check(Player.State is not AttackRecoveryState, "水：0.25 秒站定收势整段是落地动画，之后收势结束");
            }
            await Frames(20);
        }
        GameSession.Progress.Revoke("heavy_slam");
    }

    // ---------- ⑤ 开局泥、学会水、读档迁移 ----------

    private async Task UnlockChecks()
    {
        var messages = new List<string>();
        void OnMessage(string text) => messages.Add(text);
        Events.Instance.MessageRequested += OnMessage;
        var table = GD.Load<AbilityTable>("res://assets/data/core/ability_table.tres");
        string dir = "user://combo_feel_saves";
        var save = new SaveService { SaveDirectory = dir, InitialAbilities = table };
        AddChild(save);
        await Frames(1);
        try
        {
            // 新游戏
            save.StartNewGame();
            await Frames(2);
            Check(!GameSession.Progress.HasAbility(FormUnlockRule.Ability) && Player.Forms.Form == Mud, "新游戏：没有切换形态，玩家是泥");
            await Reset(new Vector2(400, FloorY), Mud);
            Player.Forms.SetForm(Mud);
            await Tap(PlayerActions.SwitchForm);
            Check(Player.Forms.Form == Mud, "按 Q 切不了");
            // 打赢守坑人
            GameSession.Progress.SetVar(FormUnlockRule.Flag, true);
            await Frames(1);
            Check(GameSession.Progress.HasAbility(FormUnlockRule.Ability) && messages.Count == 1 && messages[0] == FormUnlockRule.UnlockText,
                $"boss_keeper 变成 true：学会切换形态，提示条一次「{FormUnlockRule.UnlockText}」（{messages.Count} 条）");
            await Frames(12);
            await Tap(PlayerActions.SwitchForm);
            Check(Player.Forms.Form == Water, "学会以后按 Q 能切到水");
            GameSession.Progress.SetVar(FormUnlockRule.Flag, false);
            GameSession.Progress.SetVar(FormUnlockRule.Flag, true);
            Check(messages.Count == 1, "已经学会了：再写一次 boss_keeper 不重复提示");

            // 旧存档 1：没打赢但有切换能力 → 拿掉、是泥、不弹提示
            messages.Clear();
            WriteSave(dir, new[] { "block", "switch_form", "wall_climb" }, keeper: false);
            Player.Forms.SetForm(Water);
            save.LoadFromDisk(0);
            await Frames(2);
            Check(!GameSession.Progress.HasAbility(FormUnlockRule.Ability) && Player.Forms.Form == Mud && messages.Count == 0,
                $"旧存档（没打赢守坑人、有切换能力）：读档后拿掉、是泥、不弹提示（提示 {messages.Count} 条）");
            // 旧存档 2：打赢了但没有切换能力 → 补上、不弹提示、照样从泥开始
            WriteSave(dir, new[] { "block", "wall_climb" }, keeper: true);
            Player.Forms.SetForm(Water);
            save.LoadFromDisk(0);
            await Frames(2);
            Check(GameSession.Progress.HasAbility(FormUnlockRule.Ability) && Player.Forms.Form == Mud && messages.Count == 0,
                $"旧存档（打赢了、没有切换能力）：读档后补上、从泥开始、不弹提示（提示 {messages.Count} 条）");
            // 试玩：补的变量里有 boss_keeper
            GameSession.Trial = new TrialRequest { Room = "S1_06", Spawn = "Default", Fill = true, Flags = new List<string> { "boss_keeper" } };
            save.StartNewGame();
            await Frames(2);
            Check(GameSession.Progress.HasAbility(FormUnlockRule.Ability) && messages.Count == 0, "试玩补了 boss_keeper：能切换形态，不弹提示");
            GameSession.Trial = null;
            // 规则本身
            var p = new ProgressStore();
            p.Unlock(FormUnlockRule.Ability);
            Check(FormUnlockRule.Reconcile(p) == 1 && !p.HasAbility(FormUnlockRule.Ability), "核对：没有 boss_keeper 时拿掉切换形态");
            p.SetVar(FormUnlockRule.Flag, true);
            Check(FormUnlockRule.Reconcile(p) == 1 && p.HasAbility(FormUnlockRule.Ability) && FormUnlockRule.Reconcile(p) == 0, "核对：有 boss_keeper 时补上，再核对不变");
        }
        finally
        {
            GameSession.Trial = null;
            Events.Instance.MessageRequested -= OnMessage;
            save.QueueFree();
            GameSession.Reset(table);
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePaths.SlotPath(dir, 0)));
        }
        await Frames(2);
    }

    private static void WriteSave(string dir, string[] abilities, bool keeper)
    {
        var vars = new GDictionary();
        if (keeper) vars["boss_keeper"] = true;
        var list = new GArray();
        foreach (string a in abilities) list.Add(a);
        var data = new SaveData
        {
            Progress = new GDictionary { ["version"] = ProgressStore.SaveVersion, ["abilities"] = list, ["vars"] = vars },
            Room = "S1_04",
            SavePoint = "Save",
        };
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        data.WriteAtomic(SavePaths.SlotPath(dir, 0), out _);
    }

    // ---------- ③ G5 ----------

    private async Task InterruptChecks()
    {
        var mason = Lab.GetNode<EnemyBody>("Mason");
        var waterSpec = GD.Load<HitSpec>("res://assets/data/player_v2/water_attack.tres");
        var water2 = GD.Load<HitSpec>("res://assets/data/player_v2/water_attack_2.tres");
        var mudSpec = GD.Load<HitSpec>("res://assets/data/player_v2/mud_attack.tres");
        var mud2 = GD.Load<HitSpec>("res://assets/data/player_v2/mud_attack_2.tres");
        int id = 0;
        HitOutcome HitWith(HitSpec spec) => mason.HurtBox.Receive(Hit.From(spec, $"g5_{id++}", Player.GlobalPosition, 1f, Player));

        // 让泥匠来打站着的玩家
        await Reset(new Vector2(800, FloorY), Mud);
        foreach ((HitSpec spec, string name) in new[] { (waterSpec, "水第 1 下"), (water2, "水第 2 下") })
        {
            mason.Stats.Refill();
            int waited = await FramesUntil(() => mason.State is AttackWindupState, 600);
            int reactions = mason.Reactions, unint = mason.UninterruptedHits, hp = mason.Stats.Health;
            HitWith(spec);
            await Frames(1);
            Check(waited >= 0 && mason.Stats.Health < hp && mason.Reactions == reactions && mason.UninterruptedHits == unint + 1 && mason.IsActing,
                $"G5：泥匠前摇中被{name}打中：扣血，不进受击反应，招照常出（状态 {mason.State?.GetType().Name}）");
            int toActive = await FramesUntil(() => mason.State is AttackActiveState, 120);
            Check(toActive >= 0, "前摇被水打过以后照样出手");
            await FramesUntil(() => !mason.IsActing, 240);
            Player.Stats.Refill();
        }
        foreach ((HitSpec spec, string name) in new[] { (mudSpec, "泥第 1 下"), (mud2, "泥第 2 下") })
        {
            mason.Stats.Refill();
            await FramesUntil(() => mason.State is AttackWindupState, 600);
            HitWith(spec);
            await Frames(1);
            Check(mason.State is SonOfMud.Enemy.States.HurtState, $"G5：泥匠前摇中被{name}打中：进受击硬直，招被打断（{mason.State?.GetType().Name}）");
            await Frames(60);
            Player.Stats.Refill();
        }
        // 不在出招时被水打：照常反应
        await Reset(new Vector2(200, FloorY), Mud);
        mason.Stats.Refill();
        await FramesUntil(() => !mason.IsActing, 240);
        int r0 = mason.Reactions;
        HitWith(waterSpec);
        await Frames(1);
        Check(mason.Reactions == r0 + 1, $"不在出招时被水打：照常进受击反应（{mason.State?.GetType().Name}）");
    }
}
