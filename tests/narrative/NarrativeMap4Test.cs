using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 地图四“七道门”的真实流程测试（计划_地图四关卡按原画重做）：正式入口开新游戏，真实按键。
/// ① 七块泥板都没读时，走到通往回廊的出口：实体门挡住、显示锁着的提示（不带按键）；
/// ② 从塔顶开始一层一层往下：先走到这一层的活板门上（关着，人不掉下去、显示提示），回头读泥板，门当场打开、提示消失，再走上去落到下一层；
/// ③ 塔底读第七块泥板后，出口门打开：真实玩家从出口平台走进回廊 S4_03，再落进祭坛 S4_04、走进终末之室 S4_05。全程零报错。
/// </summary>
public partial class NarrativeMap4Test : NarrativeFlowBase
{
    protected override string SuiteName => "narrative_map4";

    private const string TabletPrompt = "拾取泥板";
    /// <summary>七层：地面行、去泥板的方向（活板门在泥板再往前）。</summary>
    private static readonly (int Row, bool Left)[] Floors = { (14, true), (28, false), (42, true), (56, false), (70, true), (89, false), (100, true) };
    private const int Shift = 16;

    protected override async Task RunChecks()
    {
        Listen();
        try
        {
            GD.PushError("narrative_map4 日志钩子自检");
            await Frames(1);
            Check(Errors.Count == 1, "前提：测试的日志钩子能收到报错（自检，不计入后面的零报错）");
            int errorBase = Errors.Count;
            int narrativeErrors = NarrativeLog.ErrorCount;

            bool started = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            Check(started && Narr != null && Interactor != null, "正式入口开新游戏");
            if (Narr == null || Interactor == null) return;
            await WaitSettled();

            await LockedExit();
            await Teleport("S4_02", SpawnPoint.DefaultId);
            Check(World.CurrentRoomDef?.Id == "S4_02" && Mathf.Abs(LocalY - FloorTop(14)) <= 2, $"传送到七道门塔顶第一层（局部 y {LocalY:0}）");
            for (int k = 1; k <= 7; k++)
                if (!await FloorChecks(k)) break;
            await OpenExit();

            Check(Errors.Count == errorBase && NarrativeLog.ErrorCount == narrativeErrors,
                "全程零报错" + (Errors.Count > errorBase ? "：" + string.Join(" | ", Errors.Snapshot().Skip(errorBase).Take(5)) : ""));
            Check(NoPauseOrLock() && !Narr.MainActive, "最后没有剧情在播，没有暂停和输入锁残留");
        }
        finally
        {
            Unlisten();
            ReleaseInputs();
        }
    }

    /// <summary>脚底的局部 y（玩家节点在身体中心，身体高 48）。</summary>
    private float LocalY => Player.GlobalPosition.Y - World.CurrentRoom.GlobalPosition.Y + 24f;
    private static float FloorTop(int row) => row * 32 - Shift;
    private static string HintOf(GateBarrier gate) => IInteractable.NoKeyMark + gate.LockedText;
    private GateBarrier Gate(string name) => World.CurrentRoom.GetNodeOrNull<GateBarrier>("Objects/" + name);
    private static readonly string[] HatchNames = { "Gate_C", "Gate_D", "Gate_E", "Gate_F", "Gate_G", "Gate_I" };

    /// <summary>① 一块都没读：出口门挡住、有提示。</summary>
    private async Task LockedExit()
    {
        await Teleport("S4_02", "From_S4_03");
        GateBarrier exit = Gate("GateToS4_03");
        bool hinted = await HoldUntil(PlayerActions.Right, () => exit != null && Interactor.CurrentPrompt == HintOf(exit), 120);
        await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id != "S4_02", 90);
        Check(exit != null && !exit.IsOpen && World.CurrentRoomDef?.Id == "S4_02" && LocalX < 2304 - 32 && hinted,
            $"一块泥板都没读：按住右键走向回廊出口，被实体门挡住（{World.CurrentRoomDef?.Id} 局部 x {LocalX:0}），提示“{exit?.LockedText}”（不带按键；实际提示“{Interactor.CurrentPrompt}”）");
    }

    /// <summary>② 第 k 层：活板门关着时站上去不掉、有提示；读泥板后门开、提示消失，走上去落到下一层。</summary>
    private async Task<bool> FloorChecks(int k)
    {
        (int row, bool left) = Floors[k - 1];
        StringName toward = left ? PlayerActions.Left : PlayerActions.Right;
        StringName back = left ? PlayerActions.Right : PlayerActions.Left;
        GateBarrier hatch = k <= 6 ? Gate(HatchNames[k - 1]) : null;
        if (hatch != null)
        {
            bool hinted = await HoldUntil(toward, () => Interactor.CurrentPrompt == HintOf(hatch), 400);
            await HoldUntil(toward, () => LocalY > FloorTop(row) + 64, 40);
            await Frames(10);
            Check(hinted && !hatch.IsOpen && Mathf.Abs(LocalY - FloorTop(row)) <= 2 && Player.IsOnFloor(),
                $"第 {k} 层：没读泥板时走到活板门 {hatch.Name} 上，门关着、人不掉下去（局部 y {LocalY:0}），提示“{hatch.LockedText}”");
        }
        bool atTablet = await WalkToPrompt(TabletPrompt, hatch != null ? back : toward);
        await KeyTap(Key.E);
        bool open = await WaitUntil(() => Narr.Tablet.IsOpen, 60);
        string title = Narr.Tablet.CurrentTitle;
        bool closed = await AdvanceUntil(() => !Narr.MainActive, 10);
        await WaitUntil(() => !PauseService.IsPaused, 60);
        await Frames(3);
        string[] numerals = { "一", "二", "三", "四", "五", "六", "七" };
        Check(atTablet && open && closed && title == numerals[k - 1] && GameSession.Progress.Flag($"tablet.gate_{k}"),
            $"第 {k} 层：走到泥板「{numerals[k - 1]}」旁按 E 读完（{title}），tablet.gate_{k} 成立");
        if (hatch == null) return atTablet && open;

        await Frames(2);
        Check(hatch.IsOpen && hatch.CollisionLayer == 0 && !hatch.Visible && Interactor.CurrentPrompt != HintOf(hatch),
            $"第 {k} 层：读完后活板门 {hatch.Name} 当场打开（没有碰撞、隐藏），提示消失");
        int nextRow = Floors[k].Row;
        bool fell = await HoldUntil(toward, () => LocalY > FloorTop(row) + 64, 400);
        bool landed = await WaitUntil(() => Player.IsOnFloor() && Mathf.Abs(Player.Velocity.Y) < 1f, 300);
        Check(fell && landed && Mathf.Abs(LocalY - FloorTop(nextRow)) <= 2 && World.CurrentRoomDef?.Id == "S4_02",
            $"第 {k} 层：走上打开的活板门，落到第 {k + 1} 层（局部 y {LocalY:0}，地面 {FloorTop(nextRow)}）");
        return fell && landed;
    }

    /// <summary>③ 七块读完：出口门打开，真实玩家走进回廊，再落进祭坛、走进终末之室。</summary>
    private async Task OpenExit()
    {
        await Teleport("S4_02", "From_S4_03");
        GateBarrier exit = Gate("GateToS4_03");
        Check(exit != null && exit.IsOpen && exit.CollisionLayer == 0 && Interactor.CurrentPrompt != HintOf(exit),
            "七块泥板都读完：出口门打开（没有碰撞），没有锁着的提示");
        bool corridor = await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id == "S4_03", 300);
        Check(corridor, "真实玩家按住右键从出口平台走进血池回廊 S4_03");
        bool altar = corridor && await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id == "S4_04", 600);
        Check(altar, "回廊往右走，从中间的洞落进心室祭坛 S4_04");
        await WaitUntil(() => Player.IsOnFloor(), 300);
        bool end = altar && await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id == "S4_05", 600);
        Check(end, "祭坛往右走进终末之室 S4_05");
    }
}
