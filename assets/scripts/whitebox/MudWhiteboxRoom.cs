using Godot;
using System.Collections.Generic;
using GDictionary = Godot.Collections.Dictionary;
using Kind = MudActionState.Kind;

public partial class MudWhiteboxRoom : Node2D
{
    [Export] public string RoomId { get; set; } = "M01";
    public string RoomTitle { get; private set; } = "";
    public GDictionary Data { get; private set; }
    public GDictionary RoomCatalog { get; private set; }
    public Vector2 RoomSize { get; private set; }
    public float FloorY { get; private set; }
    public MudWhiteboxGeometry Geometry { get; } = new();
    public MudWhiteboxEvents Events { get; } = new();
    public MudPlayer Player { get; private set; }
    public MudDebugHud Hud { get; private set; }
    public string CurrentPrompt { get; private set; } = "";
    public bool BossActive { get; private set; }
    public MudWhiteboxEnemy Boss { get; private set; }
    public Node2D Spawns { get; private set; }
    public List<Rect2> WaterRects { get; } = new();
    public List<StaticBody2D> WaterSolids { get; } = new();
    public MudBreakable TrialBreakable { get; set; }
    public int LocalSequence { get; set; }
    public bool SourceEnabled { get; set; }
    public float SourceClock { get; private set; } = 1f;
    public int SourceId { get; private set; }
    public float SourceActive { get; private set; }
    public float SourceX { get; set; }
    public List<Rect2> FloorBlocks { get; } = new();
    private Color _waterColor = new(0.22f, 0.55f, 0.65f, 0.5f);
    public float FloodTimer { get; set; } = -1f;

    public override void _Ready()
    {
        var world = Json.ParseString(FileAccess.GetFileAsString("res://assets/data/whitebox/world.json")).AsGodotDictionary();
        RoomCatalog = world.Dict("rooms");
        Data = RoomCatalog.Dict(RoomId);
        RoomTitle = Data.Str("title");
        RoomSize = Data.Point("size");
        FloorY = RoomSize.Y - 64f;
        if (RoomId == "TEST" && !MudProgress.Instance.TestMode) MudProgress.Instance.EnterTest();
        Spawns = new Node2D { Name = "Spawns" };
        AddChild(Spawns);
        SpawnMarker("Start", new Vector2(208, FloorY - 24));
        Geometry.Room = this;
        Geometry.Name = "WhiteboxGeometry";
        AddChild(Geometry);
        Geometry.BuildGeometry();
        Events.Room = this;
        BuildConnections();
        BuildEvents();
        Player = GD.Load<PackedScene>("res://assets/scenes/player/player.tscn").Instantiate<MudPlayer>();
        Player.Name = "Player";
        Player.CollisionLayer = 2;
        Player.CollisionMask = 1;
        AddChild(Player);
        Player.Position = Spawns.GetNode<Marker2D>("Start").Position;
        Player.AddChild(new Camera2D
        {
            Name = "Camera2D", LimitLeft = 0, LimitTop = 0, LimitRight = (int)RoomSize.X, LimitBottom = (int)RoomSize.Y,
            Position = new Vector2(0, -80), PositionSmoothingEnabled = true, PositionSmoothingSpeed = 8f
        });
        Hud = new MudDebugHud { Room = this };
        AddChild(Hud);
        Player.Died += OnPlayerDied;
        if (RoomId == "M04" || RoomId == "H02") MudProgress.Instance.SetCheckpoint(RoomId, "Start");
        ShowMessage(RoomTitle, 2.5f);
        QueueRedraw();
    }
    public void SpawnMarker(string key, Vector2 at) => Spawns.AddChild(new Marker2D { Name = key, Position = at });
    public void BuildConnections()
    {
        foreach (var value in Data.Array("ports"))
        {
            var info = value.AsGodotDictionary();
            var target = info.Str("target");
            var portal = new MudRoomPortal
            {
                Name = info.Str("id"), InterfaceId = info.Str("id"), TargetRoom = MudRoomTravel.Instance.RoomPath(target),
                TargetSpawn = info.Str("spawn"), Requirements = info.Array("requires").Strings(), Managed = true, Room = this,
                Title = "前往" + RoomCatalog.Dict(target).Str("title").Split('·')[0], Position = info.Point("position"),
                ShortcutFlag = info.Str("shortcut"), BreakFromThisSide = info.Bool("break_side"),
                BidirectionalAfterBreak = info.Bool("bidirectional_after_break", true)
            };
            AddChild(portal);
            SpawnMarker("From" + target, info.Point("arrival"));
        }
    }
    public void BuildEvents()
    {
        foreach (var value in Data.Array("events"))
        {
            var info = value.AsGodotDictionary();
            AddChild(new MudRoomEvent
            {
                Name = info.Str("id"), EventId = info.Str("id"), Title = info.Str("title"), Position = info.Point("position"),
                Room = this, Used = MudProgress.Instance.Flag(RoomId + ":" + info.Str("id"))
            });
        }
    }
    public void ShowMessage(string text, float duration = 8f) => Hud?.Say(text, duration);
    public void PerformEvent(string key, MudRoomEvent source) => Events.Perform(key, source);
    public void StartBoss()
    {
        var progress = MudProgress.Instance;
        if (BossActive || progress.Flag("boss_" + RoomId))
        { ShowMessage(!BossActive ? "此处战斗已完成。" : "战斗进行中：门已锁定。"); return; }
        BossActive = true;
        progress.SetCheckpoint(RoomId, "Start");
        var kind = RoomId switch { "T04" => "dragon", "H03" => "final", _ => "keeper" };
        var title = kind switch { "keeper" => "守坑人", "dragon" => "母神之兽", _ => "塔姆图" };
        Boss = SpawnEnemy(new Vector2(RoomSize.X * 0.58f, FloorY - 72), title, kind, kind == "keeper" ? 160 : 220);
        Boss.VulnerabilityMultiplier = Player.Rules.BossVulnerability;
        Boss.Defeated += OnBossDefeated;
    }
    public MudWhiteboxEnemy SpawnEnemy(Vector2 at, string title, string kind, float hp)
    {
        var enemy = new MudWhiteboxEnemy { Position = at, DisplayName = title, BossKind = kind, MaxHealth = hp };
        AddChild(enemy);
        if (kind == "dummy") { enemy.SetPhysicsProcess(false); enemy.Label.Text = $"{title}\n{hp}/{hp}"; }
        return enemy;
    }
    public void OnBossDefeated()
    {
        BossActive = false;
        MudProgress.Instance.Mark("boss_" + RoomId);
        ShowMessage(RoomId switch
        {
            "M05" => "守坑人破碎，露出握着芦苇杖的老人：祂……醒了……",
            "T04" => "龙蛇外壳破碎，工匠落下：祂……爱过我们……",
            "H03" => "父亲的身体片片碎裂，下面露出的是自己的脸。", _ => "战斗完成"
        });
    }
    public void OnPlayerDied() => ShowMessage("身躯破碎。按R重试。", 999);
    public void DebugUnlock()
    {
        if (!MudProgress.Instance.TestMode) { ShowMessage("调试解锁仅允许在F2测试场使用。"); return; }
        foreach (var key in new[] { "block", "form_switch", "double_jump", "wall_climb", "heavy_slam", "dive" }) MudProgress.Instance.Unlock(key);
        ShowMessage("测试能力已开放，不会写入正式存档；未开放高跳升级。");
    }
    public void OpenMenu()
    {
        MudProgress.Instance.LeaveTest();
        GetTree().ChangeSceneToFile("res://assets/scenes/ui/main_menu.tscn");
    }
    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(Player)) return;
        var progress = MudProgress.Instance;
        if (Input.IsActionJustPressed("retry")) MudRoomTravel.Instance.Retry();
        if (Input.IsActionJustPressed("menu")) OpenMenu();
        if (Input.IsActionJustPressed("open_lab")) { progress.EnterTest(); MudRoomTravel.Instance.EnterRoom("TEST", "Start", false); }
        if (Input.IsActionJustPressed("restart_flow")) { progress.NewGame(); MudRoomTravel.Instance.EnterRoom("M01", "Start", false); }
        if (Input.IsActionJustPressed("debug_unlock")) DebugUnlock();
        if (Input.IsActionJustPressed("toggle_module"))
        {
            if ((progress.TestMode || RoomId == "M04" || RoomId == "C03" || RoomId == "H02") && Player.IsOnFloor() && Player.Action.State == Kind.Free)
            { progress.MudModule = 1 - progress.MudModule; ShowMessage($"泥形态跳跃：{(progress.MudModule == 0 ? "重跳" : "长按跳跃")}"); }
            else ShowMessage("仅可在测试场或休整房间落地待机时切换模组。");
        }
        Node2D nearest = null;
        var distance = 100f;
        foreach (var node in GetTree().GetNodesInGroup("interactables"))
        {
            if (node is not Node2D point || node is not IMudInteractable) continue;
            var d = Player.GlobalPosition.DistanceTo(point.GlobalPosition);
            if (d < distance) { nearest = point; distance = d; }
        }
        CurrentPrompt = nearest is IMudInteractable interactable ? "[E] " + interactable.Title : "";
        if (nearest is IMudInteractable selected && Input.IsActionJustPressed("interact") && Player.Action.State == Kind.Free) selected.Interact(Player);
        if (FloodTimer >= 0f)
        {
            FloodTimer -= (float)delta;
            _waterColor.A = 0.8f;
            if (FloodTimer <= 0f)
            { progress.Mark("flood_done"); FloodTimer = -1f; _waterColor.A = 0.3f; ShowMessage("洪水退去，后续通路开放。可读取「洪水之后」。"); }
            QueueRedraw();
        }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (!GodotObject.IsInstanceValid(Player)) return;
        var progress = MudProgress.Instance;
        Player.Submerged = false;
        foreach (var rect in WaterRects) if (rect.HasPoint(Player.GlobalPosition)) Player.Submerged = true;
        foreach (var surface in WaterSolids)
            if (GodotObject.IsInstanceValid(surface)) surface.CollisionLayer = progress.HasAbility("dive") && Player.Form == 0 ? 0u : 1u;
        if (RoomId == "T01" && !progress.Flag("tutorial_double") && Player.JumpsUsed >= 2 && Player.GlobalPosition.Y < FloorY - 165f) progress.Mark("tutorial_double");
        if (RoomId == "T02" && !progress.Flag("tutorial_wall") && progress.HasAbility("wall_climb") && Player.GlobalPosition.Y < FloorY - 400f) progress.Mark("tutorial_wall");
        if (SourceEnabled)
        {
            SourceClock -= (float)delta;
            if (SourceClock <= 0f) { SourceClock = 2f; SourceActive = 0.2f; SourceId++; }
            if (SourceActive > 0f)
            {
                SourceActive -= (float)delta;
                if (Mathf.Abs(Player.Position.X - SourceX) < 150f && Mathf.Abs(Player.Position.Y - (FloorY - 24f)) < 90f)
                    Player.ReceiveAttack(8, $"source_{SourceId}", new Vector2(SourceX, FloorY - 24));
            }
            QueueRedraw();
        }
        if (Player.Position.Y > RoomSize.Y + 100f) Player.ReceiveAttack(999, $"fall_{Time.GetTicksMsec()}", Vector2.Zero);
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, RoomSize), new Color("17252d"));
        for (var x = 0; x < (int)RoomSize.X; x += 64) DrawLine(new Vector2(x, 0), new Vector2(x, RoomSize.Y), new Color(0.25f, 0.35f, 0.38f, 0.18f));
        for (var y = 0; y < (int)RoomSize.Y; y += 64) DrawLine(new Vector2(0, y), new Vector2(RoomSize.X, y), new Color(0.25f, 0.35f, 0.38f, 0.18f));
        foreach (var rect in WaterRects) DrawRect(rect, _waterColor);
        if (SourceEnabled) DrawRect(new Rect2(SourceX - 150, FloorY - 170, 300, 170), new Color(1f, 0.3f, 0.2f, SourceActive > 0f ? 0.5f : 0.12f));
    }
}
