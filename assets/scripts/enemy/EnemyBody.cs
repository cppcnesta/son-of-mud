using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy.States;

namespace SonOfMud.Enemy;

/// <summary>
/// 敌人主体（普通敌人直接用它，Boss 从 BossBody 派生）。子节点：Stats、HurtBox、HitBox（攻击判定，被动型可以没有）、
/// StateMachine（状态见 states/）。只依赖 Core 和 Combat。
///
/// - 找玩家：_EnterTree 里先读 PlayerRegistry.Current，再订阅 Changed，_ExitTree 取消订阅。
///   房间比玩家晚加载也能拿到玩家；只当 Node2D 用，不认识玩家类。
/// - 每个物理帧：当前状态 PhysicsUpdate（设速度、切状态）→ MoveAndSlide。
/// - 受击：HurtBox 结算后，没死 → 按行为组合反应（逃跑 / 起身反击 / 受击硬直）；
///   死了 → 关掉判定、发 EnemyKilled 恰好一次，按掉落表（摆放点可以覆盖）发 ItemDropped，进入 Dead。
/// - 攻击结果：HitBox.HitLanded 转给当前状态（只用来分支，收招一律按状态自己的计时器）。
/// - 随机数分两路：反应（逃跑概率）和掉落，各自可以用固定种子（测试用 SetSeeds）。
/// 没有接触伤害（Q8）：身体只在敌人层，玩家身体不和它碰撞。
/// </summary>
[GlobalClass]
public partial class EnemyBody : CharacterBody2D
{
    public static readonly StringName Group = "enemies";

    [Export] public EnemyData Data { get; set; }
    /// <summary>初始朝向：1 朝右，-1 朝左。</summary>
    [Export] public float InitialFacing { get; set; } = -1f;
    /// <summary>随机种子；0 表示随机。</summary>
    [Export] public ulong Seed { get; set; }
    /// <summary>这个摆放点自己的掉落表；不填就用 Data.Drops（占卜之城的祈祷泥人 100% 掉泥瓶）。</summary>
    [Export] public DropTable DropsOverride { get; set; }

    public Stats Stats { get; private set; }
    public HurtBox HurtBox { get; private set; }
    public HitBox AttackHitBox { get; private set; }
    public StateMachine Machine { get; private set; }
    public EnemyState State => Machine?.Current as EnemyState;

    /// <summary>当前玩家（来自 PlayerRegistry）；没有时为 null。</summary>
    public Node2D Target { get; private set; }
    /// <summary>进入场景树那一刻从 PlayerRegistry 读到的玩家（晚加载验收用）。</summary>
    public Node2D TargetAtEnterTree { get; private set; }
    public float Facing { get; set; } = -1f;
    /// <summary>冲锋型在前摇开始时锁定的方向。</summary>
    public float LockedDirection { get; set; } = -1f;
    /// <summary>出生点（巡逻的中心）。</summary>
    public Vector2 Home { get; private set; }
    public bool IsDead { get; private set; }
    /// <summary>发过的 EnemyKilled 次数（应当最多 1）。</summary>
    public int KilledEmits { get; private set; }
    /// <summary>最近一次受击反应："flee"（逃跑）/ "counter"（起身反击）/ "hurt"（受击硬直）/ "none"；还没被打过是空字符串。</summary>
    public string LastReaction { get; private set; } = "";
    public int Reactions { get; private set; }
    /// <summary>出过手的次数（被动型只有被打之后的反击才会加）。</summary>
    public int AttacksStarted { get; private set; }
    /// <summary>当前生效的掉落表：摆放点覆盖优先。</summary>
    public DropTable Drops => DropsOverride ?? Data?.Drops;

    public RandomNumberGenerator ReactionRng { get; } = new();
    public RandomNumberGenerator DropRng { get; } = new();

    public bool HasTarget => Target != null && IsInstanceValid(Target) && Target.IsInsideTree();
    public float DxToTarget => HasTarget ? Target.GlobalPosition.X - GlobalPosition.X : 0f;
    public Vector2 BodySize => Data?.BodySize ?? new Vector2(48f, 64f);

    private int _attackSequence;
    private readonly System.Collections.Generic.List<(CollisionShape2D shape, Vector2 basePosition)> _attackShapes = new();
    private bool _subscribed;

    // ---------- 生命周期 ----------

    public override void _EnterTree()
    {
        // 先读当前玩家，再订阅变化：房间晚于玩家加载时也能直接拿到。
        Target = PlayerRegistry.Current;
        TargetAtEnterTree = Target;
        if (!_subscribed)
        {
            PlayerRegistry.Changed += OnPlayerChanged;
            _subscribed = true;
        }
    }

    public override void _ExitTree()
    {
        if (_subscribed) PlayerRegistry.Changed -= OnPlayerChanged;
        _subscribed = false;
    }

    private void OnPlayerChanged(Node2D player)
    {
        if (!IsInstanceValid(this)) return;
        Target = player;
    }

    public override void _Ready()
    {
        AddToGroup(Group);
        Data ??= new EnemyData();
        foreach (Node child in GetChildren())
        {
            switch (child)
            {
                case Stats s when Stats == null: Stats = s; break;
                case HurtBox h when HurtBox == null: HurtBox = h; break;
                case HitBox hb when AttackHitBox == null && child.Name == "HitBox": AttackHitBox = hb; break;
                case StateMachine m when Machine == null: Machine = m; break;
            }
        }
        if (Stats == null)
        {
            Stats = new Stats { Name = "Stats" };
            AddChild(Stats);
        }
        Stats.MaxHealth = Data.MaxHealth;
        Stats.Refill();

        CollisionLayer = Layers.EnemyBody;
        CollisionMask = Layers.World;
        Facing = InitialFacing >= 0f ? 1f : -1f;
        LockedDirection = Facing;
        Home = GlobalPosition;
        SetSeeds(Seed, Seed == 0 ? 0 : Seed + 7919);

        if (HurtBox != null)
        {
            HurtBox.EntityRoot ??= this;
            HurtBox.Stats ??= Stats;
            HurtBox.CollisionLayer = Layers.EnemyHurt;
            HurtBox.CollisionMask = 0;
            HurtBox.Damaged += OnHurtDamaged;
            HurtBox.Killed += OnHurtKilled;
        }
        if (AttackHitBox != null) RegisterAttackHitBox(AttackHitBox);
        if (Machine != null) Machine.SetPhysicsProcess(false);
    }

    /// <summary>登记一个攻击判定框：设好碰撞层、把结算结果转给当前状态；flip 为 true 时记下形状的基准位置，出手时按朝向左右翻转。</summary>
    protected void RegisterAttackHitBox(HitBox box, bool flip = true)
    {
        box.CollisionLayer = Layers.EnemyHit;
        box.CollisionMask = Layers.PlayerHurt;
        if (flip)
            foreach (Node child in box.GetChildren())
                if (child is CollisionShape2D shape) _attackShapes.Add((shape, shape.Position));
        box.HitLanded += OnHitLanded;
    }

    /// <summary>重设两路随机数的种子；0 表示随机（测试用固定种子）。</summary>
    public void SetSeeds(ulong reactionSeed, ulong dropSeed)
    {
        if (reactionSeed == 0) ReactionRng.Randomize(); else ReactionRng.Seed = reactionSeed;
        if (dropSeed == 0) DropRng.Randomize(); else DropRng.Seed = dropSeed;
    }

    // ---------- 每帧 ----------

    public override void _PhysicsProcess(double delta)
    {
        State?.PhysicsUpdate(delta);
        MoveAndSlide();
    }

    public override void _Process(double delta) => QueueRedraw();

    // ---------- 感知与移动（各状态共用） ----------

    /// <summary>看得见玩家：水平距离在 range（默认视野）以内，高度差不超过视野高度。</summary>
    public bool CanSeeTarget(float range = -1f)
    {
        if (!HasTarget) return false;
        float reach = range < 0f ? Data.VisionRange : range;
        Vector2 d = Target.GlobalPosition - GlobalPosition;
        return Mathf.Abs(d.X) <= reach && Mathf.Abs(d.Y) <= Data.VisionHeight;
    }

    public void FaceTarget()
    {
        float dx = DxToTarget;
        if (Mathf.Abs(dx) > 0.5f) Facing = Mathf.Sign(dx);
    }

    /// <summary>按方向走；前面是悬崖就停下（不会自己走下平台）。</summary>
    public void Walk(float direction, float speed)
    {
        if (direction == 0f)
        {
            Stop();
            return;
        }
        Facing = Mathf.Sign(direction);
        float vx = LedgeAhead(Facing) ? 0f : Facing * speed;
        Velocity = new Vector2(vx, Velocity.Y);
    }

    public void Stop() => Velocity = new Vector2(0f, Velocity.Y);

    public void ApplyGravity(float delta)
    {
        float g = Data?.Gravity ?? 1600f;
        float vy = IsOnFloor() ? g * delta : Mathf.Min(Velocity.Y + g * delta, 1200f);
        Velocity = new Vector2(Velocity.X, vy);
    }

    /// <summary>朝 direction 再走一小步就会踩空（只在站在地面上时判断）。</summary>
    public bool LedgeAhead(float direction)
    {
        if (!IsOnFloor() || !IsInsideTree()) return false;
        float x = GlobalPosition.X + direction * (BodySize.X * 0.5f + 6f);
        var query = PhysicsRayQueryParameters2D.Create(new Vector2(x, GlobalPosition.Y - 4f), new Vector2(x, GlobalPosition.Y + 40f), Layers.World);
        return GetWorld2D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    // ---------- 攻击 ----------

    public string NextAttackId(string kind) => $"enemy_{GetInstanceId()}_{kind}_{++_attackSequence}";

    /// <summary>按当前朝向摆好判定形状，用 spec 打开判定（同一帧立即查询一次）。</summary>
    public void OpenAttack(HitBox box, HitSpec spec, string kind)
    {
        if (box == null || spec == null) return;
        AttacksStarted++;
        AlignAttackShapes(box);
        box.Activate(Hit.From(spec, NextAttackId(kind), GlobalPosition, 1f, this));
    }

    /// <summary>box 的判定形状按朝向左右翻转（基准位置写在场景里，按朝右摆）。</summary>
    public void AlignAttackShapes(HitBox box)
    {
        foreach (var (shape, basePosition) in _attackShapes)
            if (IsInstanceValid(shape) && shape.GetParent() == box) shape.Position = new Vector2(Mathf.Abs(basePosition.X) * Facing, basePosition.Y);
    }

    /// <summary>关掉所有攻击判定。</summary>
    public virtual void CloseAttacks() => AttackHitBox?.Deactivate();

    private void OnHitLanded(HurtBox target, Hit hit, int outcome) => OnAttackResolved(target, hit, (HitOutcome)outcome);

    /// <summary>自己的攻击结算了（没打中不会有回调）。默认转给当前状态。</summary>
    protected virtual void OnAttackResolved(HurtBox target, Hit hit, HitOutcome outcome) => State?.OnHitLanded(target, hit, outcome);

    // ---------- 受击 ----------

    private void OnHurtDamaged(Hit hit, int dealt)
    {
        if (IsDead || Stats.IsDepleted) return; // 打死的情况由 Killed 处理
        OnDamaged(hit, dealt);
    }

    private void OnHurtKilled(Hit hit) => OnKilled(hit);

    /// <summary>受到没打死自己的攻击。普通敌人按行为组合反应；Boss 覆盖。</summary>
    protected virtual void OnDamaged(Hit hit, int dealt) => React();

    /// <summary>
    /// 受击反应（被打中没死时，每次都重新决定）：先按 FleeChance 逃跑（掷一次反应随机数；为 0 时不掷）。
    /// 没逃跑时：被动型（祈祷泥人）配了攻击就起身反击——前摇、挥击、收招各一次，之后回去跪着，不会追；没配攻击就不理会。
    /// 攻击型进受击硬直（打断前摇和出手）。
    /// </summary>
    public void React()
    {
        Reactions++;
        bool flee = Data.FleeChance > 0f && ReactionRng.Randf() < Data.FleeChance;
        if (flee)
        {
            LastReaction = "flee";
            Machine.TransitionTo<FleeState>();
        }
        else if (Data.Behaviour == EnemyBehaviour.Passive)
        {
            if (Data.AttackSpec != null && AttackHitBox != null)
            {
                LastReaction = "counter";
                Machine.TransitionTo<AttackWindupState>();
            }
            else
            {
                LastReaction = "none";
            }
        }
        else
        {
            LastReaction = "hurt";
            Machine.TransitionTo<HurtState>();
        }
    }

    /// <summary>血量归零。普通敌人死亡；Boss 覆盖（守坑人变身）。</summary>
    protected virtual void OnKilled(Hit hit)
    {
        if (IsDead) return;
        IsDead = true;
        CloseAttacks();
        if (HurtBox != null) HurtBox.Receiving = false;
        CollisionLayer = 0;
        KilledEmits++;
        Events.Instance?.EmitSignal(Events.SignalName.EnemyKilled, Data.Kind, GlobalPosition);
        DropTable drops = Drops;
        if (drops != null && drops.Roll(DropRng))
            Events.Instance?.EmitSignal(Events.SignalName.ItemDropped, drops.ItemId, GlobalPosition);
        Machine.TransitionTo<DeadState>();
    }

    /// <summary>行为结束后回到自由状态：看得见玩家的攻击型去追，否则待机（被动型回去跪着）。</summary>
    public void ToFreeState()
    {
        if (Data.Behaviour != EnemyBehaviour.Passive && CanSeeTarget()) Machine.TransitionTo<ChaseState>();
        else Machine.TransitionTo<IdleState>();
    }

    // ---------- 技术占位外观 ----------

    public override void _Draw()
    {
        Vector2 size = BodySize;
        Color tint = Data?.Tint ?? new Color("8a6a4a");
        if (IsDead)
        {
            // 死亡时流出红色的泥土（占位）
            DrawRect(new Rect2(-size.X * 0.8f, -6f, size.X * 1.6f, 6f), new Color(0.6f, 0.1f, 0.08f));
            return;
        }
        if (State is FleeState) tint = tint.Lightened(0.3f);
        if (State is AttackWindupState) tint = tint.Lerp(new Color(1f, 0.85f, 0.3f), 0.5f);
        // 被动型待着时是跪坐祈祷的姿势（矮一截、双手合在胸前）；逃跑和反击时站起来。
        bool kneeling = Data?.Behaviour == EnemyBehaviour.Passive && State is IdleState;
        float height = kneeling ? size.Y * 0.7f : size.Y;
        DrawRect(new Rect2(-size.X * 0.5f, -height, size.X, height), tint);
        DrawRect(new Rect2(Facing > 0 ? size.X * 0.2f : -size.X * 0.4f, -height * 0.85f, size.X * 0.2f, 6f), Colors.Black);
        if (kneeling) DrawRect(new Rect2(Facing * size.X * 0.25f - 6f, -height * 0.6f, 12f, 14f), tint.Darkened(0.25f));
        if (State is AttackWindupState && Data?.Behaviour == EnemyBehaviour.Passive)
        {
            // 反击前摇：泥手高高举起（占位）
            DrawRect(new Rect2(Facing * size.X * 0.5f - 5f, -size.Y - 24f, 10f, 30f), tint.Darkened(0.2f));
        }
        if (AttackHitBox is { Active: true })
            foreach (var (shape, _) in _attackShapes)
                if (shape.GetParent() == AttackHitBox && shape.Shape is RectangleShape2D rect)
                    DrawRect(new Rect2(shape.Position - rect.Size * 0.5f, rect.Size), new Color(1f, 0.3f, 0.2f, 0.35f));
    }
}
