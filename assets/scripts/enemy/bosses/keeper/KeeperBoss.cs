using Godot;
using SonOfMud.Combat;
using SonOfMud.Enemy.Bosses.Keeper.States;

namespace SonOfMud.Enemy.Bosses.Keeper;

/// <summary>
/// 守坑人：蝎（boss.md）。状态一个一个类，见 states/：
/// Dormant → Approach ⇄ 横扫（SweepWindup → SweepStrike → SweepRecover）/ 蝎尾（TailAim → TailStrike → TailRecover 或 Staggered）；
/// 任何状态下血量归零 → Transform（1 秒闪白，结束时写 boss_keeper 并发 BossDefeated）→ Gone。
/// 两个技能用「有阻回」的 SkillCooldown：开战时横扫 5/5、蝎尾 0/20，只在开战后、变身前计时。
/// 子节点：Stats、HurtBox、HitBox（横扫）、TailHitBox（蝎尾竖带）、StaggerWindow、StateMachine。
/// 老人 NPC 由 Narrative 的 NpcTalk（VisibleWhen boss_keeper）负责，这里不引用。
/// </summary>
[GlobalClass]
public partial class KeeperBoss : BossBody
{
    public static readonly StringName SweepVulnerableSource = "sweep_recover";
    public static readonly StringName TailAimSource = "tail_aim";

    /// <summary>竞技场范围（父节点坐标）。玩家进入这里才开战。</summary>
    [Export] public Rect2 Arena { get; set; } = new(0f, -1000f, 4000f, 2000f);

    public KeeperData Keeper => Data as KeeperData;
    public HitBox SweepHitBox => AttackHitBox;
    public HitBox TailHitBox { get; private set; }
    public StaggerWindow Stagger { get; private set; }

    public SkillCooldown Sweep { get; private set; }
    public SkillCooldown Tail { get; private set; }
    /// <summary>开战后、变身前：冷却在计时。</summary>
    public bool FightActive { get; private set; }

    /// <summary>瞄准中跟随玩家的 X（预警显示用）。</summary>
    public float AimX { get; set; }
    /// <summary>瞄准结束时锁定的玩家 X。</summary>
    public float LockedTailX { get; set; }
    public bool Aiming { get; set; }
    public bool TailLocked { get; set; }
    /// <summary>变身闪白剩余时间（&gt;0 表示正在闪白）。</summary>
    public float FlashRemaining { get; set; }
    public bool Transforming { get; set; }
    /// <summary>横扫命中（含被格挡）让蝎尾冷却真正减少的次数。</summary>
    public int TailReductions { get; set; }

    protected override bool ResolvingDefeat => Transforming;

    public override void _Ready()
    {
        KeeperData data = Data as KeeperData ?? new KeeperData();
        Data = data;
        foreach (Node child in GetChildren())
        {
            if (child is HitBox box && child.Name == "TailHitBox") TailHitBox = box;
            if (child is StaggerWindow window) Stagger = window;
        }
        if (Stagger != null && data.Stagger != null) Stagger.Config = data.Stagger;
        Sweep = new SkillCooldown(data.SweepCooldown, true);
        Tail = new SkillCooldown(data.TailCooldown, false);
        base._Ready();
        if (TailHitBox != null) RegisterAttackHitBox(TailHitBox, flip: false);
        if (Stagger != null) Stagger.Stats ??= Stats;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (FightActive)
        {
            Sweep.Tick((float)delta);
            Tail.Tick((float)delta);
        }
        base._PhysicsProcess(delta);
    }

    /// <summary>玩家站在竞技场里（全局坐标判断）。</summary>
    public bool InArena(Vector2 globalPoint)
    {
        Vector2 origin = GetParent() is Node2D parent ? parent.GlobalPosition : Vector2.Zero;
        return new Rect2(Arena.Position + origin, Arena.Size).HasPoint(globalPoint);
    }

    public Vector2 ArenaCenter
    {
        get
        {
            Vector2 origin = GetParent() is Node2D parent ? parent.GlobalPosition : Vector2.Zero;
            return Arena.GetCenter() + origin;
        }
    }

    /// <summary>开战：横扫 5/5、蝎尾 0/20，发 BossEncounterStarted。</summary>
    public void BeginFight()
    {
        Sweep = new SkillCooldown(Keeper.SweepCooldown, true);
        Tail = new SkillCooldown(Keeper.TailCooldown, false);
        FightActive = true;
        StartEncounter();
    }

    /// <summary>变身第 ① 步：关掉所有判定和受击、清掉计时和易伤。</summary>
    public void ShutDownCombat()
    {
        FightActive = false;
        CloseAttacks();
        if (HurtBox != null)
        {
            HurtBox.Receiving = false;
            HurtBox.SetInvulnerable(TailAimSource, false);
        }
        Stagger?.End();
        Stats.SetIncomingMultiplier(SweepVulnerableSource, 1f);
        Aiming = false;
        TailLocked = false;
        Stop();
    }

    public override void CloseAttacks()
    {
        SweepHitBox?.Deactivate();
        TailHitBox?.Deactivate();
    }

    /// <summary>把蝎尾竖带放到锁定的 X 处，宽 TailWidth、高 TailHeight，竖直方向以竞技场中心为准。</summary>
    public void PlaceTailBand()
    {
        if (TailHitBox == null) return;
        TailHitBox.GlobalPosition = new Vector2(LockedTailX, ArenaCenter.Y);
        foreach (Node child in TailHitBox.GetChildren())
        {
            if (child is not CollisionShape2D shape) continue;
            shape.Position = Vector2.Zero;
            shape.Shape = new RectangleShape2D { Size = new Vector2(Keeper.TailWidth, Keeper.TailHeight) };
        }
    }

    /// <summary>受伤不打断守坑人的动作（Boss 没有受击硬直）。</summary>
    protected override void OnDamaged(Hit hit, int dealt) { }

    /// <summary>任何状态下血量归零 → 变身（优先级最高）。</summary>
    protected override void OnKilled(Hit hit)
    {
        if (Transforming || Finished) return;
        Machine.TransitionTo<TransformState>();
    }

    protected override void EnterDefeated() => Machine.TransitionTo<GoneState>();

    // ---------- 外观 ----------

    /// <summary>僵直变黄、横扫收招偏橙、横扫前摇偏黄。</summary>
    public override Color StateTint(Color baseColor)
    {
        if (State is StaggeredState) return new Color("ffd166");
        if (State is SweepRecoverState) return baseColor.Lerp(new Color(1f, 0.55f, 0.2f), 0.6f);
        if (State is SweepWindupState) return baseColor.Lerp(new Color(1f, 0.85f, 0.3f), 0.4f);
        return baseColor;
    }

    /// <summary>变身闪白：每 0.2 秒里有 0.1 秒是白的。</summary>
    public override float FlashAmount => FlashRemaining > 0f && Mathf.PosMod(FlashRemaining, 0.2f) > 0.1f ? 1f : 0f;

    // ---------- 技术占位外观（当前动作没有美术图时） ----------

    public override void _Draw()
    {
        if (!Visible || !DrawsPlaceholder) return;
        Vector2 size = BodySize;
        Color body = StateTint(Data?.Tint ?? new Color("5a4632"));
        if (FlashAmount > 0f) body = Colors.White;
        DrawRect(new Rect2(-size.X * 0.5f, -size.Y, size.X, size.Y), body);
        // 蝎尾（占位）
        DrawRect(new Rect2(-Facing * size.X * 0.5f - 8f, -size.Y - 40f, 16f, 60f), body.Darkened(0.3f));
        if (Aiming || TailLocked)
        {
            float x = (TailLocked ? LockedTailX : AimX) - GlobalPosition.X;
            float top = ArenaCenter.Y - GlobalPosition.Y - Keeper.TailHeight * 0.5f;
            DrawRect(new Rect2(x - Keeper.TailWidth * 0.5f, top, Keeper.TailWidth, Keeper.TailHeight),
                TailLocked ? new Color(1f, 0.2f, 0.2f, 0.45f) : new Color(1f, 0.3f, 0.3f, 0.18f));
        }
        if (SweepHitBox is { Active: true })
            foreach (Node child in SweepHitBox.GetChildren())
                if (child is CollisionShape2D shape && shape.Shape is RectangleShape2D rect)
                    DrawRect(new Rect2(shape.Position - rect.Size * 0.5f, rect.Size), new Color(1f, 0.3f, 0.2f, 0.35f));
    }
}
