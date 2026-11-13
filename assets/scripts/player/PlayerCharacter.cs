using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player.States;

namespace SonOfMud.Player;

/// <summary>
/// 玩家外壳：持有组件引用、统一的输入读取、移动辅助函数，以及跨状态的全局规则
/// （受击分派、切形态、输入锁、换房清理、复活、按进度决定的血量上限）。每个动作的逻辑都在 states/ 里各自的状态类中。
///
/// 每个物理帧的顺序固定：计时 → 记录输入缓冲 → 输入锁 / 切形态 → 当前状态 PhysicsUpdate（设速度、切状态）
/// → MoveAndSlide → 当前状态 AfterMove（落地、离地、贴墙判断）。HitBox 是子节点，在这之后查询。
/// </summary>
[GlobalClass]
public partial class PlayerCharacter : CharacterBody2D
{
    [Export] public PlayerRules Rules { get; set; }
    [Export] public FormController Forms { get; set; }
    [Export] public StateMachine Machine { get; set; }
    [Export] public Stats Stats { get; set; }
    [Export] public HurtBox HurtBox { get; set; }
    [Export] public HitBox AttackHitBox { get; set; }
    [Export] public HitBox SlamHitBox { get; set; }
    [Export] public PlayerAnimator Animator { get; set; }

    public PlayerTimers Timers { get; } = new();
    public InputBuffer Buffer { get; } = new();
    public CoyoteTimer Coyote { get; } = new();
    public float Facing { get; set; } = 1f;
    /// <summary>起跳那一刻冻结的跳跃参数；落地、换房传送时清掉。空中切形态不改变这次的轨迹。</summary>
    public FormConfig JumpProfile { get; set; }
    public FormConfig AirProfile => JumpProfile ?? Forms.Current;
    public PlayerState State => Machine?.Current as PlayerState;

    private int _attackSequence;
    private uint _bodyMask;
    private CollisionShape2D _bodyShape;
    /// <summary>正在按进度调整血量上限：期间 Stats 的 HealthChanged 不单独转发，调整完统一报一次。</summary>
    private bool _applyingMaxHealth;

    public override void _Ready()
    {
        AddToGroup("player");
        Rules ??= new PlayerRules();
        Forms ??= GetNode<FormController>("FormController");
        Machine ??= GetNode<StateMachine>("StateMachine");
        Stats ??= GetNode<Stats>("Stats");
        HurtBox ??= GetNode<HurtBox>("HurtBox");
        AttackHitBox ??= GetNode<HitBox>("AttackHitBox");
        SlamHitBox ??= GetNode<HitBox>("SlamHitBox");
        Animator ??= GetNodeOrNull<PlayerAnimator>("PlayerAnimator");
        _bodyMask = CollisionMask;

        // 由 Player 自己按固定顺序驱动状态机（见类注释）。
        Machine.SetPhysicsProcess(false);
        Forms.SetForm(Rules.InitialForm);
        // 先按当前进度定上限；正式开局时 SaveService 清空 / 恢复进度后会发 ProgressRestored，再按最终进度重算一次。
        Stats.MaxHealth = Rules.MaxHealthFor(GameSession.Progress);
        Stats.Refill();

        HurtBox.Damaged += OnDamaged;
        HurtBox.Blocked += OnBlocked;
        Stats.HealthChanged += OnHealthChanged;
        Stats.Depleted += OnDepleted;
        AttackHitBox.HitLanded += OnHitLanded;
        SlamHitBox.HitLanded += OnHitLanded;
        Forms.FormChanged += OnFormChanged;
        Machine.StateChanged += OnStateChanged;
        Animator?.Bind(this);

        if (Events.Instance is { } events)
        {
            events.RoomExiting += OnRoomExiting;
            events.PlayerTeleported += OnTeleported;
            events.CheckpointRespawned += OnCheckpointRespawned;
            events.InputLockChanged += OnInputLockChanged;
            events.DebugFlyToggled += OnDebugFlyToggled;
            events.ProgressRestored += OnProgressRestored;
            events.VariableChanged += OnVariableChanged;
        }
        Callable.From(AnnounceSpawn).CallDeferred();
    }

    /// <summary>进入场景树就登记为当前玩家（晚加载的敌人从 PlayerRegistry 拿到玩家）。</summary>
    public override void _EnterTree() => PlayerRegistry.Register(this);

    public override void _ExitTree()
    {
        PlayerRegistry.Unregister(this);
        if (Events.Instance is not { } events) return;
        events.RoomExiting -= OnRoomExiting;
        events.PlayerTeleported -= OnTeleported;
        events.CheckpointRespawned -= OnCheckpointRespawned;
        events.InputLockChanged -= OnInputLockChanged;
        events.DebugFlyToggled -= OnDebugFlyToggled;
        events.ProgressRestored -= OnProgressRestored;
        events.VariableChanged -= OnVariableChanged;
    }

    private void AnnounceSpawn()
    {
        Events.Instance?.EmitSignal(Events.SignalName.PlayerSpawned, this);
        // 正式开局（新游戏 / 读档）还在排队时，进度还没换成最终内容，这时的上限可能不对（读档前总是起始上限）。
        // 先不报血量，等 SaveService 发 ProgressRestored 时再报，HUD 不会先显示 3 颗再跳成 7 颗。
        if (GameSession.PendingStart == StartMode.None) AnnounceHealth();
        Events.Instance?.EmitSignal(Events.SignalName.PlayerFormChanged, Forms.Form);
    }

    private void AnnounceHealth() =>
        Events.Instance?.EmitSignal(Events.SignalName.PlayerHealthChanged, Stats.Health, Stats.MaxHealth);

    // ---------- 每帧 ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Timers.Tick(dt);
        Buffer.Tick(dt);
        Coyote.Tick(dt);

        if (!InputLock.IsLocked)
            foreach (StringName action in PlayerActions.Buffered)
                if (Input.IsActionJustPressed(action)) Buffer.Press(action);

        if (InputLock.IsLocked && State is { Lockable: true }) Machine.TransitionTo<LockedState>();
        if (JustPressed(PlayerActions.SwitchForm)) Forms.TrySwitch(this, State?.CanSwitchForm ?? false);

        State?.PhysicsUpdate(delta);
        MoveAndSlide();
        State?.AfterMove(delta);
    }

    // ---------- 输入（输入锁期间一律读不到） ----------

    public bool Held(StringName action) => !InputLock.IsLocked && Input.IsActionPressed(action);
    public bool JustPressed(StringName action) => !InputLock.IsLocked && Input.IsActionJustPressed(action);
    public float Axis => InputLock.IsLocked ? 0f : Input.GetAxis(PlayerActions.Left, PlayerActions.Right);
    public bool HasAbility(StringName id) => GameSession.Progress.HasAbility(id);

    // ---------- 移动辅助 ----------

    /// <summary>按当前形态移速走动，乘上减速恢复倍率；有输入时更新朝向。</summary>
    public void Walk(float axis)
    {
        if (axis != 0f) Facing = Mathf.Sign(axis);
        Velocity = new Vector2(axis * Forms.Current.MoveSpeed * Timers.Recovery.Multiplier(), Velocity.Y);
    }

    public void StopHorizontal() => Velocity = new Vector2(0f, Velocity.Y);

    /// <summary>地面上给一点向下的速度，保证 IsOnFloor 稳定。</summary>
    public void StickToFloor(float delta) => Velocity = new Vector2(Velocity.X, Forms.Current.Gravity * delta);

    /// <summary>空中重力：用起跳时冻结的参数。水过顶点后恒速下落；泥按重力加速、受终端速度限制。</summary>
    public void ApplyAirGravity(float delta)
    {
        FormConfig p = AirProfile;
        float vy = Velocity.Y;
        if (p.JumpStyle == JumpStyle.ConstantDescent && vy >= 0f) vy = p.DescentSpeed;
        else vy = Mathf.Min(vy + p.Gravity * delta, p.TerminalSpeed);
        Velocity = new Vector2(Velocity.X, vy);
    }

    public void ApplyGravity(float delta)
    {
        if (IsOnFloor()) StickToFloor(delta);
        else ApplyAirGravity(delta);
    }

    /// <summary>动作结束后回到自由状态：地面 → Idle / Run，空中 → Fall。</summary>
    public void ToFreeState()
    {
        if (!IsOnFloor()) Machine.TransitionTo<FallState>();
        else if (Axis != 0f) Machine.TransitionTo<RunState>();
        else Machine.TransitionTo<IdleState>();
    }

    public bool CanStartWallClimb() =>
        CanUseWall() && Held(PlayerActions.Up) && Axis != 0f && Axis * GetWallNormal().X < 0f;

    // ---------- 墙面（关卡几何计划 v3 第 2.1 节） ----------

    /// <summary>
    /// 这面墙能不能用（开始攀墙、攀墙过程中、蹬墙跳三条路径共用）：有攀墙能力、正贴着墙，
    /// 并且上一次 MoveAndSlide 接触到的墙面里没有任何一个不可攀（见 TouchesNoClimbWall）。
    /// </summary>
    public bool CanUseWall() => HasAbility(PlayerAbilities.WallClimb) && IsOnWall() && !TouchesNoClimbWall();

    /// <summary>
    /// 上一次 MoveAndSlide 的滑动碰撞里，有没有接触到不可攀的墙面（只看墙，地面和天花板不算）。
    /// - 碰撞体是 TileMapLayer：用碰撞点（沿法线往墙里退半个像素）找到瓦片坐标；再把身体侧面贴着的那一列瓦片
    ///   （覆盖身体整个高度）也算进来，因为一次滑动碰撞只报告一个接触点，而身体同时贴着两三块瓦片。
    ///   瓦片集有 Surfaces.NoClimbData 自定义数据层、值为 true 就是不可攀。
    ///   不用 GetCoordsForBodyRid：Godot 4.7.2 的 TileMapLayer 默认 physics_quadrant_size = 16，同一象限的瓦片合成一个物理体，
    ///   这时它返回的是象限坐标（实测贴着第 (30,17) 格的墙返回 (1,1)），不是瓦片坐标。
    /// - 其他碰撞体：在 Surfaces.NoClimbGroup 分组里就是不可攀（ArenaLock 门体等）。
    /// 只要有一个不可攀的接触就返回 true。
    /// </summary>
    public bool TouchesNoClimbWall()
    {
        int count = GetSlideCollisionCount();
        for (int i = 0; i < count; i++)
        {
            KinematicCollision2D collision = GetSlideCollision(i);
            Vector2 normal = collision.GetNormal();
            if (!IsWallNormal(normal)) continue;
            GodotObject collider = collision.GetCollider();
            if (collider is TileMapLayer layer)
            {
                Vector2 inside = collision.GetPosition() - normal * 0.5f;
                if (NoClimbTile(layer, layer.LocalToMap(layer.ToLocal(inside)))) return true;
                if (NoClimbColumn(layer, normal)) return true;
            }
            else if (collider is Node node && node.IsInGroup(Surfaces.NoClimbGroup)) return true;
        }
        return false;
    }

    /// <summary>和 CharacterBody2D 的分类一致：法线与“上”或“下”的夹角都大于 FloorMaxAngle 的才是墙。</summary>
    private bool IsWallNormal(Vector2 normal)
    {
        float limit = FloorMaxAngle + 0.01f;
        return Mathf.Abs(normal.AngleTo(UpDirection)) > limit && Mathf.Abs(normal.AngleTo(-UpDirection)) > limit;
    }

    private static bool NoClimbTile(TileMapLayer layer, Vector2I cell)
    {
        TileSet tileSet = layer.TileSet;
        if (tileSet == null || tileSet.GetCustomDataLayerByName(Surfaces.NoClimbData) < 0) return false;
        TileData data = layer.GetCellTileData(cell);
        return data != null && data.GetCustomData(Surfaces.NoClimbData).AsBool();
    }

    /// <summary>身体侧面贴着的那一列瓦片：墙在法线反方向，竖直范围是身体的上下边（各内缩 1 像素，不算脚下和头顶的角）。</summary>
    private bool NoClimbColumn(TileMapLayer layer, Vector2 normal)
    {
        if (layer.TileSet == null || layer.TileSet.GetCustomDataLayerByName(Surfaces.NoClimbData) < 0) return false;
        Rect2 body = BodyRect();
        float x = normal.X > 0f ? body.Position.X - 1f : body.End.X + 1f;
        float top = body.Position.Y + 1f, bottom = body.End.Y - 1f;
        float step = Mathf.Max(1f, layer.TileSet.TileSize.Y / 2f);
        for (float y = top; ; y = Mathf.Min(y + step, bottom))
        {
            if (NoClimbTile(layer, layer.LocalToMap(layer.ToLocal(new Vector2(x, y))))) return true;
            if (y >= bottom) return false;
        }
    }

    /// <summary>身体碰撞矩形（全局坐标）。player_v2 的身体是 48×48 的 RectangleShape2D。</summary>
    private Rect2 BodyRect()
    {
        _bodyShape ??= GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        Vector2 size = _bodyShape?.Shape is RectangleShape2D rect ? rect.Size : new Vector2(48f, 48f);
        Vector2 center = _bodyShape?.GlobalPosition ?? GlobalPosition;
        return new Rect2(center - size / 2f, size);
    }

    public string NextAttackId(string kind) => $"player_{GetInstanceId()}_{kind}_{++_attackSequence}";

    public void CloseHitBoxes()
    {
        AttackHitBox.Deactivate();
        SlamHitBox.Deactivate();
    }

    public void SetBodyCollision(bool on) => CollisionMask = on ? _bodyMask : 0u;

    // ---------- 动作入口（各状态共用的「能不能开始」判断） ----------

    /// <summary>消费缓冲的攻击输入：空中按住下且有下砸能力 → Slam；否则普通攻击（空中按住下 → 下劈）。</summary>
    public bool TryStartAttack()
    {
        if (!Timers.AttackReady || !Buffer.Consume(PlayerActions.Attack, Rules.ActionBuffer)) return false;
        bool down = !IsOnFloor() && Held(PlayerActions.Down);
        if (down && HasAbility(PlayerAbilities.HeavySlam)) Machine.TransitionTo<SlamState>();
        else Machine.TransitionTo<AttackState>(new StateMsg().Set(AttackState.DownKey, down));
        return true;
    }

    public bool TryStartBlock()
    {
        if (!HasAbility(PlayerAbilities.Block) || !Timers.BlockReady) return false;
        if (!Buffer.Consume(PlayerActions.Block, Rules.ActionBuffer)) return false;
        Machine.TransitionTo<BlockState>();
        return true;
    }

    public bool TryStartDodge()
    {
        if (!Timers.DodgeReady || !Buffer.Consume(PlayerActions.Dodge, Rules.ActionBuffer)) return false;
        Machine.TransitionTo<DodgeState>();
        return true;
    }

    // ---------- 受击 ----------

    private void OnDamaged(Hit hit, int dealt)
    {
        if (Stats.IsDepleted)
        {
            Machine.TransitionTo<DeadState>();
            return;
        }
        if (State is StunnedState stunned)
        {
            // 眩晕中还能继续挨打（策划要的连环惩罚）：不打断眩晕，带眩晕的攻击取更长的剩余时间。
            stunned.Extend(hit);
            return;
        }
        if (hit.StunSeconds > 0f) Machine.TransitionTo<StunnedState>(StateMsg.With(hit));
        else Machine.TransitionTo<HurtState>(StateMsg.With(hit));
    }

    private void OnBlocked(Hit hit)
    {
        if (State is BlockState block) block.OnBlocked(hit);
    }

    /// <summary>
    /// 自己的攻击结算了。真正打中（受伤 / 打死）时按这一刀的打击感（Hit.Feel）请求顿帧、震屏、命中点粒子和音效，
    /// 每次结算只请求一次顿帧；没配打击感时顿帧用 Rules.HitStopSeconds。闪白和击退由受击方自己处理。
    /// </summary>
    private void OnHitLanded(HurtBox target, Hit hit, int outcomeValue)
    {
        var outcome = (HitOutcome)outcomeValue;
        Timers.Bonus.OnOutcome(outcome);
        if (outcome is not (HitOutcome.Damaged or HitOutcome.Killed)) return;
        bool killed = outcome == HitOutcome.Killed;
        HitFeel feel = hit.Feel;
        Events events = Events.Instance;
        events?.EmitSignal(Events.SignalName.HitStopRequested, feel?.HitStopFor(killed) ?? Rules.HitStopSeconds);
        if (feel == null || events == null) return;
        float shake = feel.ShakeFor(killed);
        if (shake > 0f && feel.ShakeDuration > 0f)
            events.EmitSignal(Events.SignalName.CameraShakeRequested, shake, feel.ShakeDuration);
        if (feel.SparkAmount > 0)
        {
            (Vector2 point, float side) = SparkPoint(target);
            events.EmitSignal(Events.SignalName.HitSparkRequested, point, new Vector2(side, 0f), feel.SparkAmount, feel.SparkColor, feel.SparkSpeed);
        }
        Godot.Collections.Array<AudioStream> sounds = feel.SoundsFor(killed);
        if (sounds.Count > 0) events.EmitSignal(Events.SignalName.SoundRequested, (Godot.Collections.Array)sounds);
    }

    /// <summary>粒子出现在受击框朝向主角那一侧的中点，朝远离主角的方向喷。</summary>
    private (Vector2 point, float side) SparkPoint(HurtBox target)
    {
        Vector2 center = target.GlobalPosition;
        float halfWidth = 0f;
        foreach (Node child in target.GetChildren())
        {
            if (child is not CollisionShape2D shape) continue;
            center = shape.GlobalPosition;
            if (shape.Shape is RectangleShape2D rect) halfWidth = rect.Size.X * 0.5f;
            else if (shape.Shape is CircleShape2D circle) halfWidth = circle.Radius;
            break;
        }
        float side = Mathf.Sign(center.X - GlobalPosition.X);
        if (side == 0f) side = Facing;
        return (center - new Vector2(side * halfWidth, 0f), side);
    }

    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        if (!_applyingMaxHealth) AnnounceHealth();
    }

    // ---------- 血量上限（生命之面包） ----------

    /// <summary>新游戏 / 读档：进度整体换掉了，按最终进度定上限并回满血（读档等于回到存档点），然后报一次血量。</summary>
    private void OnProgressRestored() => ApplyMaxHealth(refill: true, alwaysAnnounce: true);

    /// <summary>剧情等改了生命之面包的变量：上限变大时当前血量加上新增的部分（吃下面包 +70）。</summary>
    private void OnVariableChanged(StringName key, Variant value)
    {
        if (key == Rules.LifeBreadFlag) ApplyMaxHealth(refill: false, alwaysAnnounce: false);
    }

    /// <summary>
    /// 按 GameSession.Progress 重算血量上限。refill=true 时回满；否则上限增加多少当前血量就加多少（上限变小时只按新上限截断），
    /// 已经倒下（血量 0）时不加血。有变化（或 alwaysAnnounce）时发一次 PlayerHealthChanged。
    /// </summary>
    private void ApplyMaxHealth(bool refill, bool alwaysAnnounce)
    {
        int oldMax = Stats.MaxHealth;
        int oldHealth = Stats.Health;
        int newMax = Rules.MaxHealthFor(GameSession.Progress);
        int gained = Mathf.Max(0, newMax - oldMax);
        int newHealth = refill ? newMax : Stats.IsDepleted ? 0 : oldHealth + gained;
        _applyingMaxHealth = true;
        Stats.MaxHealth = newMax;
        Stats.SetHealth(newHealth);
        _applyingMaxHealth = false;
        if (alwaysAnnounce || oldMax != Stats.MaxHealth || oldHealth != Stats.Health) AnnounceHealth();
    }

    /// <summary>
    /// 血量归零时不立刻通知：延到本帧的伤害结算全部结束之后再发 PlayerDied，
    /// 避免复活流程在 HurtBox.Receive 还没返回时就重入（审查 M5）。
    /// </summary>
    private void OnDepleted() => Callable.From(EmitDied).CallDeferred();

    private void EmitDied()
    {
        if (IsInstanceValid(this) && Stats.IsDepleted) Events.Instance?.EmitSignal(Events.SignalName.PlayerDied);
    }

    private void OnFormChanged(int form) => Events.Instance?.EmitSignal(Events.SignalName.PlayerFormChanged, form);

    private void OnStateChanged(StringName from, StringName to)
    {
        HurtBox.Receiving = State?.AcceptsHits ?? true;
    }

    // ---------- 换房、复活、锁定、调试飞行 ----------

    /// <summary>
    /// 每次换房（走边缘、过门都算）卸载旧房间前：只关掉正在生效的 HitBox，并让攻击状态把这一刀标记为
    /// 「判定已结束」；状态、速度、冷却、缓冲、下砸资格都保留（规格第 4 节）。
    /// </summary>
    public void OnRoomExiting(string roomId)
    {
        State?.OnRoomExiting();
        CloseHitBoxes();
    }

    /// <summary>过门和复活后：完整清理。回 Idle、速度清零、关 HitBox、清缓冲、清下砸资格；冷却、减速、增伤保留。</summary>
    public void OnTeleported(StringName spawnId)
    {
        State?.OnRoomExiting();
        CloseHitBoxes();
        Buffer.Clear();
        Coyote.Clear();
        JumpProfile = null;
        Velocity = Vector2.Zero;
        if (State is not DeadState) Machine.TransitionTo<IdleState>();
    }

    /// <summary>回到存档点（D4）：回满血、回 Idle。</summary>
    public void OnCheckpointRespawned()
    {
        CloseHitBoxes();
        Buffer.Clear();
        Coyote.Clear();
        JumpProfile = null;
        Velocity = Vector2.Zero;
        Stats.Refill();
        Machine.TransitionTo<IdleState>();
    }

    private void OnInputLockChanged(bool locked)
    {
        if (locked && State is { Lockable: true }) Machine.TransitionTo<LockedState>();
    }

    private void OnDebugFlyToggled()
    {
        if (State is DeadState) return;
        if (State is DebugFlyState) Machine.TransitionTo<FallState>();
        else Machine.TransitionTo<DebugFlyState>();
    }

    /// <summary>复位到指定位置和形态（测试场、调试用）：清掉所有计时、缓冲和动作。</summary>
    public void ResetForLab(Vector2 position, int form)
    {
        CloseHitBoxes();
        Buffer.Clear();
        Coyote.Clear();
        JumpProfile = null;
        Timers.DodgeCooldown = Timers.BlockCooldown = Timers.AttackInterval = Timers.SwitchCooldown = 0f;
        Timers.Recovery.Clear();
        Timers.Bonus.Clear();
        Forms.SetForm(form);
        Facing = 1f;
        GlobalPosition = position;
        Velocity = Vector2.Zero;
        Stats.Refill();
        Machine.TransitionTo<IdleState>();
    }
}
