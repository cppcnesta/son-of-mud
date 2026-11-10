using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player.States;

namespace SonOfMud.Player;

/// <summary>
/// 玩家外壳：持有组件引用、统一的输入读取、移动辅助函数，以及跨状态的全局规则
/// （受击分派、切形态、输入锁、换房清理、复活）。每个动作的逻辑都在 states/ 里各自的状态类中。
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
        Stats.MaxHealth = Rules.MaxHealth;
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
        }
        Callable.From(AnnounceSpawn).CallDeferred();
    }

    public override void _ExitTree()
    {
        if (Events.Instance is not { } events) return;
        events.RoomExiting -= OnRoomExiting;
        events.PlayerTeleported -= OnTeleported;
        events.CheckpointRespawned -= OnCheckpointRespawned;
        events.InputLockChanged -= OnInputLockChanged;
        events.DebugFlyToggled -= OnDebugFlyToggled;
    }

    private void AnnounceSpawn()
    {
        Events.Instance?.EmitSignal(Events.SignalName.PlayerSpawned, this);
        Events.Instance?.EmitSignal(Events.SignalName.PlayerHealthChanged, Stats.Health, Stats.MaxHealth);
        Events.Instance?.EmitSignal(Events.SignalName.PlayerFormChanged, Forms.Form);
    }

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
        HasAbility(PlayerAbilities.WallClimb) && IsOnWall() && Held(PlayerActions.Up) &&
        Axis != 0f && Axis * GetWallNormal().X < 0f;

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

    private void OnHitLanded(HurtBox target, Hit hit, int outcomeValue)
    {
        var outcome = (HitOutcome)outcomeValue;
        Timers.Bonus.OnOutcome(outcome);
        if (outcome is HitOutcome.Damaged or HitOutcome.Killed)
            Events.Instance?.EmitSignal(Events.SignalName.HitStopRequested, Rules.HitStopSeconds);
    }

    private void OnHealthChanged(int oldHealth, int newHealth) =>
        Events.Instance?.EmitSignal(Events.SignalName.PlayerHealthChanged, newHealth, Stats.MaxHealth);

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
