using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.Bosses.Keeper.States;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 关卡几何计划 v3 第 5.4 节：地图 1 一路走到底，全程不传送。从正式入口 main_game.tscn 开新游戏，真实玩家、真实按键：
/// ① 静态模拟器在地图 1 的文字图上规划路线，路线机器人用真实按键从 S1_01 按到 S1_05 竞技场外（坑底的台阶和攀墙、
///    朝圣者之路尽头的台阶和攀墙都在里面）；② 走进竞技场开战，用 KeeperBot 真实按键打赢守坑人；
/// ③ 闪白结束、条件台阶（R）出现后，再规划一次：爬上之字形台阶出竞技场，经 S1_06 的台阶、S1_07 平地，走过边缘进入 S2_01。
/// 全程只靠走边缘换房（不发 PlayerTeleported），玩家没死。路线每一步的结果打印在日志里。
/// 这条测试验证的是地形能不能走通：沿路的普通敌人（祈祷泥人、虔诚泥人等）进房间时就冻结（不跑 AI、不出手），
/// 和它们打的情况由敌人测试覆盖；守坑人照常真打。
/// </summary>
public partial class Map1WalkTest : EnemyFlowBase
{
    protected override string SuiteName => "map1_walk";
    protected override string ResultPrefix => "levelcheck";

    private static readonly string[] Map1 = { "S1_01", "S1_02", "S1_03", "S1_04", "S1_05", "S1_06", "S1_07", "S1_08" };

    private SimConfig _cfg;
    private LevelWorld _world;
    private RouteBot _bot;
    private int _teleports;
    private Callable _freeze;

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
            if (_freeze.Target != null && Events.Instance.IsConnected(Events.SignalName.RoomEntered, _freeze))
                Events.Instance.Disconnect(Events.SignalName.RoomEntered, _freeze);
            _bot?.ReleaseAll();
            UnlistenEnemy();
        }
    }

    private Vector2 RealOrigin => World.CurrentRoom.GlobalPosition;

    /// <summary>冻结当前房间里除守坑人以外的敌人（见类注释）。</summary>
    private void FreezeRegularEnemies(string roomId)
    {
        foreach (SonOfMud.Enemy.EnemyBody enemy in InRoom<SonOfMud.Enemy.EnemyBody>())
            if (enemy is not KeeperBoss) enemy.ProcessMode = ProcessModeEnum.Disabled;
    }

    /// <summary>玩家身体中心换算到模拟器的全局坐标（房间原点 + 房间局部坐标）。</summary>
    private Vector2 SimCenter => _world.Find(RoomId).Origin + (Player.GlobalPosition - RealOrigin);

    private async Task<bool> Follow(string phase, List<MoveEdge> edges)
    {
        if (edges == null || edges.Count == 0)
        {
            Check(false, $"{phase}：模拟器没有找到路线");
            return false;
        }
        List<RouteStep> steps = RoutePlan.From(edges);
        _bot.Log.Clear();
        var (ok, done) = await _bot.RunRoute(steps);
        foreach (string line in _bot.Log) GD.Print("    " + line);
        Check(ok, $"{phase}：路线机器人用真实按键走完 {done}/{steps.Count} 步（{edges.Count(e => e.Kind == EdgeKind.Move)} 次移动、" +
                  $"{edges.Count(e => e.Climb)} 次攀墙、{edges.Count(e => e.Kind == EdgeKind.Exit)} 次出房间），现在在 {RoomId}");
        return ok;
    }

    private async Task Run(Callable onTeleport)
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        BindNarrative();
        await WaitSettled();
        Check(finished && Player != null && RoomId == "S1_01", "新游戏：从正式入口开局，玩家在 S1_01");
        if (Player == null || RoomId != "S1_01") return;
        Events.Instance.Connect(Events.SignalName.PlayerTeleported, onTeleport);
        _freeze = Callable.From<string>(FreezeRegularEnemies);
        Events.Instance.Connect(Events.SignalName.RoomEntered, _freeze);
        FreezeRegularEnemies(RoomId);

        _cfg = SimConfig.Load();
        _world = new LevelWorld(LevelWorld.DefaultCellSize);
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        foreach (string id in Map1)
        {
            RoomDef def = layout.Rooms.First(r => r.Id == id);
            RoomGrid grid = RoomGrid.Load(def, (Vector2I)LevelWorld.DefaultCellSize, out string error);
            Check(grid != null, $"{id} 的文字图能读到{(grid == null ? "：" + error : "")}");
            if (grid == null) return;
            _world.Add(def, grid);
        }
        _bot = new RouteBot(this, () => Player, () => (RoomId, RealOrigin));
        int form = Player.Forms.Form;

        // ① S1_01 → S1_05 竞技场外（局部 x=200 的地面）
        var state = new LevelState();
        var graph = new MoveGraph(_cfg, _world, state);
        Segment start = graph.FindSegment(_world.Find("S1_01"), SimCenter);
        LevelRoom s105 = _world.Find("S1_05");
        Vector2I lockCell = s105.Grid.Find(RoomGrid.DefaultSpawn).Value;
        Segment arenaDoor = graph.FindSegment(s105, s105.FeetGlobal(6, lockCell.Y) - new Vector2(0, _cfg.HalfH));
        Check(start != null && arenaDoor != null, "模拟器：起点（S1_01 出生处）和 S1_05 竞技场外的地面都找得到");
        if (start == null || arenaDoor == null) return;
        List<MoveEdge> toArena = graph.Explore(new[] { start }, e => e.Form == form).RouteTo(arenaDoor);
        if (!await Follow("S1_01 → S1_05 竞技场外", toArena)) return;

        // ② 开战、打赢守坑人
        KeeperBoss keeper = InRoom<KeeperBoss>().FirstOrDefault();
        ArenaLock arenaLock = InRoom<ArenaLock>().FirstOrDefault();
        bool engaged = keeper != null && arenaLock != null && await HoldKeyUntil(RealKeys.Right, () => LocalX >= 1460f, 600);
        Check(engaged && arenaLock.IsClosed && Encounters.Count == 1, "S1_05：走进竞技场开战，入口门关上");
        if (!engaged) return;
        var fighter = new KeeperBot(Player, keeper, n => Frames(n)) { AllowMove = false };
        bool won = await fighter.FightUntil(() => keeper.State is TransformState || Player.Stats.IsDepleted, 3600);
        bool gone = won && await WaitUntil(() => keeper.State is GoneState && GameSession.Progress.Flag("boss_keeper"), 240);
        ConditionalTerrain steps = InRoom<ConditionalTerrain>().FirstOrDefault();
        bool stepsShown = gone && await WaitUntil(() => steps != null && steps.Enabled && steps.CollisionEnabled, 240);
        await WaitUntil(() => Player.IsOnFloor() && PlayerFree && !World.IsTransitioning, 120);
        Check(won && gone && stepsShown && Deaths == 0, $"S1_05：真实按键打赢守坑人（出刀 {fighter.Attacks}），闪白结束后条件台阶出现");
        if (!stepsShown) return;

        // ③ 爬条件台阶出竞技场 → S1_06 → S1_07 → 走过边缘进入 S2_01
        state.Flags.Add("boss_keeper");
        graph = new MoveGraph(_cfg, _world, state);
        Segment here = graph.FindSegment(s105, SimCenter);
        Check(here != null, $"模拟器：打完后玩家所在的地面找得到（S1_05 局部 {Player.GlobalPosition - RealOrigin}）");
        if (here == null) return;
        LevelRoom s107 = _world.Find("S1_07");
        ExitDef toS201 = s107.Def.Exits.First(e => e.Target == "S2_01");
        List<MoveEdge> toEnd = graph.Explore(new[] { here }, e => e.Form == form).RouteToExit(MoveEdge.ExitKey(s107, toS201));
        Check(toEnd == null || toEnd.Any(e => e.From.Room.Id == "S1_05" && e.To?.Room.Id == "S1_06"), "路线经过条件台阶从 S1_05 高处出口进入 S1_06");
        if (!await Follow("S1_05 → S2_01", toEnd)) return;
        await Frames(10);

        Check(RoomId == "S2_01" && Deaths == 0, $"一路走到 S2_01，玩家没死（死亡 {Deaths} 次）");
        Check(_teleports == 0, $"全程只靠走边缘换房，没有传送（PlayerTeleported {_teleports} 次）");
    }
}
