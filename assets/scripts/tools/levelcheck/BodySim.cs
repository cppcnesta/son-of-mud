using System;
using System.Collections.Generic;
using SonOfMud.Player;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>模拟器里的玩家状态，对应 player/states 下的移动状态（攻击、闪避等不在模拟范围）。</summary>
public enum SimState
{
    Idle,
    Run,
    Windup,
    Rise,
    Fall,
    WallClimb,
}

/// <summary>一帧的输入：左右（-1/0/1）、按住上、这一帧刚按下跳。</summary>
public struct SimInput
{
    public int Axis;
    public bool Up;
    public bool Jump;

    public SimInput(int axis, bool up = false, bool jump = false)
    {
        Axis = axis;
        Up = up;
        Jump = jump;
    }
}

/// <summary>
/// 48×48 身体的逐帧模拟。每帧顺序和 PlayerCharacter._PhysicsProcess 一致：
/// 计时（跳跃缓冲变老、土狼时间减少）→ 记录这一帧按下的跳 → 当前状态 PhysicsUpdate → MoveAndSlide → AfterMove。
/// 各状态的规则逐条照抄 GroundState / JumpWindupState / AirState / RiseState / FallState / WallClimbState，
/// 数值全部来自 SimConfig（正式配置）。碰撞对轴对齐瓦片做扫掠，尽量复刻 CharacterBody2D.MoveAndSlide 的结果：
/// 最多滑动 4 次、落地 / 撞墙 / 撞顶的标志、贴地 1 像素、站地时清掉竖直速度、空中撞墙清掉水平速度、单向平台只挡从上往下。
/// 不可攀规则（计划 2.1）：这一帧接触的墙面里有 X 时，不能开始攀墙、攀墙中转为下落、不能蹬墙跳。
/// </summary>
public sealed class BodySim
{
    private const double Eps = 1e-4;
    private const int MaxSlides = 4;

    public SimConfig Cfg { get; }
    public ISimWorld World { get; set; }

    public double X, Y;
    public float VX, VY;
    public SimState State = SimState.Idle;
    public int Form;
    public FormParams JumpProfile;
    public bool HasWallClimb = true;
    public bool OnFloor = true, OnWall, OnCeiling;
    public float WallNormalX;
    /// <summary>上一次 MoveAndSlide 接触的墙面里有不可攀的瓦片。</summary>
    public bool TouchNoClimb;
    public float Facing = 1f;
    public int Frame;

    // 统计：这次模拟里有没有攀过墙、蹬过墙（路线分类用）
    public bool EverClimbed;
    public bool EverWallJumped;
    public double MinY;

    private float _windupRemaining;
    private bool _windupWall;
    private FormParams _windupProfile;
    private float _coyote;
    private float _jumpAge = -1f;
    private SimInput _in;
    private List<SimTile> _tiles = new();

    public BodySim(SimConfig cfg, ISimWorld world, double x, double y, int form)
    {
        Cfg = cfg;
        World = world;
        X = x;
        Y = y;
        MinY = y;
        Form = form;
    }

    public FormParams Cur => Cfg.Form(Form);
    public FormParams AirProfile => JumpProfile ?? Cur;
    public bool Grounded => State is SimState.Idle or SimState.Run;
    public double Bottom => Y + Cfg.HalfH;

    public BodySim Clone()
    {
        var c = (BodySim)MemberwiseClone();
        c._tiles = new List<SimTile>();
        return c;
    }

    public SimObservation Observe() => new(Grounded, State == SimState.WallClimb, !OnFloor);

    // ---------- 每帧 ----------

    public void Step(SimInput input)
    {
        _in = input;
        float dt = Cfg.Dt;
        if (_jumpAge >= 0f) _jumpAge += dt;
        _coyote = Math.Max(0f, _coyote - dt);
        if (_coyote < 0.000001f) _coyote = 0f;
        if (input.Jump) _jumpAge = 0f;

        PhysicsUpdate(dt);
        MoveAndSlide(dt);
        AfterMove();
        Frame++;
        if (Y < MinY) MinY = Y;
    }

    private bool ConsumeJump()
    {
        if (_jumpAge < 0f || _jumpAge > Cfg.JumpBuffer + 0.000001f) return false;
        _jumpAge = -1f;
        return true;
    }

    private void Walk(int axis)
    {
        if (axis != 0) Facing = Math.Sign(axis);
        VX = axis * Cur.MoveSpeed;
    }

    private void StickToFloor(float dt) => VY = Cur.Gravity * dt;

    private void ApplyAirGravity(float dt)
    {
        FormParams p = AirProfile;
        if (p.Style == JumpStyle.ConstantDescent && VY >= 0f) VY = p.DescentSpeed;
        else VY = Math.Min(VY + p.Gravity * dt, p.TerminalSpeed);
    }

    private bool CanUseWall => HasWallClimb && OnWall && !(Cfg.NoClimbRule && TouchNoClimb);

    private bool CanStartWallClimb() =>
        CanUseWall && _in.Up && _in.Axis != 0 && _in.Axis * WallNormalX < 0f;

    // ---------- 状态切换（Enter 里的逻辑） ----------

    private void EnterGround(SimState s)
    {
        State = s;
        JumpProfile = null;
        _coyote = 0f;
        if (OnFloor && ConsumeJump()) EnterWindup(false);
    }

    private void EnterWindup(bool wall)
    {
        State = SimState.Windup;
        _windupProfile = Cur;
        _windupRemaining = _windupProfile.JumpWindup;
        _windupWall = wall;
        _coyote = 0f;
        _jumpAge = -1f;
        if (wall) EverWallJumped = true;
    }

    private void ToFreeState()
    {
        if (!OnFloor) State = SimState.Fall;
        else if (_in.Axis != 0) EnterGround(SimState.Run);
        else EnterGround(SimState.Idle);
    }

    // ---------- PhysicsUpdate ----------

    private void PhysicsUpdate(float dt)
    {
        switch (State)
        {
            case SimState.Idle:
            case SimState.Run:
            {
                if (ConsumeJump())
                {
                    EnterWindup(false);
                    return;
                }
                int axis = _in.Axis;
                Walk(axis);
                StickToFloor(dt);
                if (axis != 0 && State == SimState.Idle) EnterGround(SimState.Run);
                else if (axis == 0 && State == SimState.Run) EnterGround(SimState.Idle);
                return;
            }
            case SimState.Windup:
            {
                VX = 0f;
                if (_windupWall) VY = 0f;
                else if (OnFloor) StickToFloor(dt);
                else ApplyAirGravity(dt);
                _windupRemaining -= dt;
                if (_windupRemaining > 0.000001f) return;
                JumpProfile = _windupProfile;
                VY = -_windupProfile.JumpSpeed;
                State = SimState.Rise;
                return;
            }
            case SimState.Rise:
            case SimState.Fall:
            {
                if (TryAirJump()) return;
                if (CanStartWallClimb())
                {
                    State = SimState.WallClimb;
                    EverClimbed = true;
                    return;
                }
                Walk(_in.Axis);
                ApplyAirGravity(dt);
                return;
            }
            case SimState.WallClimb:
            {
                if (Cfg.NoClimbRule && TouchNoClimb)
                {
                    State = SimState.Fall;
                    return;
                }
                if (ConsumeJump())
                {
                    EnterWindup(true);
                    return;
                }
                int axis = _in.Axis;
                if (!_in.Up || axis == 0 || !HasWallClimb)
                {
                    State = SimState.Fall;
                    return;
                }
                Facing = Math.Sign(axis);
                VX = axis * Cur.MoveSpeed;
                VY = -Cur.WallClimbSpeed;
                return;
            }
        }
    }

    private bool TryAirJump()
    {
        if (State == SimState.Fall && _coyote > 0f && ConsumeJump())
        {
            EnterWindup(false);
            return true;
        }
        if (!CanUseWall) return false;
        if (!ConsumeJump()) return false;
        EnterWindup(true);
        return true;
    }

    // ---------- AfterMove ----------

    private void AfterMove()
    {
        switch (State)
        {
            case SimState.Idle:
            case SimState.Run:
                if (OnFloor) return;
                _coyote = Math.Max(0f, Cfg.CoyoteTime);
                State = SimState.Fall;
                return;
            case SimState.Rise:
            case SimState.Fall:
                if (OnFloor)
                {
                    JumpProfile = null;
                    ToFreeState();
                    return;
                }
                if (OnCeiling && VY < 0f) VY = 0f;
                if (State == SimState.Rise && VY >= 0f) State = SimState.Fall;
                return;
            case SimState.WallClimb:
                if (OnFloor && VY >= 0f) ToFreeState();
                else if (!OnWall) State = SimState.Fall;
                return;
        }
    }

    // ---------- MoveAndSlide ----------

    private void MoveAndSlide(float dt)
    {
        bool wasOnFloor = OnFloor;
        OnFloor = OnWall = OnCeiling = false;
        TouchNoClimb = false;
        bool velUp = VY < 0f;
        double mx = VX * dt, my = VY * dt;
        double motionX0 = mx;
        double firstNx = 0, firstNy = 0;
        bool anyHit = false;
        // 帧开始时贴着的墙（Godot 的 MoveAndSlide 把安全边距内的接触也算作碰撞：recovery_as_collision）
        int touchingBefore = TouchingWall(1) ? 1 : TouchingWall(-1) ? -1 : 0;

        for (int iter = 0; iter < MaxSlides; iter++)
        {
            if (Math.Abs(mx) < 1e-9 && Math.Abs(my) < 1e-9) break;
            if (!Sweep(mx, my, out double t, out double nx, out double ny, out SimTile tile))
            {
                X += mx;
                Y += my;
                break;
            }
            X += mx * t;
            Y += my * t;
            // 贴到接触面上，避免浮点误差积累成穿透
            if (nx < 0) X = tile.X0 - Cfg.HalfW;
            else if (nx > 0) X = tile.X1 + Cfg.HalfW;
            if (ny < 0) Y = tile.Y0 - Cfg.HalfH;
            else if (ny > 0) Y = tile.Y1 + Cfg.HalfH;

            if (!anyHit)
            {
                anyHit = true;
                firstNx = nx;
                firstNy = ny;
            }
            if (ny < 0) OnFloor = true;
            else if (ny > 0) OnCeiling = true;
            else
            {
                OnWall = true;
                WallNormalX = (float)nx;
            }

            // floor_stop_on_slope：速度正对着地面（纯竖直向下）时整个速度清零、停止滑动
            if (ny < 0 && VX == 0f && VY > 0f)
            {
                VX = 0f;
                VY = 0f;
                break;
            }
            double rx = mx * (1 - t), ry = my * (1 - t);
            double dot = rx * nx + ry * ny;
            rx -= nx * dot;
            ry -= ny * dot;
            if (rx * VX + ry * VY <= 0) rx = ry = 0;
            if (ny > 0 && velUp) VY = 0f; // 撞顶：速度沿顶面滑动，竖直分量清零
            mx = rx;
            my = ry;
        }

        // 贴地：上一帧在地上、这一帧没往上走，往下探 FloorSnapLength
        if (!OnFloor && wasOnFloor && !velUp)
        {
            double snap = Math.Max(Cfg.FloorSnapLength, 0.08f);
            if (Sweep(0, snap, out double t, out _, out double ny, out SimTile tile) && ny < 0)
            {
                Y = tile.Y0 - Cfg.HalfH;
                OnFloor = true;
            }
        }

        // 帧开始和结束都贴着同一面墙（没有朝墙运动也算）：仍然在墙上
        if (!OnWall && touchingBefore != 0 && TouchingWall(touchingBefore))
        {
            OnWall = true;
            WallNormalX = -touchingBefore;
        }

        // 只贴着墙（不在地上、不在顶上）且第一次碰撞迎着运动方向：水平速度清零
        if (OnWall && !OnFloor && !OnCeiling && anyHit && firstNy == 0 && motionX0 * firstNx < 0) VX = 0f;
        // 站在地上：清掉竖直速度（重力不累积）
        if (OnFloor && !velUp) VY = 0f;

        if (OnWall) TouchNoClimb = WallHasNoClimb();
    }

    /// <summary>side 一侧（1 右、-1 左）是否紧贴着挡路的瓦片（竖直方向有重叠）。</summary>
    private bool TouchingWall(int side)
    {
        double edge = side > 0 ? X + Cfg.HalfW : X - Cfg.HalfW;
        double y0 = Y - Cfg.HalfH, y1 = Y + Cfg.HalfH;
        _tiles.Clear();
        World.Query(edge - 1, y0, edge + 1, y1, _tiles);
        foreach (SimTile t in _tiles)
        {
            if (t.Kind is TileKind.Empty or TileKind.OneWay) continue;
            double face = side > 0 ? t.X0 : t.X1;
            if (Math.Abs(face - edge) > 0.01) continue;
            if (t.Y1 <= y0 + Eps || t.Y0 >= y1 - Eps) continue;
            return true;
        }
        return false;
    }

    /// <summary>墙那一侧紧贴着的瓦片里有没有不可攀的。</summary>
    private bool WallHasNoClimb()
    {
        double edge = WallNormalX < 0 ? X + Cfg.HalfW : X - Cfg.HalfW;
        double y0 = Y - Cfg.HalfH, y1 = Y + Cfg.HalfH;
        _tiles.Clear();
        World.Query(edge - 1, y0, edge + 1, y1, _tiles);
        foreach (SimTile t in _tiles)
        {
            double face = WallNormalX < 0 ? t.X0 : t.X1;
            if (Math.Abs(face - edge) > 0.5) continue;
            if (t.Y1 <= y0 + Eps || t.Y0 >= y1 - Eps) continue;
            if (t.Kind == TileKind.NoClimb) return true;
        }
        return false;
    }

    /// <summary>扫掠：找最早碰到的瓦片。t ∈ [0,1]，法线是轴向单位向量。同一时刻优先竖直方向（地面 / 顶）。</summary>
    private bool Sweep(double mx, double my, out double bestT, out double bestNx, out double bestNy, out SimTile bestTile)
    {
        double hw = Cfg.HalfW, hh = Cfg.HalfH;
        double bx0 = X - hw, bx1 = X + hw, by0 = Y - hh, by1 = Y + hh;
        _tiles.Clear();
        World.Query(Math.Min(bx0, bx0 + mx) - 1, Math.Min(by0, by0 + my) - 1, Math.Max(bx1, bx1 + mx) + 1, Math.Max(by1, by1 + my) + 1, _tiles);
        bestT = double.MaxValue;
        bestNx = bestNy = 0;
        bestTile = default;
        bool found = false;
        double len = Math.Sqrt(mx * mx + my * my);

        foreach (SimTile tile in _tiles)
        {
            double t, nx = 0, ny = 0;
            if (tile.Kind == TileKind.OneWay)
            {
                // 单向平台：只挡往下走、而且起点在平台顶面上方（含余量）的身体
                if (my <= 0) continue;
                if (by1 > tile.Y0 + Cfg.OneWayMargin) continue;
                t = Math.Max(0, (tile.Y0 - by1) / my);
                if (t > 1) continue;
                double ax0 = bx0 + mx * t, ax1 = bx1 + mx * t;
                if (!(ax1 > tile.X0 + Eps && ax0 < tile.X1 - Eps)) continue;
                ny = -1;
            }
            else
            {
                double txIn, txOut, tyIn, tyOut;
                if (mx > 0) { txIn = (tile.X0 - bx1) / mx; txOut = (tile.X1 - bx0) / mx; }
                else if (mx < 0) { txIn = (tile.X1 - bx0) / mx; txOut = (tile.X0 - bx1) / mx; }
                else
                {
                    if (!(bx1 > tile.X0 + Eps && bx0 < tile.X1 - Eps)) continue;
                    txIn = double.NegativeInfinity;
                    txOut = double.PositiveInfinity;
                }
                if (my > 0) { tyIn = (tile.Y0 - by1) / my; tyOut = (tile.Y1 - by0) / my; }
                else if (my < 0) { tyIn = (tile.Y1 - by0) / my; tyOut = (tile.Y0 - by1) / my; }
                else
                {
                    if (!(by1 > tile.Y0 + Eps && by0 < tile.Y1 - Eps)) continue;
                    tyIn = double.NegativeInfinity;
                    tyOut = double.PositiveInfinity;
                }
                double enter = Math.Max(txIn, tyIn), exit = Math.Min(txOut, tyOut);
                if (enter >= exit || enter > 1) continue;
                // 已经在内部（深度超过容差）的瓦片不算碰撞，避免卡死
                if (enter < 0 && -enter * len > 0.01) continue;
                t = Math.Max(0, enter);
                if (tyIn >= txIn - 1e-9) ny = my > 0 ? -1 : 1;
                else nx = mx > 0 ? -1 : 1;
                // 退化：沿着某个轴擦边（另一个轴没有运动）时，法线取有运动的那个轴
                if (ny != 0 && my == 0) { ny = 0; nx = mx > 0 ? -1 : 1; }
                if (nx != 0 && mx == 0) { nx = 0; ny = my > 0 ? -1 : 1; }
            }
            bool better = t < bestT - 1e-9 || (Math.Abs(t - bestT) <= 1e-9 && ny != 0 && bestNy == 0);
            if (!better) continue;
            bestT = t;
            bestNx = nx;
            bestNy = ny;
            bestTile = tile;
            found = true;
        }
        return found;
    }
}

/// <summary>脚本条件看到的观察值（模拟和真实玩家都按同一套定义给出）。</summary>
public readonly struct SimObservation
{
    /// <summary>在地面自由状态（Idle / Run）。</summary>
    public readonly bool Grounded;
    /// <summary>在攀墙状态。</summary>
    public readonly bool Climbing;
    /// <summary>不在地上（IsOnFloor 为假）。</summary>
    public readonly bool Airborne;

    public SimObservation(bool grounded, bool climbing, bool airborne)
    {
        Grounded = grounded;
        Climbing = climbing;
        Airborne = airborne;
    }
}
