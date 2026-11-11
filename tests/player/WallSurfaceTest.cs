using System;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;

/// <summary>
/// 不可攀墙测试场（关卡几何计划 v3 第 2.1 节）：运行时用代码搭一个 32 像素的 TileMapLayer（代码建的 TileSet，
/// 带 Surfaces.NoClimbData 自定义数据层），放真实玩家 player_v2.tscn，用真实按键（RealKeys），水、泥各跑一遍：
/// - 绕过 1：贴着 X 墙连按跳 20 次（只按朝墙，和朝墙 + 上两种），最高点不超过单跳最高点，从未进入攀墙，也没有蹬墙跳；
/// - 绕过 2：下半段可攀、上半段 X 的墙，从下往上爬，到交界处转为下落，最高点不超过交界线；
/// - 对照：同样的动作换成全可攀的墙，能爬到顶翻上去，蹬墙跳照常可用；
/// - 门体：贴着加进 Surfaces.NoClimbGroup 的 StaticBody2D 爬不上去；同样的 StaticBody2D 不在分组里就能爬上去。
/// 结果写到 tests/output/wall_surface_results.json。
/// </summary>
public partial class WallSurfaceTest : LabSuiteBase
{
    private const int Tile = 32;
    private const int FloorRow = 20;
    private const float FloorTop = FloorRow * Tile;          // 640
    private const float StandY = FloorTop - 24f;             // 身体 48×48，中心离地 24
    private const float HalfBody = 24f;

    // 各段场地（瓦片列号；墙都是两列宽）
    private const int XWallCol = 30;      // 全是 X，从顶到地
    private const int MixedWallCol = 60;  // 下半段 #、上半段 X
    private const int MixedJunctionRow = 10;
    private const int ClimbWallCol = 90;  // 全是 #，顶在第 6 行
    private const int ClimbWallTopRow = 6;
    private const float DoorX = 3840f, PlainDoorX = 4800f, DoorTop = 192f;

    private const int JumpPresses = 20;
    private const int PressEvery = 15;

    protected override string ReportName => "wall_surface_results";

    private TileMapLayer _tiles;
    private StaticBody2D _door, _plainDoor;
    private int _airWindups;

    private static float JunctionY => MixedJunctionRow * Tile;   // 320
    private static float ClimbWallTop => ClimbWallTopRow * Tile; // 192

    protected override async Task RunChecks()
    {
        GameSession.Reset(GD.Load<AbilityTable>(AbilityTable.DefaultPath));
        await UseLab(BuildLab());
        Check(Player.HasAbility(PlayerAbilities.WallClimb), "前提：按正式能力表开局，玩家有攀墙（测试没有手动解锁）");
        Check(_tiles.TileSet.GetCustomDataLayerByName(Surfaces.NoClimbData) >= 0 && IsNoClimb(new Vector2I(XWallCol, 5)) && !IsNoClimb(new Vector2I(ClimbWallCol, 10))
              && _door.IsInGroup(Surfaces.NoClimbGroup) && !_plainDoor.IsInGroup(Surfaces.NoClimbGroup),
            "前提：瓦片集带 no_climb 自定义数据层，X 墙为 true、# 墙为 false；门体在 no_climb 分组里，对照门体不在");
        Player.Machine.StateChanged += OnStateChanged;

        foreach (int form in new[] { 0, 1 })
        {
            string name = form == 0 ? "水" : "泥";
            float single = await MeasureSingleJump(form);
            Metric($"single_jump_{form}", single);
            FormConfig cfg = form == 0 ? Player.Forms.WaterConfig : Player.Forms.MudConfig;
            float formula = cfg.JumpSpeed * cfg.JumpSpeed / (2f * cfg.Gravity);
            Check(single > 60f && Mathf.Abs(single - formula) < 12f, $"{name}：原地单跳最高点 {single:F1}px（按配置 v²/2g 约 {formula:F1}px）");
            await Bypass1(form, name, single);
            await Bypass2(form, name, single);
            await Controls(form, name, single);
            await Doors(form, name, single);
        }
        Player.Machine.StateChanged -= OnStateChanged;
    }

    // ---------- 场地 ----------

    private Node BuildLab()
    {
        var lab = new Node2D { Name = "WallSurfaceLab" };
        _tiles = new TileMapLayer { Name = "Geometry", TileSet = BuildTileSet() };
        lab.AddChild(_tiles);
        for (int c = 0; c < 160; c++) Set(c, FloorRow, false);
        for (int r = 0; r < FloorRow; r++)
        {
            Set(0, r, false);
            Set(159, r, false);
            for (int c = XWallCol; c < XWallCol + 2; c++) Set(c, r, true);
            for (int c = MixedWallCol; c < MixedWallCol + 2; c++) Set(c, r, r < MixedJunctionRow);
            if (r >= ClimbWallTopRow)
                for (int c = ClimbWallCol; c < ClimbWallCol + 2; c++) Set(c, r, false);
        }
        _door = MakeDoor("ArenaDoor", DoorX, true);
        _plainDoor = MakeDoor("PlainDoor", PlainDoorX, false);
        lab.AddChild(_door);
        lab.AddChild(_plainDoor);
        Node player = GD.Load<PackedScene>("res://assets/scenes/player/player_v2.tscn").Instantiate();
        player.Name = "Player";
        ((Node2D)player).Position = new Vector2(400, StandY);
        lab.AddChild(player);
        return lab;
    }

    private void Set(int col, int row, bool noClimb) => _tiles.SetCell(new Vector2I(col, row), 0, new Vector2I(noClimb ? 1 : 0, 0));

    /// <summary>32 像素瓦片集：(0,0) 可攀，(1,0) 不可攀（no_climb = true）。都是整格碰撞。</summary>
    private static TileSet BuildTileSet()
    {
        var tileSet = new TileSet { TileSize = new Vector2I(Tile, Tile) };
        tileSet.AddPhysicsLayer();
        tileSet.SetPhysicsLayerCollisionLayer(0, Layers.World);
        tileSet.SetPhysicsLayerCollisionMask(0, 0);
        tileSet.AddCustomDataLayer();
        tileSet.SetCustomDataLayerName(0, Surfaces.NoClimbData);
        tileSet.SetCustomDataLayerType(0, Variant.Type.Bool);
        var image = Image.CreateEmpty(Tile * 2, Tile, false, Image.Format.Rgba8);
        image.FillRect(new Rect2I(0, 0, Tile, Tile), new Color(0.36f, 0.31f, 0.26f));
        image.FillRect(new Rect2I(Tile, 0, Tile, Tile), new Color(0.55f, 0.2f, 0.2f));
        var source = new TileSetAtlasSource { Texture = ImageTexture.CreateFromImage(image), TextureRegionSize = new Vector2I(Tile, Tile) };
        tileSet.AddSource(source, 0);
        float h = Tile / 2f;
        Vector2[] square = { new(-h, -h), new(h, -h), new(h, h), new(-h, h) };
        for (int i = 0; i < 2; i++)
        {
            source.CreateTile(new Vector2I(i, 0));
            TileData data = source.GetTileData(new Vector2I(i, 0), 0);
            data.SetCollisionPolygonsCount(0, 1);
            data.SetCollisionPolygonPoints(0, 0, square);
            data.SetCustomData(Surfaces.NoClimbData, i == 1);
        }
        return tileSet;
    }

    /// <summary>448 像素高、64 像素宽的门体（StaticBody2D，World 层），立在地面上。</summary>
    private static StaticBody2D MakeDoor(string name, float left, bool noClimb)
    {
        var body = new StaticBody2D { Name = name, CollisionLayer = Layers.World, CollisionMask = 0, Position = new Vector2(left + 32f, (DoorTop + FloorTop) / 2f) };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(64f, FloorTop - DoorTop) } });
        if (noClimb) body.AddToGroup(Surfaces.NoClimbGroup);
        return body;
    }

    private bool IsNoClimb(Vector2I cell) => _tiles.GetCellTileData(cell)?.GetCustomData(Surfaces.NoClimbData).AsBool() ?? false;

    private void OnStateChanged(StringName from, StringName to)
    {
        if (Player.State is JumpWindupState && !Player.IsOnFloor()) _airWindups++;
    }

    // ---------- 动作 ----------

    private sealed class Attempt
    {
        public float MinY = float.MaxValue;
        public bool SawClimb;
        public int AirWindups;
        public int TargetContactFrames;
        public int NoClimbFrames;
        public bool FellAtJunction;
        public float FinalX, FinalY;
        public bool FinalOnFloor;
        public float Rise(float startY) => startY - MinY;
        public float MinTop => MinY - HalfBody;
    }

    /// <summary>
    /// 从 startX 出发，真实按键：一直按住 D（朝右边的墙），holdUp 时同时按住 W；spam 为 true 时每 15 帧按一次空格、共 20 次，
    /// 否则只在开头按一次空格。target 是要统计接触帧数的碰撞体（墙所在的 TileMapLayer 或门体）。
    /// until 成立就提前结束（例如已经翻上墙顶，不再按着 D 往前走）。
    /// </summary>
    private async Task<Attempt> Run(float startX, int form, bool holdUp, bool spam, int frames, GodotObject target, Func<bool> until = null)
    {
        RealKeys.ReleaseAll();
        await Reset(new Vector2(startX, StandY), form);
        var result = new Attempt();
        int windupsBefore = _airWindups;
        RealKeys.Down(RealKeys.Right);
        if (holdUp) RealKeys.Down(RealKeys.MoveUp);
        int presses = spam ? JumpPresses : 1;
        int total = Mathf.Max(frames, presses * PressEvery + 60);
        for (int f = 0; f < total; f++)
        {
            int k = f / PressEvery, phase = f % PressEvery;
            if (k < presses && phase == 2) RealKeys.Down(RealKeys.Jump);
            if (k < presses && phase == 4) RealKeys.Up(RealKeys.Jump);
            await Frames(1);
            result.MinY = Mathf.Min(result.MinY, Player.GlobalPosition.Y);
            result.SawClimb |= Player.State is WallClimbState;
            if (Player.IsOnWall() && Player.TouchesNoClimbWall()) result.NoClimbFrames++;
            if (result.SawClimb && Player.State is FallState && Player.GlobalPosition.Y - HalfBody <= JunctionY + 8f) result.FellAtJunction = true;
            for (int i = 0; i < Player.GetSlideCollisionCount(); i++)
            {
                KinematicCollision2D c = Player.GetSlideCollision(i);
                if (Mathf.Abs(c.GetNormal().X) < 0.9f || c.GetCollider() != target) continue;
                result.TargetContactFrames++;
                break;
            }
            if (until != null && until()) break;
        }
        RealKeys.ReleaseAll();
        await Frames(2);
        result.AirWindups = _airWindups - windupsBefore;
        result.FinalX = Player.GlobalPosition.X;
        result.FinalY = Player.GlobalPosition.Y;
        result.FinalOnFloor = Player.IsOnFloor();
        return result;
    }

    private async Task<float> MeasureSingleJump(int form)
    {
        RealKeys.ReleaseAll();
        await Reset(new Vector2(400, StandY), form);
        float startY = Player.GlobalPosition.Y, minY = startY;
        RealKeys.Down(RealKeys.Jump);
        await Frames(2);
        RealKeys.Up(RealKeys.Jump);
        bool left = false;
        for (int i = 0; i < 180; i++)
        {
            await Frames(1);
            minY = Mathf.Min(minY, Player.GlobalPosition.Y);
            left |= !Player.IsOnFloor();
            if (left && Player.IsOnFloor()) break;
        }
        return startY - minY;
    }

    /// <summary>已经翻上去、站在墙（门体）顶上。</summary>
    private bool OnTopOf(float left, float top) => Player.IsOnFloor() && Player.GlobalPosition.X > left && Player.GlobalPosition.Y < top;

    // ---------- 绕过 1：贴着 X 墙连按跳 ----------

    private async Task Bypass1(int form, string name, float single)
    {
        float startX = XWallCol * Tile - 60f;
        foreach (bool holdUp in new[] { false, true })
        {
            string keys = holdUp ? "D+W" : "D";
            Attempt a = await Run(startX, form, holdUp, true, 0, _tiles);
            Metric($"bypass1_{form}_{keys}_rise", a.Rise(StandY));
            Check(a.TargetContactFrames > 30, $"{name} 绕过 1（按住 {keys}）：确实贴着 X 墙（接触 {a.TargetContactFrames} 帧）");
            Check(a.Rise(StandY) <= single + 1f, $"{name} 绕过 1（按住 {keys}，连按跳 {JumpPresses} 次）：最高点 {a.Rise(StandY):F1}px 不超过单跳 {single:F1}px");
            Check(!a.SawClimb && a.AirWindups == 0, $"{name} 绕过 1（按住 {keys}）：从未进入攀墙状态，也没有空中起跳（蹬墙跳 {a.AirWindups} 次）");
            Check(a.NoClimbFrames > 30, $"{name} 绕过 1（按住 {keys}）：贴墙时 TouchesNoClimbWall 认出 X 墙（{a.NoClimbFrames} 帧）");
        }
    }

    // ---------- 绕过 2：下半段 #、上半段 X ----------

    private async Task Bypass2(int form, string name, float single)
    {
        Attempt a = await Run(MixedWallCol * Tile - 60f, form, true, false, 360, _tiles);
        Metric($"bypass2_{form}_min_top", a.MinTop);
        Check(a.SawClimb && a.MinTop <= JunctionY + 8f,
            $"{name} 绕过 2：从下面开始攀墙，身体顶部爬到了交界线附近（最高 {a.MinTop:F1}，交界线 {JunctionY}，单跳只能到 {StandY - HalfBody - single:F0}）");
        // 余量 4 像素：攀墙速度 180px/s，一帧最多上 3 像素；碰到 X 的那一帧立刻去掉向上的速度
        Check(a.MinTop >= JunctionY - 4f, $"{name} 绕过 2：身体顶部最高 {a.MinTop:F1}，不超过交界线 {JunctionY}（允许 4px 碰撞余量）");
        Check(a.FellAtJunction, $"{name} 绕过 2：到交界处转为下落");
    }

    // ---------- 对照：全可攀的墙 ----------

    private async Task Controls(int form, string name, float single)
    {
        float startX = ClimbWallCol * Tile - 60f, wallLeft = ClimbWallCol * Tile;
        Attempt climb = await Run(startX, form, true, false, 360, _tiles, () => OnTopOf(wallLeft, ClimbWallTop));
        Check(climb.SawClimb && climb.NoClimbFrames == 0 && climb.FinalX > wallLeft && climb.FinalY < ClimbWallTop && climb.FinalOnFloor,
            $"{name} 对照：全可攀的墙，按住 D+W 从下面爬到顶并翻上去（最终 ({climb.FinalX:F0},{climb.FinalY:F0})，墙顶 {ClimbWallTop}）");

        Attempt spam = await Run(startX, form, false, true, 0, _tiles);
        Metric($"control_spam_{form}_rise", spam.Rise(StandY));
        Check(spam.AirWindups > 0 && spam.Rise(StandY) > single + 32f,
            $"{name} 对照：全可攀的墙，同样只按 D 连按跳 {JumpPresses} 次，蹬墙跳照常可用（蹬墙跳 {spam.AirWindups} 次，最高 {spam.Rise(StandY):F1}px > 单跳 {single:F1}px）");
    }

    // ---------- 门体 ----------

    private async Task Doors(int form, string name, float single)
    {
        foreach (bool spam in new[] { false, true })
        {
            string how = spam ? $"按住 D+W 连按跳 {JumpPresses} 次" : "按住 D+W 起跳";
            Attempt a = await Run(DoorX - 60f, form, true, spam, 300, _door);
            Check(a.TargetContactFrames > 30 && !a.SawClimb && a.AirWindups == 0 && a.Rise(StandY) <= single + 1f,
                $"{name} 门体（no_climb 分组）：{how}，爬不上去（接触 {a.TargetContactFrames} 帧，攀墙 {a.SawClimb}，蹬墙跳 {a.AirWindups} 次，最高 {a.Rise(StandY):F1}px ≤ 单跳 {single:F1}px）");
        }
        Attempt plain = await Run(PlainDoorX - 60f, form, true, false, 360, _plainDoor, () => OnTopOf(PlainDoorX, DoorTop));
        Check(plain.SawClimb && plain.FinalX > PlainDoorX && plain.FinalY < DoorTop && plain.FinalOnFloor,
            $"{name} 对照：同样的门体不在 no_climb 分组里，按住 D+W 能爬上去（接触 {plain.TargetContactFrames} 帧，攀墙 {plain.SawClimb}，最高 {plain.Rise(StandY):F1}px，最终 ({plain.FinalX:F0},{plain.FinalY:F0})）");
    }
}
