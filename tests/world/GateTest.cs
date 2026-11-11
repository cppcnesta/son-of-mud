using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>条件门：S1_05 → S1_06（boss_keeper）。条件不满足时挡住；写入变量后立刻打开、替身能走过去。</summary>
public partial class GateTest : WorldTestBase
{
    protected override string SuiteName => "gate_test";

    protected override async Task RunChecks()
    {
        await SetUpGame();
        World.LoadRoomImmediate("S1_05", "From_S1_06");
        await Frames(20);
        GateBarrier gate = FindChild<GateBarrier>(World.CurrentRoom);
        Check(gate != null && gate.Requirement != null && gate.Requirement.Flags.Contains("boss_keeper"), "S1_05 的右出口有条件门，条件是 boss_keeper");
        if (gate == null) return;
        Check(!gate.IsOpen && gate.Visible && gate.CollisionLayer == Layers.World, "条件不满足：门关着（有碰撞、可见）");
        Check(gate.LockedText != "", $"门有锁住时的提示文字（{gate.LockedText}）");
        Check(Mover.IsOnFloor(), "替身站在门左边的出口平台上");

        float gateLeft = gate.GlobalPosition.X - BuildWorldTile / 2f;
        Mover.MoveDir = 1f;
        await Frames(60);
        float blockedX = Mover.GlobalPosition.X;
        Check(blockedX + TestMover.BodySize.X / 2f <= gateLeft + 1f && World.CurrentRoomDef.Id == "S1_05", $"条件不满足时被挡住（替身右边缘 {blockedX + TestMover.BodySize.X / 2f}，门左边 {gateLeft}）");

        GameSession.Progress.SetVar("boss_keeper", true);
        Check(gate.IsOpen && !gate.Visible && gate.CollisionLayer == 0, "写入 boss_keeper=true 后门立刻打开（同一帧，无需等待）");
        bool passed = await WaitUntil(() => World.CurrentRoomDef.Id == "S1_06", 120);
        Mover.MoveDir = 0f;
        Check(passed, "门打开后替身走过去，进入 S1_06");

        // 已满足条件时进入房间，门一开始就是开的
        World.LoadRoomImmediate("S1_05", "Default");
        await Frames(2);
        GateBarrier again = FindChild<GateBarrier>(World.CurrentRoom);
        Check(again != null && again.IsOpen && again.CollisionLayer == 0, "条件已满足时重新进入 S1_05，门直接是开的");

        // 变量改回 false，门重新关上（门跟着进度走，不自己记状态）
        GameSession.Progress.SetVar("boss_keeper", false);
        Check(again != null && !again.IsOpen && again.CollisionLayer == Layers.World, "变量改回 false 时门重新关上");

        // 能力门不放实体门（靠地形，物理验证留到做关卡几何时）
        World.LoadRoomImmediate("S1_04", "Default");
        await Frames(2);
        Check(FindChild<GateBarrier>(World.CurrentRoom) == null, "S1_04↑S1_08 的攀墙门没有实体门（物理门本轮未验证）");

        await TearDownGame();
    }

    private const float BuildWorldTile = 64f;
}
