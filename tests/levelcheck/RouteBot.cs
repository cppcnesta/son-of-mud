using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.Tools.LevelCheck;

/// <summary>
/// 路线机器人：把静态检查算出的路线（RouteStep 列表）用真实按键（RealKeys，经过 InputMap）在真实游戏里按出来。
/// 每一步：确认在对的房间 → 切到要求的形态 → 走到起点 → 按脚本做动作 → 检查是否到了目标段（或换了房间）。
/// 脚本由 ScriptRunner 逐帧给出输入，和静态模拟器用同一个运行器，条件（落地、攀上墙、离地）的反应时机一致。
/// 房间信息由调用方提供：测试场里是固定的一个房间；在正式游戏里接 WorldGame 的当前房间。
/// </summary>
public sealed class RouteBot
{
    /// <summary>上（move_up）的键位：W（project.godot 里的 physical_keycode 87）。RealKeys 里没有这个键。</summary>
    public const Key UpKey = Key.W;

    private readonly Node _host;
    private readonly Func<PlayerCharacter> _player;
    private readonly Func<(string id, Vector2 origin)> _room;
    private int _axis;
    private bool _up;
    private bool _jump;

    public List<string> Log { get; } = new();

    public RouteBot(Node host, Func<PlayerCharacter> player, Func<(string id, Vector2 origin)> room)
    {
        _host = host;
        _player = player;
        _room = room;
    }

    private PlayerCharacter P => _player();

    public async Task Frame() => await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.PhysicsFrame);

    public async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    public static SimObservation Observe(PlayerCharacter p) =>
        new(p.State is GroundState, p.State is WallClimbState, !p.IsOnFloor());

    // ---------- 按键 ----------

    public void Apply(SimInput input)
    {
        if (input.Axis != _axis)
        {
            if (_axis != 0) RealKeys.Up(RealKeys.Toward(_axis));
            if (input.Axis != 0) RealKeys.Down(RealKeys.Toward(input.Axis));
            _axis = input.Axis;
        }
        if (input.Up != _up)
        {
            if (input.Up) RealKeys.Down(UpKey);
            else RealKeys.Up(UpKey);
            _up = input.Up;
        }
        if (input.Jump)
        {
            if (_jump) RealKeys.Up(RealKeys.Jump);
            RealKeys.Down(RealKeys.Jump);
            _jump = true;
        }
        else if (_jump)
        {
            RealKeys.Up(RealKeys.Jump);
            _jump = false;
        }
    }

    public void ReleaseAll()
    {
        Apply(default);
        RealKeys.ReleaseAll();
        RealKeys.Up(UpKey);
        _axis = 0;
        _up = _jump = false;
    }

    // ---------- 跑一段脚本 ----------

    public sealed class Recording
    {
        /// <summary>脚本开始前的位置（全局）。</summary>
        public Vector2 Start;
        /// <summary>第 j 帧（脚本帧）结束后的位置（全局），和 SimRun.Path 一一对应。</summary>
        public List<Vector2> Path = new();
        public List<string> States = new();
        public bool Failed;
        public int Frames;
    }

    /// <summary>
    /// 跑脚本并逐帧记录。在第 i 次恢复时（物理帧开头）读到的是上一帧结束的状态、按下的键下一帧才生效，
    /// 所以第 j 个脚本帧结束后的位置在第 j + 1 + Delay 次恢复时读到。
    /// </summary>
    public async Task<Recording> Run(InputScript script)
    {
        int delay = ScriptRunner.ObservationDelay;
        var runner = new ScriptRunner(script, delay);
        var raw = new List<Vector2>();
        var states = new List<string>();
        var rec = new Recording();
        int doneAt = -1;
        for (int i = 0; i < 3000; i++)
        {
            PlayerCharacter p = P;
            raw.Add(p.GlobalPosition);
            states.Add(p.State?.GetType().Name ?? "");
            if (doneAt < 0)
            {
                SimInput input = runner.Next(Observe(p));
                if (runner.Done)
                {
                    doneAt = i;
                    Apply(default);
                }
                else Apply(input);
            }
            if (doneAt >= 0 && i >= doneAt + delay) break;
            await Frame();
        }
        if (doneAt < 0) doneAt = raw.Count - 1;
        rec.Failed = runner.Failed || !runner.Done;
        rec.Frames = doneAt;
        rec.Start = raw[0];
        for (int j = 0; j < doneAt; j++)
        {
            int k = j + 1 + delay;
            if (k >= raw.Count) break;
            rec.Path.Add(raw[k]);
            rec.States.Add(states[k]);
        }
        return rec;
    }

    // ---------- 走到起点 ----------

    /// <summary>
    /// 走到房间局部 x（误差不超过半步：水 3 像素、泥 2.3 像素）。没有加速度，按住 n 帧就正好走 n 步，
    /// 所以按距离算好帧数一次走完，再看一眼，不对就补一次。wallSide 不为 0 时多按两帧，顶到墙为止。
    /// </summary>
    public async Task<bool> WalkTo(float localX, int wallSide = 0)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Frames(2);
            PlayerCharacter p = P;
            float target = _room().origin.X + localX;
            float step = p.Forms.Current.MoveSpeed / Engine.PhysicsTicksPerSecond;
            float dx = target - p.GlobalPosition.X;
            if (wallSide == 0 && Mathf.Abs(dx) <= step * 0.5f + 0.01f) return true;
            if (wallSide != 0 && Mathf.Abs(dx) <= 1f) return true;
            int dir = wallSide != 0 && Mathf.Abs(dx) <= step * 2f ? wallSide : Math.Sign(dx);
            int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(dx) / step)) + (wallSide != 0 ? 2 : 0);
            Apply(new SimInput(dir));
            await Frames(n);
            Apply(default);
            await Frames(2 + ScriptRunner.ObservationDelay);
        }
        return false;
    }

    // ---------- 按路线走 ----------

    public sealed class StepResult
    {
        public bool Ok;
        public string Message = "";
        public Vector2 End;
    }

    public async Task<StepResult> RunStep(RouteStep step)
    {
        var result = new StepResult();
        if (_room().id != step.Room)
        {
            result.Message = $"不在起始房间 {step.Room}（现在在 {_room().id}）";
            Log.Add($"{step.Text}：{result.Message}");
            return result;
        }
        if (P.Forms.Form != step.Form)
        {
            RealKeys.Down(RealKeys.SwitchForm);
            await Frame();
            RealKeys.Up(RealKeys.SwitchForm);
            await Frames(3);
            if (P.Forms.Form != step.Form)
            {
                result.Message = "切形态失败";
                Log.Add($"{step.Text}：{result.Message}");
                return result;
            }
        }
        if (step.Kind != EdgeKind.Break && !await WalkTo(step.StartX, step.WallSide))
        {
            result.Message = $"走不到起点 x={step.StartX}（现在 {P.GlobalPosition - _room().origin}，在地上 {P.IsOnFloor()}，状态 {P.Machine.Current?.Name}）";
            Log.Add($"{step.Text}：{result.Message}");
            return result;
        }
        switch (step.Kind)
        {
            case EdgeKind.Break:
            {
                await WalkTo(step.StartX - step.BreakDir * 6f);
                Apply(new SimInput(step.BreakDir));
                await Frame();
                Apply(default);
                await Frames(2);
                RealKeys.Down(RealKeys.Attack);
                await Frame();
                RealKeys.Up(RealKeys.Attack);
                await Frames(Mathf.CeilToInt(2f * Engine.PhysicsTicksPerSecond));
                result.Ok = true;
                result.Message = "已攻击（是否打碎由调用方检查）";
                break;
            }
            case EdgeKind.Door:
            {
                string before = _room().id;
                RealKeys.Down(RealKeys.Interact);
                await Frame();
                RealKeys.Up(RealKeys.Interact);
                for (int i = 0; i < 180 && _room().id == before; i++) await Frame();
                await Frames(10);
                result.Ok = _room().id == step.TargetRoom;
                result.Message = result.Ok ? "过门成功" : $"过门后在 {_room().id}，期望 {step.TargetRoom}";
                break;
            }
            default:
            {
                string before = _room().id;
                Recording rec = await Run(step.Script);
                await Frames(2);
                PlayerCharacter p = P;
                var (roomId, origin) = _room();
                Vector2 local = p.GlobalPosition - origin;
                result.End = local;
                float bottom = local.Y + 24f;
                if (step.Kind == EdgeKind.Exit)
                {
                    result.Ok = roomId != before && roomId == step.TargetRoom;
                    result.Message = result.Ok ? $"已换到 {roomId}" : $"没有换到 {step.TargetRoom}（在 {roomId}）";
                }
                else
                {
                    bool onTarget = roomId == step.TargetRoom && p.IsOnFloor() && Mathf.Abs(bottom - step.TargetFloorY) <= 4f &&
                                    local.X >= step.TargetMinX - 8f && local.X <= step.TargetMaxX + 8f;
                    result.Ok = onTarget && !rec.Failed;
                    result.Message = $"{(result.Ok ? "到达" : "没到")}目标段（{step.TargetRoom} 地面 y={step.TargetFloorY} x∈[{step.TargetMinX},{step.TargetMaxX}]），实际 {roomId} ({local.X:F1},{local.Y:F1})" +
                                     (rec.Failed ? "，脚本超时" : "");
                }
                break;
            }
        }
        Log.Add($"{step.Text}：{result.Message}");
        return result;
    }

    public async Task<(bool ok, int done)> RunRoute(IReadOnlyList<RouteStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            StepResult r = await RunStep(steps[i]);
            if (!r.Ok) return (false, i);
        }
        return (true, steps.Count);
    }
}
