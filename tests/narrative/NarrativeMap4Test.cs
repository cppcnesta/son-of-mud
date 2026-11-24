using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Player;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 地图四“七道门”的真实流程测试（按美术比例重做后，塔每层一间）：正式入口开新游戏，真实玩家、真实按键。
/// ① 七块泥板都没读时，在 ⑥ 层 S4_07 走向通往回廊的出口：实体门挡住、显示锁着的提示（不带按键）；
/// ② 从 S4_01 出发，静态模拟器在地图四的文字图上规划路线，路线机器人用真实按键一层一层往下走：
///    到这一层的泥板旁按 E 读完（这一层往下的门当场打开），再走到下一层；②③ 在同一间 S4_0304；
/// ③ 塔底 ⑦ 读完后沿台阶爬回 ⑥，出口门打开，过桥进血池回廊 S4_09、落进心室祭坛 S4_10、走进终末之室 S4_11。
/// 路上只靠走边缘、落洞换房（不发 PlayerTeleported），玩家没死，全程零报错。
/// </summary>
public partial class NarrativeMap4Test : EnemyFlowBase
{
    protected override string SuiteName => "narrative_map4";
    protected override string ResultPrefix => "narrative";

    private const string TabletPrompt = "拾取泥板";
    private static readonly string[] Numerals = { "一", "二", "三", "四", "五", "六", "七" };

    private SimConfig _cfg;
    private LevelWorld _world;
    private RouteBot _bot;
    private int _teleports;
    private readonly List<string> _entered = new();
    private void OnRoomEntered(string id) => _entered.Add(id);

    protected override async Task RunChecks()
    {
        ListenEnemy();
        Callable onTeleport = Callable.From<StringName>(_ => _teleports++);
        try
        {
            await Run(onTeleport);
        }
        finally
        {
            if (Events.Instance.IsConnected(Events.SignalName.PlayerTeleported, onTeleport))
                Events.Instance.Disconnect(Events.SignalName.PlayerTeleported, onTeleport);
            Events.Instance.RoomEntered -= OnRoomEntered;
            _bot?.ReleaseAll();
            UnlistenEnemy();
            ReleaseInputs();
        }
    }

    private Vector2 RealOrigin => World.CurrentRoom.GlobalPosition;
    private Vector2 SimCenter => _world.Find(RoomId).Origin + (Player.GlobalPosition - RealOrigin);
    private static string HintOf(GateBarrier gate) => IInteractable.NoKeyMark + gate.LockedText;
    private GateBarrier GateFor(string flag) =>
        InRoom<GateBarrier>().FirstOrDefault(g => g.Requirement != null && g.Requirement.Flags.Count == 1 && g.Requirement.Flags.Contains(flag));
    private GateBarrier CorridorGate => InRoom<GateBarrier>().FirstOrDefault(g => g.Requirement != null && g.Requirement.Flags.Count == 7);

    private async Task Run(Callable onTeleport)
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
        // 这里测地图四的地形和门；“七道门”“塔顶”自动播的剧情当作已经看过（剧情本身由 narrative_story 测）
        GameSession.Progress.SetVar("seen.h_tower.七道门", true);
        GameSession.Progress.SetVar("seen.h_tower.塔顶", true);

        await LockedExit();

        _cfg = SimConfig.Load();
        _world = new LevelWorld(LevelWorld.DefaultCellSize);
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        foreach (string id in Map4CheckTest.Rooms)
        {
            RoomGrid grid = RoomGrid.Load(layout.Find(id), (Vector2I)LevelWorld.DefaultCellSize, out string error);
            Check(grid != null, $"{id} 的文字图能读到{(grid == null ? "：" + error : "")}");
            if (grid == null) return;
            _world.Add(layout.Find(id), grid);
        }
        _bot = new RouteBot(this, () => Player, () => (RoomId, RealOrigin));

        await Teleport("S4_01", "From_S3_08");
        await WaitUntil(() => Player.IsOnFloor() && PlayerFree, 120);
        Check(RoomId == "S4_01", "从 S3_08 的门进地图四：在 S4_01 心室入口");
        Events.Instance.Connect(Events.SignalName.PlayerTeleported, onTeleport);
        Events.Instance.RoomEntered += OnRoomEntered;
        var state = new LevelState();

        for (int k = 1; k <= 7; k++)
        {
            string room = Map4CheckTest.TabletRoom[k];
            if (!await WalkTo($"走到泥板「{Numerals[k - 1]}」（{room}）", state, room, $"Tablet_gate_{k}")) return;
            GateBarrier hatch = k <= 6 ? GateFor($"tablet.gate_{k}") : null;
            if (k <= 6)
                Check(hatch != null && !hatch.IsOpen, $"第 {k} 块：读之前 {room} 里这一层的门 {hatch?.Name} 关着");
            if (!await ReadTablet(k)) return;
            state.Flags.Add($"tablet.gate_{k}");
            if (hatch != null)
            {
                await Frames(2);
                Check(hatch.IsOpen && hatch.CollisionLayer == 0 && !hatch.Visible, $"第 {k} 块：读完后门 {hatch.Name} 当场打开（没有碰撞、隐藏）");
            }
        }

        if (!await WalkTo("⑦ 读完：爬回 ⑥ 层，过桥进回廊，落进祭坛，走进终末之室", state, "S4_11", null)) return;
        Check(_entered.SkipWhile(r => r != "S4_09").Take(3).SequenceEqual(new[] { "S4_09", "S4_10", "S4_11" }),
            $"最后一段依次进入血池回廊 S4_09、心室祭坛 S4_10、终末之室 S4_11（进入顺序 {string.Join("→", _entered)}）");
        await Frames(10);
        Check(RoomId == "S4_11" && Deaths == 0, $"一路走到终末之室 S4_11，玩家没死（死亡 {Deaths} 次）");
        Check(_teleports == 0, $"从 S4_01 起全程只靠走边缘、落洞换房，没有传送（PlayerTeleported {_teleports} 次）");

        Check(Errors.Count == errorBase && NarrativeLog.ErrorCount == narrativeErrors,
            "全程零报错" + (Errors.Count > errorBase ? "：" + string.Join(" | ", Errors.Snapshot().Skip(errorBase).Take(5)) : ""));
        Check(NoPauseOrLock() && !Narr.MainActive, "最后没有剧情在播，没有暂停和输入锁残留");
    }

    /// <summary>① 一块都没读：⑥ 层的回廊出口门挡住、有提示。</summary>
    private async Task LockedExit()
    {
        await Teleport("S4_07", "From_S4_09");
        GateBarrier exit = CorridorGate;
        bool hinted = await HoldUntil(PlayerActions.Right, () => exit != null && Interactor.CurrentPrompt == HintOf(exit), 120);
        await HoldUntil(PlayerActions.Right, () => RoomId != "S4_07", 90);
        Check(exit != null && !exit.IsOpen && RoomId == "S4_07" && LocalX < 1728 - 32 && hinted,
            $"一块泥板都没读：在 S4_07 按住右键走向回廊出口，被实体门挡住（{RoomId} 局部 x {LocalX:0}），提示“{exit?.LockedText}”（不带按键；实际提示“{Interactor.CurrentPrompt}”）");
    }

    /// <summary>用模拟器规划从当前位置到目标（房间里的锚点，没写锚点就是房间的默认出生点）的路线，再用真实按键走过去。</summary>
    private async Task<bool> WalkTo(string phase, LevelState state, string roomId, string anchor)
    {
        var graph = new MoveGraph(_cfg, _world, state);
        Segment here = graph.FindSegment(_world.Find(RoomId), SimCenter);
        LevelRoom room = _world.Find(roomId);
        char mark = anchor == null ? RoomGrid.DefaultSpawn : room.Grid.Anchors.First(a => a.Value == anchor).Key;
        Vector2I cell = room.Grid.Find(mark).Value;
        Segment target = graph.FindSegment(room, room.FeetGlobal(cell.X, cell.Y) - new Vector2(0, _cfg.HalfH));
        Check(here != null && target != null, $"{phase}：模拟器找得到起点（{RoomId} 局部 {Player.GlobalPosition - RealOrigin}）和目标的地面");
        if (here == null || target == null) return false;
        int form = Player.Forms.Form;
        List<MoveEdge> edges = graph.Explore(new[] { here }, e => e.Form == form).RouteTo(target);
        if (edges == null || edges.Count == 0)
        {
            if (here == target) return true;
            Check(false, $"{phase}：模拟器没有找到路线");
            return false;
        }
        List<RouteStep> steps = RoutePlan.From(edges);
        _bot.Log.Clear();
        var (ok, done) = await _bot.RunRoute(steps);
        foreach (string line in _bot.Log) GD.Print("    " + line);
        Check(ok && RoomId == roomId, $"{phase}：路线机器人用真实按键走完 {done}/{steps.Count} 步（{edges.Count(e => e.Kind == EdgeKind.Exit)} 次换房），现在在 {RoomId}");
        return ok && RoomId == roomId;
    }

    /// <summary>走到泥板旁（往泥板那边走到出现拾取提示），按 E 读完。</summary>
    private async Task<bool> ReadTablet(int k)
    {
        Node2D tablet = World.CurrentRoom.GetNodeOrNull<Node2D>($"Objects/Tablet_gate_{k}");
        StringName toward = tablet != null && tablet.GlobalPosition.X < Player.GlobalPosition.X ? PlayerActions.Left : PlayerActions.Right;
        bool atTablet = Interactor.CurrentPrompt?.Contains(TabletPrompt) == true || await WalkToPrompt(TabletPrompt, toward);
        await KeyTap(Key.E);
        bool open = await WaitUntil(() => Narr.Tablet.IsOpen, 60);
        string title = Narr.Tablet.CurrentTitle;
        bool closed = await AdvanceUntil(() => !Narr.MainActive, 10);
        await WaitUntil(() => !PauseService.IsPaused, 60);
        await WaitUntil(() => Player.IsOnFloor() && PlayerFree, 60);
        bool ok = atTablet && open && closed && title == Numerals[k - 1] && GameSession.Progress.Flag($"tablet.gate_{k}");
        Check(ok, $"第 {k} 块：走到泥板「{Numerals[k - 1]}」旁按 E 读完（{title}），tablet.gate_{k} 成立");
        return ok;
    }
}
