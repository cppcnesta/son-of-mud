using Godot;
using GDictionary = Godot.Collections.Dictionary;
using Kind = MudActionState.Kind;

public partial class MudPlayer : CharacterBody2D
{
    [Signal] public delegate void DiedEventHandler();
    [Signal] public delegate void AttackBlockedEventHandler(string attackId);

    [ExportGroup("配置资源（.tres）")]
    [Export] public MudFormConfig WaterConfig { get; set; } = GD.Load<MudFormConfig>("res://assets/data/player/water.tres");
    [Export] public MudFormConfig MudConfig { get; set; } = GD.Load<MudFormConfig>("res://assets/data/player/mud.tres");
    [Export] public MudPrototypeRules Rules { get; set; } = GD.Load<MudPrototypeRules>("res://assets/data/player/prototype_rules.tres");
    [ExportGroup("调试")]
    [Export] public float InspectionSpeed { get; set; } = 500f;

    public MudActionState Action { get; } = new();
    public MudPlayerCombat Combat { get; } = new();
    public MudMovementRecovery Recovery { get; } = new();
    public MudPlayerGuard Guard { get; } = new();
    public MudAttackLedger Incoming { get; } = new();
    public int Form { get; set; }
    public float Facing { get; set; } = 1f;
    public float Health { get; set; } = 120f;
    public float DodgeCooldown { get; set; }
    public float SwitchRemaining { get; set; }
    public float PenaltyRemaining => Recovery.Remaining;
    public float PenaltyTotal => Recovery.Duration;
    public float PenaltyInitial => Recovery.InitialMultiplier;
    public MudFormConfig DodgeProfile { get; private set; }
    public MudFormConfig BlockProfile => Guard.Profile;
    public MudFormConfig JumpProfile { get; private set; }
    // The pending launch and the current airborne trajectory are separate contexts.
    public MudFormConfig PendingJumpProfile { get; private set; }
    public int PendingJumpForm { get; private set; }
    public int PendingJumpModule { get; private set; }
    public int JumpForm { get; private set; }
    public int JumpModule { get; private set; }
    public int JumpsUsed { get; set; }
    public float JumpOriginY { get; private set; }
    public float HoldTime { get; private set; }
    public bool HoldEligible { get; private set; }
    public bool ReachedCap { get; set; }
    public bool JumpInProgress { get; private set; }
    public bool Submerged { get; set; }
    public float PeakHeight { get; private set; }

    public override void _Ready()
    {
        AddToGroup("player");
        Form = Rules.InitialForm;
        Health = Rules.PlayerHealth;
        Combat.Actor = this;
        Guard.Actor = this;
        AddChild(Combat);
        GetNodeOrNull<Sprite2D>("Sprite2D")?.Hide();
    }
    public MudFormConfig Config() => Form == 0 ? WaterConfig : MudConfig;
    public bool HasAbility(string key) => MudProgress.Instance.HasAbility(key);
    public float PenaltyMultiplier() => Recovery.Multiplier();

    public bool SwitchForm()
    {
        if (!HasAbility("form_switch") || SwitchRemaining > 0f) return false;
        if (Submerged && Form == 0) return false;
        if (Combat.RecoveryRemaining > 0f && !Rules.AllowActionCancel) return false;
        if (!IsOnFloor() && !Rules.AllowAirSwitch) return false;
        if (Action.State != Kind.Free)
        {
            if (!Rules.AllowActionCancel || Action.State == Kind.Dead) return false;
            if (Action.State == Kind.Dodge) FinishDodge();
            CancelPendingJump();
            Action.Enter(Kind.Free);
        }
        Form = 1 - Form;
        SwitchRemaining = MudProgress.Instance.TestMode && Rules.TestFastSwitch ? 0f : Rules.SwitchCooldown;
        QueueRedraw();
        return true;
    }
    public bool StartDodge()
    {
        var cancelRecovery = Combat.CanDodgeCancel() && (Action.State == Kind.Free || Action.State == Kind.AttackRecovery);
        if ((Action.State != Kind.Free && !cancelRecovery) || DodgeCooldown > 0f) return false;
        if (cancelRecovery) Combat.ResetAttackInterval();
        DodgeProfile = Config();
        DodgeCooldown = DodgeProfile.DodgeCooldown;
        Action.Enter(Kind.Dodge, DodgeProfile.DodgeDuration);
        return true;
    }
    public void FinishDodge()
    {
        if (DodgeProfile != null && DodgeProfile.PenaltyDuration > 0f)
            Recovery.Start(MudMovementRecovery.Source.Dodge, DodgeProfile.PenaltyDuration,
                DodgeProfile.PenaltyInitialMultiplier, DodgeProfile.RecoveryCurve);
        Action.Enter(Kind.Free);
    }
    public bool StartBlock() => Guard.Start();
    public bool RequestJump()
    {
        if (Action.State != Kind.Free) return false;
        var wallJump = HasAbility("wall_climb") && IsOnWall() && !IsOnFloor();
        if (!IsOnFloor() && !wallJump && (!HasAbility("double_jump") || JumpsUsed >= 2)) return false;
        if (!IsOnFloor() && !wallJump && JumpsUsed == 0) JumpsUsed = 1;
        PendingJumpProfile = Config();
        PendingJumpForm = Form;
        PendingJumpModule = MudProgress.Instance.MudModule;
        Action.Enter(Kind.JumpWindup, PendingJumpProfile.JumpWindup);
        return true;
    }
    public void LaunchJump()
    {
        if (Action.State != Kind.JumpWindup || PendingJumpProfile == null)
        {
            CancelPendingJump();
            return;
        }
        JumpProfile = PendingJumpProfile;
        JumpForm = PendingJumpForm;
        JumpModule = PendingJumpModule;
        PendingJumpProfile = null;
        JumpsUsed++;
        Velocity = new Vector2(Velocity.X, -JumpProfile.JumpSpeed);
        JumpOriginY = GlobalPosition.Y;
        HoldTime = 0f;
        HoldEligible = JumpForm == 1 && JumpModule == 1;
        ReachedCap = false;
        JumpInProgress = true;
        PeakHeight = 0f;
        Action.Enter(Kind.Free);
    }
    public void CancelPendingJump()
    {
        PendingJumpProfile = null;
        PendingJumpForm = 0;
        PendingJumpModule = 0;
        if (Action.State == Kind.JumpWindup) Action.Enter(Kind.Free);
    }
    public void ClearAirContext(bool preservePendingJump = false)
    {
        JumpInProgress = false;
        ReachedCap = false;
        HoldEligible = false;
        JumpsUsed = 0;
        JumpProfile = null;
        if (!preservePendingJump) CancelPendingJump();
    }
    public string ReceiveAttack(float damage, string attackId, Vector2 source)
    {
        if (!Incoming.Accept(attackId) || Action.State == Kind.Dead) return "duplicate";
        if (Guard.Covers(source))
        {
            Guard.Succeed();
            EmitSignal(SignalName.AttackBlocked, attackId);
            QueueRedraw();
            return "blocked";
        }
        if (Action.State == Kind.Dodge && Rules.DodgeInvulnerability > 0f &&
            DodgeProfile.DodgeDuration - Action.Remaining <= Rules.DodgeInvulnerability) return "invulnerable";
        Health = Mathf.Max(0f, Health - damage);
        if (Action.State == Kind.Dodge) FinishDodge();
        CancelPendingJump();
        if (Health <= 0f)
        {
            Action.Enter(Kind.Dead);
            EmitSignal(SignalName.Died);
        }
        else Action.Enter(Kind.Hurt, Rules.HurtDuration);
        QueueRedraw();
        return "hurt";
    }
    public GDictionary ExportTransient() => new()
    {
        ["form"] = Form, ["health"] = Health, ["dodge"] = DodgeCooldown,
        ["switch"] = SwitchRemaining, ["recovery"] = Recovery.Snapshot(),
        ["block_cooldown"] = Guard.CooldownRemaining, ["combat_timing"] = Combat.Snapshot(),
        ["bonus"] = Combat.BonusReady
    };
    public void RestoreTransient(GDictionary data)
    {
        Form = data.Int("form", Rules.InitialForm);
        Health = data.Num("health", Rules.PlayerHealth);
        DodgeCooldown = data.Num("dodge");
        SwitchRemaining = data.Num("switch");
        if (data.ContainsKey("recovery")) Recovery.Restore(data.Dict("recovery"), MudConfig.RecoveryCurve);
        else Recovery.Restore(new GDictionary
        {
            ["source"] = 1, ["remaining"] = data.Num("penalty"),
            ["duration"] = data.Num("penalty_total", 1f), ["initial"] = data.Num("penalty_initial", 0.2f)
        }, MudConfig.RecoveryCurve);
        Guard.CooldownRemaining = Mathf.Max(0f, data.Num("block_cooldown"));
        Combat.Restore(data.Dict("combat_timing"));
        Combat.BonusReady = data.Bool("bonus");
        ClearAirContext();
        Velocity = Vector2.Zero;
        Action.Enter(Kind.Free);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double deltaValue)
    {
        var delta = (float)deltaValue;
        DodgeCooldown = Mathf.Max(0f, DodgeCooldown - delta);
        SwitchRemaining = Mathf.Max(0f, SwitchRemaining - delta);
        Recovery.Tick(delta);
        Action.Tick(delta);
        Guard.Tick(delta);
        if (Action.State == Kind.Dead) { Velocity = Vector2.Zero; return; }
        if (Input.IsActionJustPressed("switch_form")) SwitchForm();
        if (Input.IsActionJustPressed("dodge")) StartDodge();
        if (Input.IsActionJustPressed("block")) StartBlock();
        if (Input.IsActionJustPressed("jump")) RequestJump();
        if (Input.IsActionJustPressed("attack") && Action.State == Kind.Free && Combat.IsReady())
        {
            if (Input.IsActionPressed("move_down") && !IsOnFloor() && HasAbility("heavy_slam"))
            {
                Action.Enter(Kind.Slam);
                ReachedCap = false;
            }
            else Combat.Start(Config(), !IsOnFloor() && Input.IsActionPressed("move_down"));
        }
        if (Action.State == Kind.JumpWindup && Action.Remaining <= 0f) LaunchJump();
        if (Action.State == Kind.Dodge && Action.Remaining <= 0f) FinishDodge();
        if (Action.State == Kind.Hurt && Action.Remaining <= 0f) Action.Enter(Kind.Free);
        Combat.Tick(delta);
        var axis = Input.GetAxis("move_left", "move_right");
        var canMove = Action.State == Kind.Free || (Action.State == Kind.AttackRecovery && Combat.CanMoveInRecovery());
        if (axis != 0f && canMove) Facing = Mathf.Sign(axis);
        var oldFloor = IsOnFloor();
        var velocity = Velocity;
        velocity.X = Action.State == Kind.Dodge ? Facing * DodgeProfile.DodgeSpeed :
            canMove ? axis * Config().MoveSpeed * PenaltyMultiplier() : 0f;
        var activeProfile = JumpProfile ?? Config();
        if (!IsOnFloor())
        {
            if (activeProfile.ConstantDescent && velocity.Y >= 0f) velocity.Y = activeProfile.DescentSpeed;
            else
            {
                var gravity = activeProfile.Gravity;
                if (HoldEligible && velocity.Y < 0f)
                {
                    if (!Input.IsActionPressed("jump"))
                    {
                        HoldEligible = false;
                        velocity.Y = Mathf.Max(velocity.Y, -130f);
                    }
                    else if (HoldTime < activeProfile.HoldMaxTime)
                    {
                        HoldTime += delta;
                        gravity -= activeProfile.HoldAcceleration;
                    }
                }
                velocity.Y = Mathf.Min(velocity.Y + gravity * delta, activeProfile.TerminalSpeed);
            }
            if (HasAbility("wall_climb") && IsOnWall() && axis != 0f && Input.IsActionPressed("move_up") && Action.State == Kind.Free)
                velocity.Y = -180f;
        }
        if (Submerged && HasAbility("dive") && Form == 0) velocity.Y = Input.GetAxis("move_up", "move_down") * 200f;
        if (Action.State == Kind.Slam) velocity = new Vector2(0f, Rules.SlamSpeed);
        Velocity = velocity;
        MoveAndSlide();
        if (JumpInProgress)
        {
            var height = JumpOriginY - GlobalPosition.Y;
            PeakHeight = Mathf.Max(PeakHeight, height);
            var cap = HasAbility("high_jump_upgrade") ? Rules.UpgradedHoldHeight : activeProfile.HoldHeightCap;
            if (JumpForm == 1 && JumpModule == 1 && height >= cap)
            {
                GlobalPosition = new Vector2(GlobalPosition.X, JumpOriginY - cap);
                if (HoldEligible) ReachedCap = true;
                Velocity = new Vector2(Velocity.X, Mathf.Max(0f, Velocity.Y));
            }
        }
        if (IsOnCeiling()) { HoldEligible = false; ReachedCap = false; }
        if (IsOnFloor() && !oldFloor)
        {
            if (Action.State == Kind.Slam)
            {
                Combat.Burst(Rules.SlamDamage, Rules.LandingRadius, true);
                if (Form == 1) Combat.BeginRecovery(MudConfig);
                else Action.Enter(Kind.AttackRecovery, 0.25f);
            }
            else if (ReachedCap && JumpInProgress) Combat.Burst(Rules.LandingDamage, Rules.LandingRadius, false);
            ClearAirContext(true);
        }
        else if (IsOnFloor() && !JumpInProgress) JumpsUsed = 0;
        QueueRedraw();
    }
    public override void _Draw()
    {
        var tint = Action.State == Kind.Hurt ? new Color("ff6868") : Config().Tint;
        DrawRect(new Rect2(-20, -24, 40, 48), tint);
        DrawLine(new Vector2(0, -8), new Vector2(Facing * 25f, -8), Colors.White, 4f);
        if (Action.State == Kind.Block) DrawArc(Vector2.Zero, 36, 0, Mathf.Tau, 32, new Color("fff3a3"), 4);
        if (Combat.BonusReady) DrawCircle(new Vector2(0, -36), 6, new Color("ffdc64"));
        if (Action.State == Kind.AttackActive)
            DrawRect(new Rect2(new Vector2(Facing > 0 ? 18 : -108, -32), new Vector2(90, 64)), new Color(1f, 0.85f, 0.35f, 0.45f));
    }
}
