using Godot;
using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>
/// 地面移动。脚步声按真正走过的距离算：在地上每走 StepDistance 像素响一步（水 360 速约每秒 4 步，泥 280 约每秒 3 步）。
/// 距离用这一帧移动后的实际速度（GetRealVelocity）× 时间：贴墙跑走不动不算，换房、复活、过门这类直接改位置的也不算。
/// 停下（回 Idle）、离地都不响；每次开始跑先走半步就响第一声。
/// </summary>
public partial class RunState : GroundState
{
    /// <summary>每走多少像素响一步。</summary>
    public const float StepDistance = 90f;

    /// <summary>距离上一步走了多少像素。</summary>
    public float StepTravel { get; private set; }
    /// <summary>响过的脚步数（测试用）。</summary>
    public int Steps { get; private set; }

    public override string AnimName => "run";
    public override string DisplayName => "移动";

    public override void Enter(StateMsg msg)
    {
        StepTravel = StepDistance * 0.5f;
        base.Enter(msg);
    }

    public override void AfterMove(double delta)
    {
        if (P.IsOnFloor())
        {
            StepTravel += Mathf.Abs(P.GetRealVelocity().X) * (float)delta;
            if (StepTravel >= StepDistance)
            {
                StepTravel = Mathf.Min(StepTravel - StepDistance, StepDistance);
                Steps++;
                SoundBank.Play(SoundBank.PlayerFootstep);
            }
        }
        base.AfterMove(delta);
    }
}
