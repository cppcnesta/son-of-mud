using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.Tools.LevelCheck;
using SonOfMud.World;

/// <summary>
/// 静态模拟器的校准（关卡几何计划 v3 第 5.2 节）：在运行时用文字图 fixtures/calibration.txt 搭场地（代码里建的 32 像素瓦片集），
/// 放真实玩家 player_v2.tscn，用真实按键（RealKeys）做固定动作，逐帧记录坐标，和静态模拟器的输出比对：
/// 落点误差 ≤ 4 像素、轨迹最大误差 ≤ 8 像素，水、泥各一遍。
/// 再用路线机器人把模拟器算出的 3 条路线（跳上台阶、攀墙、攀上高台再走下去）按出来。
/// 结果写到 tests/output/levelcheck_calibration_results.json。
/// </summary>
public partial class CalibrationTest : LabSuiteBase
{
    /// <summary>
    /// 不可攀墙（X）和真实玩家的比对开关。玩家代码里的规则（CanUseWall）已随 feat/geo-wall 合并，现在启用；
    /// 关掉时只跑模拟器一侧的断言。
    /// </summary>
    public static readonly bool NoClimbRealEnabled = true;

    public const float LandTolerance = 4f;
    public const float PathTolerance = 8f;

    private const string FixturePath = "res://tests/levelcheck/fixtures/calibration.txt";

    protected override string ReportName => "levelcheck_calibration_results";

    private SimConfig _cfg;
    private LevelWorld _world;
    private LevelRoom _room;
    private readonly LevelState _state = new();
    private PlayerCharacter _player;
    private RouteBot _bot;
    private readonly Dictionary<string, (int landFrame, Vector2 land)> _real = new();

    private static InputStep F(int frames, int axis, bool up = false, bool jump = false) => InputStep.For(frames, axis, up, jump);
    private static InputStep U(Until until, int axis, bool up = false, bool jump = false) => InputStep.Till(until, axis, up, jump);

    private sealed record Case(string Name, char Anchor, InputScript Script);

    private static readonly Case[] Cases =
    {
        new("原地跳", 'a', new InputScript("原地跳", F(1, 0, jump: true))),
        new("跑跳", 'a', new InputScript("跑跳", F(10, 1), F(20, 1, jump: true))),
        new("10格高台走下去按住方向", 'b', new InputScript("10格高台走下去按住方向", U(Until.Airborne, 1), U(Until.Grounded, 1))),
        new("10格高台走下去松手", 'b', new InputScript("10格高台走下去松手", U(Until.Airborne, 1))),
        new("贴墙下落", 'c', new InputScript("贴墙下落", F(1, 1), U(Until.Grounded, 1, jump: true))),
        new("攀墙到顶翻上去", 'c', new InputScript("攀墙到顶翻上去", F(1, 1), U(Until.Grounded, 1, up: true, jump: true))),
        new("蹬墙跳后继续攀", 'c', new InputScript("蹬墙跳后继续攀", F(1, 1), U(Until.Climbing, 1, up: true, jump: true), F(20, 1, up: true), U(Until.Grounded, 1, up: true, jump: true))),
        new("蹬墙跳离墙", 'c', new InputScript("蹬墙跳离墙", F(1, 1), U(Until.Climbing, 1, up: true, jump: true), F(30, 1, up: true), U(Until.Grounded, -1, jump: true))),
        new("单向平台跳上去", 'd', new InputScript("单向平台跳上去", F(1, 0, jump: true))),
    };

    /// <summary>贴着 X 墙：连按跳 20 次；按住方向和上起跳。</summary>
    private static InputScript MashAgainstWall()
    {
        var steps = new List<InputStep> { F(1, 1) };
        for (int i = 0; i < 20; i++)
        {
            steps.Add(F(1, 1, jump: true));
            steps.Add(F(5, 1));
        }
        steps.Add(U(Until.Grounded, 1));
        return new InputScript("贴着X墙连按跳20次", steps.ToArray());
    }

    private static readonly Case[] NoClimbCases =
    {
        new("贴着X墙连按跳20次", 'e', MashAgainstWall()),
        new("X墙按住方向和上", 'e', new InputScript("X墙按住方向和上", F(1, 1), U(Until.Grounded, 1, up: true, jump: true))),
    };

    protected override async Task RunChecks()
    {
        _cfg = SimConfig.Load();
        ConfigChecks();

        var def = new RoomDef { Id = "CAL", Origin = Vector2I.Zero, Size = new Vector2I(3, 2) };
        RoomGrid grid = RoomGrid.Parse(FileAccess.GetFileAsString(FixturePath), def, (Vector2I)LevelWorld.DefaultCellSize, out string error);
        Check(grid != null, "校准场文字图解析通过 " + error);
        if (grid == null) return;
        _world = new LevelWorld(LevelWorld.DefaultCellSize);
        _room = _world.Add(def, grid);

        var arena = new Node2D { Name = "Arena" };
        AddChild(arena);
        arena.AddChild(GridTileMap.Build(grid, false, _room.Origin));
        _player = GD.Load<PackedScene>(SimConfig.PlayerScenePath).Instantiate<PlayerCharacter>();
        arena.AddChild(_player);
        GameSession.Progress.Unlock(PlayerAbilities.WallClimb);
        GameSession.Progress.Unlock(PlayerAbilities.SwitchForm);
        _bot = new RouteBot(this, () => _player, () => ("CAL", _room.Origin));
        await Frames(6);

        await LatencyProbe();
        foreach (int form in new[] { SimConfig.Water, SimConfig.Mud })
            foreach (Case c in Cases)
                await Calibrate(c, form);
        ContrastCheck();
        await NoClimbGroup();
        await RouteChecks();
    }

    /// <summary>模拟器读到的就是正式配置（抽查几项和 .tres 一致，且两种形态的下落方式不同）。</summary>
    private void ConfigChecks()
    {
        var water = GD.Load<FormConfig>("res://assets/data/player_v2/water.tres");
        var mud = GD.Load<FormConfig>("res://assets/data/player_v2/mud.tres");
        var rules = GD.Load<PlayerRules>("res://assets/data/player_v2/rules.tres");
        Check(_cfg.WaterForm.JumpWindup == water.JumpWindup && _cfg.MudForm.JumpWindup == mud.JumpWindup &&
              _cfg.WaterForm.MoveSpeed == water.MoveSpeed && _cfg.MudForm.MoveSpeed == mud.MoveSpeed &&
              _cfg.WaterForm.Style == water.JumpStyle && _cfg.MudForm.Style == mud.JumpStyle &&
              _cfg.MudForm.TerminalSpeed == mud.TerminalSpeed && _cfg.WaterForm.DescentSpeed == water.DescentSpeed,
            "模拟器的形态参数来自正式的 water.tres / mud.tres");
        Check(_cfg.CoyoteTime == rules.CoyoteTime && _cfg.JumpBuffer == rules.JumpBuffer, "模拟器的土狼时间、跳跃缓冲来自 rules.tres");
        Check(_cfg.BodySize == new Vector2(48, 48) && Mathf.IsEqualApprox(_cfg.Dt, 1f / 60f), $"身体 {_cfg.BodySize}、步长 {_cfg.Dt}");
        Check(_cfg.WaterForm.Style == JumpStyle.ConstantDescent && _cfg.MudForm.Style == JumpStyle.HeavyFall, "水恒速下落、泥加速下落（配置里）");
    }

    private async Task ResetAt(Vector2 center, int form)
    {
        _bot.ReleaseAll();
        InputLock.Reset();
        _player.ResetForLab(center, form);
        for (int i = 0; i < 40; i++)
        {
            await Frames(1);
            if (i > 4 && _player.IsOnFloor() && _player.State is IdleState) break;
        }
    }

    private Vector2 AnchorCenter(char anchor)
    {
        Vector2I cell = _room.Grid.Find(anchor) ?? throw new InvalidOperationException("没有锚点 " + anchor);
        return _room.FeetGlobal(cell.X, cell.Y) - new Vector2(0, _cfg.HalfH);
    }

    /// <summary>实测：在第 0 次恢复时按下右键，位置在第几次恢复时开始变化 → 观察延迟。</summary>
    private async Task LatencyProbe()
    {
        await ResetAt(AnchorCenter('a'), SimConfig.Water);
        float x0 = _player.GlobalPosition.X;
        RealKeys.Down(RealKeys.Right);
        int changed = -1;
        for (int i = 1; i <= 6; i++)
        {
            await Frames(1);
            if (changed < 0 && Mathf.Abs(_player.GlobalPosition.X - x0) > 0.5f) changed = i;
        }
        RealKeys.Up(RealKeys.Right);
        int delay = changed - 1;
        Metric("observation_delay", delay);
        Check(delay == ScriptRunner.ObservationDelay, $"按键延迟探针：实测观察延迟 {delay} 帧，和 ScriptRunner.ObservationDelay（{ScriptRunner.ObservationDelay}）一致");
    }

    private static int LandFrame(IReadOnlyList<bool> grounded)
    {
        bool left = false;
        for (int j = 0; j < grounded.Count; j++)
        {
            if (!grounded[j]) left = true;
            else if (left) return j;
        }
        return -1;
    }

    private async Task<(float land, float path, SimRun sim, RouteBot.Recording rec)> Compare(Case c, int form, bool noClimb = false)
    {
        await ResetAt(AnchorCenter(c.Anchor), form);
        Vector2 start = _player.GlobalPosition;
        RouteBot.Recording rec = await _bot.Run(c.Script);
        SimRun sim = _world.Simulate(_cfg, _state, _room, rec.Start.X, rec.Start.Y, form, c.Script, recordPath: true);
        int n = Math.Max(rec.Path.Count, sim.Path.Count);
        float path = 0f;
        int worst = 0;
        for (int j = 0; j < n; j++)
        {
            Vector2 a = rec.Path[Math.Min(j, rec.Path.Count - 1)], b = sim.Path[Math.Min(j, sim.Path.Count - 1)];
            float d = a.DistanceTo(b);
            if (d > path)
            {
                path = d;
                worst = j;
            }
        }
        float land = rec.Path.Count > 0 && sim.Path.Count > 0 ? rec.Path[^1].DistanceTo(sim.Path[^1]) : float.MaxValue;
        string formName = form == SimConfig.Mud ? "泥" : "水";
        if (path > PathTolerance || land > LandTolerance)
        {
            int j = Math.Min(worst, Math.Min(rec.Path.Count, sim.Path.Count) - 1);
            GD.Print($"[校准差异] {c.Name}/{formName} 最大差在第 {worst} 帧：真实 {rec.Path[Math.Min(worst, rec.Path.Count - 1)]} {rec.States[Math.Min(worst, rec.States.Count - 1)]}，" +
                     $"模拟 {sim.Path[Math.Min(worst, sim.Path.Count - 1)]} {sim.States[Math.Min(worst, sim.States.Count - 1)]}；帧数 真实 {rec.Path.Count} 模拟 {sim.Path.Count}");
            bool dumpAll = OS.GetEnvironment("LEVELCHECK_DUMP") == "1";
            for (int k = dumpAll ? 0 : Math.Max(0, j - 3); k <= (dumpAll ? Math.Min(rec.Path.Count, sim.Path.Count) - 1 : Math.Min(j + 3, Math.Min(rec.Path.Count, sim.Path.Count) - 1)); k++)
                GD.Print($"    帧{k}: 真实 {rec.Path[k]} {rec.States[k]} | 模拟 {sim.Path[k]} {sim.States[k]}");
        }
        return (land, path, sim, rec);
    }

    private async Task Calibrate(Case c, int form)
    {
        var (land, path, sim, rec) = await Compare(c, form);
        string formName = form == SimConfig.Mud ? "泥" : "水";
        Metric($"{c.Name}_{formName}_落点误差", land);
        Metric($"{c.Name}_{formName}_轨迹最大误差", path);
        Metric($"{c.Name}_{formName}_帧数", new Godot.Collections.Array { rec.Path.Count, sim.Path.Count });
        Check(!rec.Failed && sim.End == RunEnd.Settled && land <= LandTolerance && path <= PathTolerance,
            $"校准 {c.Name}（{formName}）：落点误差 {land:F2} ≤ {LandTolerance}，轨迹最大误差 {path:F2} ≤ {PathTolerance}（{rec.Path.Count} 帧 / 模拟 {sim.Path.Count} 帧）");
        int realLand = LandFrame(rec.States.Select(s => s is nameof(IdleState) or nameof(RunState)).ToList());
        int simLand = LandFrame(sim.States.Select(s => s is SimState.Idle or SimState.Run).ToList());
        _real[$"{c.Name}/{form}"] = (realLand, rec.Path.Count > 0 ? rec.Path[^1] : Vector2.Zero);
        _real[$"{c.Name}/{form}/sim"] = (simLand, sim.EndPos);
    }

    /// <summary>对照用例：同一个“从 10 格高处走下去（按住方向）”，水和泥的落地时间和落点必须不同，并且各自和真实一致。</summary>
    private void ContrastCheck()
    {
        const string name = "10格高台走下去按住方向";
        var waterReal = _real[$"{name}/{SimConfig.Water}"];
        var mudReal = _real[$"{name}/{SimConfig.Mud}"];
        var waterSim = _real[$"{name}/{SimConfig.Water}/sim"];
        var mudSim = _real[$"{name}/{SimConfig.Mud}/sim"];
        Metric("对照_落地帧", new Godot.Collections.Array { waterReal.landFrame, waterSim.landFrame, mudReal.landFrame, mudSim.landFrame });
        Check(waterReal.landFrame != mudReal.landFrame && waterReal.land.DistanceTo(mudReal.land) > 32f,
            $"对照：真实玩家水、泥落地帧（{waterReal.landFrame} / {mudReal.landFrame}）和落点（{waterReal.land} / {mudReal.land}）不同");
        Check(waterSim.landFrame == waterReal.landFrame && waterSim.land.DistanceTo(waterReal.land) <= LandTolerance,
            $"对照：水的模拟落地帧 {waterSim.landFrame}、落点 {waterSim.land} 和真实（{waterReal.landFrame}、{waterReal.land}）一致");
        Check(mudSim.landFrame == mudReal.landFrame && mudSim.land.DistanceTo(mudReal.land) <= LandTolerance,
            $"对照：泥的模拟落地帧 {mudSim.landFrame}、落点 {mudSim.land} 和真实（{mudReal.landFrame}、{mudReal.land}）一致");
    }

    /// <summary>不可攀墙（计划 2.1）：模拟器一侧现在就断言；和真实玩家比对等合并后启用。</summary>
    private async Task NoClimbGroup()
    {
        // 单跳最高点：用模拟器（已和真实校准）在开阔处原地跳一次
        Vector2 open = AnchorCenter('a');
        SimRun single = _world.Simulate(_cfg, _state, _room, open.X, open.Y, SimConfig.Water, Cases[0].Script);
        float singleJump = open.Y - (float)single.Body.MinY;
        Metric("单跳最高点", singleJump);
        foreach (int form in new[] { SimConfig.Water, SimConfig.Mud })
            foreach (Case c in NoClimbCases)
            {
                string formName = form == SimConfig.Mud ? "泥" : "水";
                Vector2 center = AnchorCenter(c.Anchor);
                SimRun sim = _world.Simulate(_cfg, _state, _room, center.X, center.Y, form, c.Script, recordPath: true);
                float rise = center.Y - (float)sim.Body.MinY;
                Check(sim.End == RunEnd.Settled && !sim.Body.EverClimbed && !sim.Body.EverWallJumped && rise <= singleJump + 1f,
                    $"模拟器：{c.Name}（{formName}）从未攀墙、从未蹬墙跳，最高升起 {rise:F1} ≤ 单跳 {singleJump:F1}");
                if (!NoClimbRealEnabled)
                {
                    GD.Print($"Tests: SKIP 和真实玩家比对 {c.Name}（{formName}）：等 feat/geo-wall（玩家的不可攀规则）合并后启用");
                    continue;
                }
                var (land, path, _, rec) = await Compare(c, form, true);
                Check(land <= LandTolerance && path <= PathTolerance && !rec.States.Contains(nameof(WallClimbState)),
                    $"校准 {c.Name}（{formName}）：落点误差 {land:F2}、轨迹最大误差 {path:F2}，真实玩家从未进入攀墙");
            }
        Metric("不可攀_真实比对", NoClimbRealEnabled ? "启用" : "等合并后启用");
    }

    /// <summary>路线机器人：用模拟器算出的路线，在真实游戏里按出来（跳上台阶、攀墙、攀上 10 格高台再走下去）。</summary>
    private async Task RouteChecks()
    {
        var graph = new MoveGraph(_cfg, _world, _state);
        Vector2 p = AnchorCenter(RoomGrid.DefaultSpawn);
        Segment start = graph.FindSegment(_room, p);
        Check(start != null, "P 在一段地面上");
        if (start == null) return;
        Segment Seg(int row, int col) => graph.Segments(_room).All.FirstOrDefault(s => s.Row == row && s.Contains(_room.Left + col * RoomGrid.Tile + 16, s.FloorY, 24));
        Segment stepTop = Seg(16, 32), wallTop = Seg(13, 48), ledgeTop = Seg(9, 5), floorRight = graph.FindSegment(_room, AnchorCenter('c'));

        var routes = new List<(string name, int form, List<MoveEdge> edges)>();
        List<MoveEdge> Route(int form, Segment from, Segment to, Func<MoveEdge, bool> extra = null) =>
            graph.Explore(new[] { from }, e => e.Form == form && (extra?.Invoke(e) ?? true)).RouteTo(to);
        routes.Add(("跳上 3 格台阶（水，不攀墙）", SimConfig.Water, Route(SimConfig.Water, start, stepTop, e => !e.Climb && !e.WallJump)));
        // 先到可攀墙脚下（要翻过台阶和 X 墙顶），再从墙脚攀上 6 格墙顶（这一段只能攀墙）
        List<MoveEdge> toFoot = floorRight != null ? Route(SimConfig.Mud, start, floorRight) : null;
        List<MoveEdge> climb = floorRight != null ? Route(SimConfig.Mud, floorRight, wallTop) : null;
        routes.Add(("攀墙到 6 格墙顶（泥）", SimConfig.Mud, toFoot != null && climb != null ? toFoot.Concat(climb).ToList() : null));
        List<MoveEdge> up = Route(SimConfig.Water, start, ledgeTop);
        List<MoveEdge> down = ledgeTop != null ? Route(SimConfig.Water, ledgeTop, start, e => !e.Climb) : null;
        routes.Add(("攀上 10 格高台再走下去（水）", SimConfig.Water, up != null && down != null ? up.Concat(down).ToList() : null));
        Check(routes.All(r => r.edges is { Count: > 0 }) && floorRight != null, "模拟器给出了 3 条路线：" + string.Join("；", routes.Select(r => $"{r.name} {(r.edges == null ? "无" : r.edges.Count + " 步")}")));
        Check(routes[0].edges?.All(e => !e.Climb) == true, "路线 1 没有攀墙");
        Check(routes[1].edges?.Any(e => e.Climb) == true, "路线 2 用到了攀墙");
        Check(routes[2].edges?.Any(e => e.Climb) == true && routes[2].edges.Last().To.FloorY > routes[2].edges.Last().From.FloorY, "路线 3 先攀墙上去、最后一步往下");

        foreach (var (name, form, edges) in routes)
        {
            if (edges == null) continue;
            await ResetAt(p, form);
            List<RouteStep> steps = RoutePlan.From(edges);
            _bot.Log.Clear();
            var (ok, done) = await _bot.RunRoute(steps);
            foreach (string line in _bot.Log) GD.Print("    " + line);
            Check(ok, $"路线机器人：{name} 按出来了（{done}/{steps.Count} 步）：" + string.Join(" → ", steps.Select(s => s.Text)));
        }
        _bot.ReleaseAll();
    }
}
