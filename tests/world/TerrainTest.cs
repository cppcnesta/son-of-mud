using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Tools;
using SonOfMud.World;

/// <summary>
/// 条件地形 R 和可破坏地形 B / H（关卡几何计划 v3 第 2、4.1 节），用真实玩家 player_v2 和真实按键，在 S1_01 的平地上测：
/// ① 条件地形：条件成立前不挡路、不可见；玩家站在会出现的位置时先不出现，离开后才出现，出现后挡路；
///    不重叠时条件一成立就同一刻出现；进房间时条件已成立直接出现；存档 → 重置 → 读档后直接出现。
/// ② 可破坏地形：挡路；普通攻击（J）打碎、写进度变量 broken.S1_01.0；离开再回来、存档读档后仍是碎的；
///    H 的外观（图块）和 # 相同，B 用裂纹图块。节点由生成工具的 GeometryBuilder.BreakableBlocks 按文字图生成（序号也由它定）。
/// 测试里的地形节点是运行时加进当前房间的（正式房间里还没有 R / B / H），节点类和生成工具的构造函数与正式生成一致。
/// </summary>
public partial class TerrainTest : WorldTestBase
{
    protected override string SuiteName => "terrain_test";

    private const string PlayerScene = "res://assets/scenes/player/player_v2.tscn";
    private const string SaveDir = "user://terrain_tests";
    private const string Flag = "test_terrain_r";
    private const string Flag2 = "test_terrain_r2";
    private const float HalfBody = 24f;

    private PlayerCharacter _player;
    private Vector2I _cell;
    private RoomDef _s101;
    private int _shift;
    private float _floorY;

    protected override async Task RunChecks()
    {
        _cell = new Vector2I((int)MetSysApi.CellSize.X, (int)MetSysApi.CellSize.Y);
        await SetUpRealGame();
        if (_player == null) return;
        _s101 = World.Layout.Find("S1_01");
        _shift = RoomGrid.ShiftFor(_s101, _cell);
        RoomGrid grid = RoomGrid.Load(_s101, _cell, out string error);
        Check(grid != null, "S1_01 的文字图能读到（" + error + "）");
        if (grid == null) return;
        _floorY = grid.TileRect(0, FloorRow(grid)).Position.Y;
        await ConditionalChecks();
        await BreakableChecks(grid);
        RealKeys.ReleaseAll();
        await TearDownGame();
    }

    // ---------- 搭建 ----------

    private async Task SetUpRealGame()
    {
        GameSession.Reset();
        GameSession.Progress.Unlock("block");
        GameSession.Progress.Unlock("switch_form");
        MetSysApi.ResetState();
        MetSysApi.SetSaveData();
        Game = GD.Load<PackedScene>(GameScene).Instantiate();
        AddChild(Game);
        World = Game.GetNode<WorldGame>("WorldGame");
        Saves = Game.GetNode<SonOfMud.Save.SaveService>("SaveService");
        Saves.SaveDirectory = SaveDir;
        _player = GD.Load<PackedScene>(PlayerScene).Instantiate<PlayerCharacter>();
        _player.Name = "Player";
        Game.AddChild(_player);
        await Frames(2);
        Check(World.Player == _player && PlayerRegistry.Current == _player, "搭建：真实玩家 player_v2 接入 WorldGame 和 PlayerRegistry");
    }

    /// <summary>S1_01 默认出生点下面那一行（地面）。</summary>
    private static int FloorRow(RoomGrid grid)
    {
        Vector2I p = grid.Find(RoomGrid.DefaultSpawn).Value;
        return p.Y + 1;
    }

    private async Task Place(string room, StringName spawn, float? x = null)
    {
        RealKeys.ReleaseAll();
        await WaitUntil(() => !World.IsTransitioning, 240);
        World.LoadRoomImmediate(room, spawn);
        Vector2 at = x.HasValue ? new Vector2(x.Value, _floorY - HalfBody - 1f) : _player.GlobalPosition;
        _player.ResetForLab(at, 0);
        await Frames(15);
    }

    /// <summary>按住方向键直到条件成立或超时，返回是否成立。</summary>
    private async Task<bool> Hold(Key key, System.Func<bool> until, int frames)
    {
        RealKeys.Down(key);
        bool done = await WaitUntil(until, frames);
        RealKeys.Up(key);
        await Frames(2);
        return done;
    }

    /// <summary>立在 S1_01 地面上的一根柱子（文字图坐标）。</summary>
    private IEnumerable<Vector2I> Pillar(int col0, int width, int height)
    {
        int floor = Mathf.RoundToInt((_floorY + _shift) / RoomGrid.Tile);
        for (int c = col0; c < col0 + width; c++)
            for (int r = floor - height; r < floor; r++)
                yield return new Vector2I(c, r);
    }

    private float Left(int col) => col * RoomGrid.Tile;

    // ---------- ① 条件地形 ----------

    private async Task ConditionalChecks()
    {
        await Place("S1_01", "Default", 560f);
        const int col = 22;
        ConditionalTerrain terrain = ConditionalTerrain.Create(Pillar(col, 2, 3), Flag, _shift);
        World.CurrentRoom.AddChild(terrain);
        await Frames(2);
        Check(!terrain.IsShown && !terrain.Visible && !terrain.CollisionEnabled, "条件地形：条件不成立时不可见、没有碰撞");
        bool passed = await Hold(RealKeys.Right, () => _player.GlobalPosition.X > Left(col + 2) + 40f, 120);
        Check(passed, $"条件地形：条件不成立时玩家直接走过柱子所在的位置（x={_player.GlobalPosition.X:0}）");

        // 玩家站在会出现的位置 → 条件成立 → 先不出现
        _player.ResetForLab(new Vector2(Left(col) + RoomGrid.Tile, _floorY - HalfBody - 1f), 0);
        await Frames(10);
        Check(terrain.PlayerOverlaps(), $"条件地形：玩家站在柱子的位置上（{_player.GlobalPosition}）");
        GameSession.Progress.SetVar(Flag, true);
        Check(terrain.IsWaiting && !terrain.IsShown && !terrain.CollisionEnabled, "条件地形：条件成立时玩家和它重叠 → 先不出现，等玩家离开");
        await Frames(20);
        Check(terrain.IsWaiting && !terrain.IsShown && !terrain.Visible, "条件地形：玩家一直站着，20 帧后仍不出现");

        // 离开 → 出现；出现那一刻玩家已经完全离开柱子
        float rightEdgeWhenShown = float.NaN;
        RealKeys.Down(RealKeys.Left);
        for (int i = 0; i < 120 && !terrain.IsShown; i++)
        {
            await Frames(1);
            if (terrain.IsShown) rightEdgeWhenShown = _player.GlobalPosition.X + HalfBody;
        }
        RealKeys.Up(RealKeys.Left);
        await Frames(5);
        Check(terrain.IsShown && terrain.Visible && terrain.CollisionEnabled && !terrain.IsWaiting, "条件地形：玩家走开后出现（可见、有碰撞）");
        Check(rightEdgeWhenShown <= Left(col) + 0.5f, $"条件地形：出现时玩家右边缘 {rightEdgeWhenShown:0.0} 已在柱子左边 {Left(col)} 之外（没有把人卡在里面）");
        await Hold(RealKeys.Right, () => false, 60);
        Check(_player.GlobalPosition.X + HalfBody <= Left(col) + 1f, $"条件地形：出现后挡路（玩家右边缘 {_player.GlobalPosition.X + HalfBody:0.0}，柱子左边 {Left(col)}）");
        terrain.QueueFree();

        // 不重叠时条件一成立就同一刻出现
        var instant = ConditionalTerrain.Create(Pillar(28, 1, 2), Flag2, _shift);
        World.CurrentRoom.AddChild(instant);
        await Frames(1);
        bool hiddenBefore = !instant.IsShown;
        GameSession.Progress.SetVar(Flag2, true);
        Check(hiddenBefore && instant.IsShown && instant.CollisionEnabled, "条件地形：玩家不在那里时，条件成立的同一刻就出现（不等下一帧）");
        GameSession.Progress.SetVar(Flag2, false);
        Check(!instant.IsShown && !instant.Visible && !instant.CollisionEnabled, "条件地形：条件改回不成立就消失（跟着进度走，不自己记状态）");
        instant.QueueFree();

        // 进房间时条件已成立：直接出现（即使玩家站在那里也不等）
        await Place("S1_01", "Default", Left(col) + RoomGrid.Tile);
        var entering = ConditionalTerrain.Create(Pillar(col, 2, 3), Flag, _shift);
        World.CurrentRoom.AddChild(entering);
        Check(entering.IsShown && entering.Visible && entering.CollisionEnabled, "条件地形：进房间时条件已成立 → 加进场景树（_Ready）就直接出现");
        entering.QueueFree();
        await Frames(2);

        // 存档 → 重置 → 读档：条件标记恢复，新进房间的条件地形直接出现
        bool loaded = await SaveResetLoad();
        Check(loaded && GameSession.Progress.Flag(Flag), "条件地形：存档 → 重置 → 读档后，条件标记恢复");
        await Place("S1_01", "Default", 400f);
        var afterLoad = ConditionalTerrain.Create(Pillar(col, 2, 3), Flag, _shift);
        World.CurrentRoom.AddChild(afterLoad);
        Check(afterLoad.IsShown && afterLoad.CollisionEnabled, "条件地形：读档后进房间，条件地形直接出现");
        afterLoad.QueueFree();
        await Frames(2);
    }

    /// <summary>在 S1_04 存档点存档 → GameSession 重置（模拟回主菜单）→ 从磁盘读档，等读档的换房完成。</summary>
    private async Task<bool> SaveResetLoad()
    {
        await Place("S1_04", "Save");
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SaveDir));
        bool saved = Saves.SaveToDisk("S1_04");
        GameSession.Reset();
        GameSession.Progress.Unlock("block");
        GameSession.Progress.Unlock("switch_form");
        bool wiped = !GameSession.Progress.HasVar(Flag);
        string loadedRoom = null;
        void OnLoaded(string room) => loadedRoom = room;
        Events.Instance.GameLoaded += OnLoaded;
        bool started = Saves.LoadFromDisk(0);
        bool arrived = await WaitUntil(() => loadedRoom != null && !World.IsTransitioning, 600);
        Events.Instance.GameLoaded -= OnLoaded;
        Check(saved && wiped && started && arrived && loadedRoom == "S1_04", $"存档读档：存档 {saved}、重置后清空 {wiped}、读档完成回到 {loadedRoom}");
        return saved && started && arrived;
    }

    // ---------- ② 可破坏地形 ----------

    private async Task BreakableChecks(RoomGrid source)
    {
        // 按文字图生成：在 S1_01 的文字图里放一块 B（第 20 列，2 格高）和一块 H（第 28 列，2 格高）
        RoomGrid grid = RoomGrid.Parse(source.Serialize(), _s101, _cell, out _);
        int floor = FloorRow(grid);
        foreach (int r in new[] { floor - 2, floor - 1 })
        {
            grid.Set(20, r, RoomGrid.Breakable);
            grid.Set(28, r, RoomGrid.HiddenBreakable);
        }
        List<BreakableTerrain> Build() => GeometryBuilder.BreakableBlocks(_s101, grid);
        List<BreakableTerrain> first = Build();
        Check(first.Count == 2 && first[0].BreakKey == "broken.S1_01.0" && !first[0].HiddenWall && first[1].BreakKey == "broken.S1_01.1" && first[1].HiddenWall,
            $"可破坏地形：生成工具按连通块分块、序号按生成顺序（{string.Join(",", first.Select(b => b.BreakKey))}）");

        await Place("S1_01", "Default", 560f);
        Node2D container = Inject(first);
        await Frames(2);
        BreakableTerrain wall = first[0], hidden = first[1];
        Check(!wall.HurtBox.HeavyOnly && wall.CollisionLayer == Layers.World && wall.HurtBox.CollisionLayer == Layers.EnemyHurt,
            "可破坏地形：World 层实心体 + 敌人受击层的受击框，不要求重击（HeavyOnly=false）");

        // 外观：H 和 # 用同一个图块，B 用裂纹图块
        var geometry = World.CurrentRoom.GetNode<TileMapLayer>("Geometry");
        Vector2I solidCell = geometry.GetUsedCells().First(c => grid.At(c.X, c.Y) == RoomGrid.Solid);
        Vector2I solidAtlas = geometry.GetCellAtlasCoords(solidCell);
        Vector2I hiddenAtlas = hidden.Tiles.GetCellAtlasCoords(hidden.Cells[0]);
        Vector2I crackedAtlas = wall.Tiles.GetCellAtlasCoords(wall.Cells[0]);
        bool sameSet = hidden.Tiles.TileSet == geometry.TileSet && hidden.Tiles.GetCellSourceId(hidden.Cells[0]) == geometry.GetCellSourceId(solidCell);
        Check(sameSet && hiddenAtlas == solidAtlas && hidden.Cells.All(c => hidden.Tiles.GetCellAtlasCoords(c) == solidAtlas),
            $"外观：H 的每一格和 Geometry 里的 # 用同一个瓦片集、同一个图块 {solidAtlas}（H {hiddenAtlas}）");
        Check(crackedAtlas == TerrainTiles.CrackedAtlas && crackedAtlas != solidAtlas, $"外观：B 用裂纹图块 {crackedAtlas}，和 # 不同");
        Check(!hidden.Tiles.CollisionEnabled && !wall.Tiles.CollisionEnabled, "外观：显示用的瓦片层不带碰撞（碰撞由实心体负责）");

        // 挡路
        await Hold(RealKeys.Right, () => false, 60);
        float blockedRight = _player.GlobalPosition.X + HalfBody;
        Check(blockedRight <= Left(20) + 1f && _player.IsOnFloor(), $"可破坏地形：没打碎时挡路（玩家右边缘 {blockedRight:0.0}，墙左边 {Left(20)}）");

        // 普通攻击打碎
        bool heavy = true;
        int kills = 0;
        void OnKilled(Hit hit) { kills++; heavy = hit.Heavy; }
        wall.HurtBox.Killed += OnKilled;
        RealKeys.Down(RealKeys.Attack);
        await Frames(1);
        RealKeys.Up(RealKeys.Attack);
        bool broke = await WaitUntil(() => wall.Broken, 60);
        wall.HurtBox.Killed -= OnKilled;
        Check(broke && kills == 1 && !heavy, $"可破坏地形：按 J 普通攻击一下就碎（碎了 {broke}，击杀 {kills} 次，重击 {heavy}）");
        Check(GameSession.Progress.Flag("broken.S1_01.0") && !GameSession.Progress.HasVar("broken.S1_01.1"), "可破坏地形：碎了写进度变量 broken.S1_01.0 = true（另一块没写）");
        Check(wall.CollisionLayer == 0 && !wall.Visible && !wall.HurtBox.Receiving, "可破坏地形：碎后没有碰撞、不可见、不再受击");
        bool through = await Hold(RealKeys.Right, () => _player.GlobalPosition.X > Left(21) + 40f, 120);
        Check(through, $"可破坏地形：碎后能走过去（x={_player.GlobalPosition.X:0}）");
        container.QueueFree();

        // 离开再回来：重新进 S1_01，按同一张文字图生成的节点一进来就是碎的
        await Place("S1_02", "From_S1_01");
        await Place("S1_01", "Default", 560f);
        List<BreakableTerrain> again = Build();
        Node2D container2 = Inject(again);
        await Frames(2);
        Check(again[0].Broken && again[0].CollisionLayer == 0 && !again[0].Visible, "可破坏地形：离开再回来，B 仍是碎的");
        Check(!again[1].Broken && again[1].CollisionLayer == Layers.World && again[1].Visible, "可破坏地形：没打过的 H 还在");
        bool walk = await Hold(RealKeys.Right, () => _player.GlobalPosition.X > Left(21) + 40f, 120);
        Check(walk, "可破坏地形：离开再回来后能直接走过 B 原来的位置");
        container2.QueueFree();

        // 存档读档后仍是碎的
        bool loaded = await SaveResetLoad();
        Check(loaded && GameSession.Progress.Flag("broken.S1_01.0"), "可破坏地形：读档后进度变量 broken.S1_01.0 还在");
        await Place("S1_01", "Default", 560f);
        List<BreakableTerrain> afterLoad = Build();
        Inject(afterLoad);
        await Frames(2);
        Check(afterLoad[0].Broken && afterLoad[0].CollisionLayer == 0 && !afterLoad[1].Broken, "可破坏地形：读档后进房间，B 仍是碎的，H 还在");
    }

    private Node2D Inject(List<BreakableTerrain> blocks)
    {
        var container = new Node2D { Name = "TestBreakables" };
        foreach (BreakableTerrain block in blocks) container.AddChild(block);
        World.CurrentRoom.AddChild(container);
        return container;
    }
}
