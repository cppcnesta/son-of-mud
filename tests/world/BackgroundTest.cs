using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Camera;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 房间背景“按命名导入一次，之后手动调”（计划_背景导入按钮）。从正式入口 main_game.tscn 开新游戏，用真实镜头。
/// 导入直接在当前房间里执行（和编辑器按钮走同一份 PlanImport / Apply）；“盖满画面”一律在屏幕坐标下判定：
/// Image 的 GetGlobalTransformWithCanvas() × GetRect() 必须盖住整个可见画面。
/// </summary>
public partial class BackgroundTest : FlowTestBase
{
    protected override string SuiteName => "background";
    protected override string ResultPrefix => "world";

    private const string FixtureRoot = "res://tests/world/fixtures/bg";
    /// <summary>s1 下有区域通用的 tool_tree，以及 S1_03 专属的 tool_tree（更大）和 tool_big_cloud。</summary>
    private const string RoomToolRoot = "res://tests/world/fixtures/bg_room";
    private const string MissingRoot = "res://tests/world/fixtures/bg_none";
    /// <summary>分张：S1_03 专属 front_01/02/03、单张 front、far_01/02；区域通用 front_02。</summary>
    private const string PiecesRoot = "res://tests/world/fixtures/bg_pieces";
    /// <summary>分张：S1_03 专属 front_01–04（和 PiecesRoot 不同的图）。</summary>
    private const string PiecesMoreRoot = "res://tests/world/fixtures/bg_pieces_more";
    /// <summary>分张写法重复：front_1 和 front_01，另有 front_3、编号超出范围的 front_20。</summary>
    private const string PiecesDupRoot = "res://tests/world/fixtures/bg_pieces_dup";

    private Rect2 Screen => GetViewport().GetVisibleRect();
    private Vector2 ViewTopLeft => -GetViewport().GetCanvasTransform().Origin;
    private RoomRoot Room => World?.CurrentRoom;
    private readonly BackgroundSettings _settings = BackgroundSettings.LoadDefault();

    protected override async Task RunChecks()
    {
        PureChecks();
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        Check(started && World != null && Cam != null && Player != null, "正式入口开新游戏成功，有 WorldGame、RoomCamera、玩家");
        if (World == null || Cam == null || Player == null) return;

        await NoAutoBackground();
        await FitChecks("S1_03");
        await FitChecks("S4_02");
        await ReimportChecks();
        await FileLookupChecks();
        await ToolImportChecks();
        await PieceChecks();
        await UpgradeChecks();
        await HandBuiltChecks();
        await ZFillChecks();
        await MigrationFidelityChecks();
        await DepthChecks();
        await OrderChecks();
        await PreviewChecks();
        await UndoRedoChecks();
        await WarningChecks();
        await ParallaxShiftChecks();
        await DynamicChecks();
    }

    // =================================================================
    // 纯计算
    // =================================================================

    private void PureChecks()
    {
        var fixtures = new BackgroundSettings { ArtRoot = FixtureRoot };
        string roomFar = BackgroundImporter.ResolvePath(fixtures, "S1_03", BackgroundImporter.Far);
        string regionFar = BackgroundImporter.ResolvePath(fixtures, "S1_02", BackgroundImporter.Far);
        Check(roomFar == $"{FixtureRoot}/s1/tex_bg_s1_03_far.png", $"查图：有房间专属图时优先用它（{roomFar}）");
        Check(regionFar == $"{FixtureRoot}/s1/tex_bg_s1_far.png", $"查图：没有房间专属图时用区域通用图（{regionFar}）");
        Check(BackgroundImporter.ResolvePath(fixtures, "S1_02", BackgroundImporter.Mid) == null && BackgroundImporter.ResolvePath(fixtures, "S2_01", BackgroundImporter.Far) == null,
            "查图：两种都没有时返回 null（跳过这一层）");

        Vector2 screen = BackgroundImporter.ProjectScreenSize();
        Vector2 s103Mid = BackgroundImporter.RequiredSize(new Vector2(3456, 972), screen, _settings.MidScroll, _settings.ShakeMargin);
        Vector2 s402Far = BackgroundImporter.RequiredSize(new Vector2(2304, 3240), screen, _settings.FarScroll, _settings.ShakeMargin);
        Check(Mathf.CeilToInt(s103Mid.X) == 2328 && Mathf.CeilToInt(s103Mid.Y) == 834, $"尺寸：S1_03 中景至少 2328×834（{s103Mid}）");
        Check(Mathf.CeilToInt(s402Far.X) == 1332 && Mathf.CeilToInt(s402Far.Y) == 1044, $"尺寸：S4_02 远景至少 1332×1044（{s402Far}）");
        Check(BackgroundImporter.TryRoomSize("S4_02", out Vector2 shaft) && shaft == new Vector2(2304, 3240), $"尺寸：房间像素尺寸从房间清单和 MetSys 格子算出（S4_02 = {shaft}）");

        // 你放的 S1_03 中景是 4232×1577：应缩小到刚好盖满 2328×834
        (float scale, _) = BackgroundImporter.CoverFit(new Vector2(4232, 1577), s103Mid, _settings.MidScroll, _settings.ShakeMargin);
        Vector2 shown = new Vector2(4232, 1577) * scale;
        Check(scale < 1f && shown.X >= s103Mid.X - 0.01f && shown.Y >= s103Mid.Y - 0.01f && (Mathf.IsEqualApprox(shown.X, s103Mid.X) || Mathf.IsEqualApprox(shown.Y, s103Mid.Y)),
            $"初始大小：4232×1577 的中景缩小到 {scale:0.###} 倍，刚好盖满（{shown.Round()}）");

        var camera = ResourceLoader.Exists("res://assets/data/camera/camera_settings.tres")
            ? GD.Load<CameraSettings>("res://assets/data/camera/camera_settings.tres") : null;
        float maxShake = (camera ?? new CameraSettings()).MaxShakeOffset;
        Check(_settings.ShakeMargin >= maxShake, $"配置一致：背景的震屏余量 {_settings.ShakeMargin} ≥ 镜头最大震屏 {maxShake}");

        Check(_settings.FrontScroll == new Vector2(0.8f, 0.8f) && _settings.ToolScroll == Vector2.One
              && _settings.ScrollOf(BackgroundImporter.Front) == _settings.FrontScroll && _settings.ScrollOf(BackgroundImporter.Tool) == _settings.ToolScroll,
            $"配置：近景视差 {_settings.FrontScroll}，物件层视差 {_settings.ToolScroll}");

        // 物件命名
        Check(BackgroundImporter.ToolKeyOfPath("res://a/s1/tex_bg_s1_03_tool_tree.png") == "tree"
              && BackgroundImporter.ToolKeyOfPath("res://a/s1/tex_bg_s1_tool_big_cloud.png") == "big_cloud"
              && BackgroundImporter.ToolKeyOfPath("res://a/s1/tex_bg_s1_03_far.png") == null
              && BackgroundImporter.ToolKeyOfPath("res://a/s1/tex_bg_s1_03_tool_.png") == null
              && BackgroundImporter.ToolKeyOfPath(null) == null,
            "物件命名：从文件名认出物件标识（tool_tree → tree，tool_big_cloud → big_cloud；不是物件图返回空）");
        Check(BackgroundImporter.ToolNodeName("tree") == "Tree" && BackgroundImporter.ToolNodeName("big_cloud") == "BigCloud",
            "物件命名：节点名首字母大写（tree → Tree，big_cloud → BigCloud）");

        var region = BackgroundImporter.ResolveTools(fixtures, "S1_03");
        var room = BackgroundImporter.ResolveTools(new BackgroundSettings { ArtRoot = RoomToolRoot }, "S1_03");
        var otherRoom = BackgroundImporter.ResolveTools(new BackgroundSettings { ArtRoot = RoomToolRoot }, "S1_02");
        Check(string.Join(",", region.Keys) == "rock,tree" && region["tree"] == $"{FixtureRoot}/s1/tex_bg_s1_tool_tree.png",
            $"查物件图：只有区域通用图时用区域图（{string.Join(" / ", region.Values)}）");
        Check(string.Join(",", room.Keys) == "big_cloud,tree" && room["tree"] == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_tree.png",
            $"查物件图：同一个物件两边都有时用房间专属图（{string.Join(" / ", room.Values)}）");
        Check(string.Join(",", otherRoom.Keys) == "tree" && otherRoom["tree"] == $"{RoomToolRoot}/s1/tex_bg_s1_tool_tree.png",
            "查物件图：别的房间的专属图不会被拿来用，只用区域图");
        Check(BackgroundImporter.ResolveTools(new BackgroundSettings { ArtRoot = MissingRoot }, "S1_03").Count == 0, "查物件图：目录不存在时没有物件");

        // 分张命名
        Check(BackgroundImporter.PieceOfPath("res://a/s1/tex_bg_s1_03_front_01.png") == ("front", 1)
              && BackgroundImporter.PieceOfPath("res://a/s1/tex_bg_s1_mid_2.png") == ("mid", 2)
              && BackgroundImporter.PieceOfPath("res://a/s1/tex_bg_s1_03_far_19.png") == ("far", 19)
              && BackgroundImporter.PieceOfPath("res://a/s1/tex_bg_s1_03_tool_front_01.png") == null
              && BackgroundImporter.PieceOfPath("res://a/s1/tex_bg_s1_03_front_20.png") == null
              && BackgroundImporter.PieceOfPath("res://a/s1/tex_bg_s1_03_front.png") == null
              && BackgroundImporter.PieceOfPath(null) == null,
            "分张命名：front_01 → 近景第 1 张，mid_2 → 中景第 2 张；物件图 tool_front_01、编号 20、不带编号的都不算分张");
        var pieces = new BackgroundSettings { ArtRoot = PiecesRoot };
        var roomPieces = BackgroundImporter.ResolvePieces(pieces, "S1_03", BackgroundImporter.Front);
        var regionPieces = BackgroundImporter.ResolvePieces(pieces, "S1_02", BackgroundImporter.Front);
        Check(string.Join(",", roomPieces.Keys) == "1,2,3" && roomPieces[2] == $"{PiecesRoot}/s1/tex_bg_s1_03_front_02.png",
            $"查分张图：同一个编号区域图和房间专属图都有时用房间专属的（{string.Join(" / ", roomPieces.Values)}）");
        Check(string.Join(",", regionPieces.Keys) == "2" && regionPieces[2] == $"{PiecesRoot}/s1/tex_bg_s1_front_02.png",
            "查分张图：别的房间只用区域通用的分张图");
        var dupMessages = new List<string>();
        var dup = BackgroundImporter.ResolvePieces(new BackgroundSettings { ArtRoot = PiecesDupRoot }, "S1_03", BackgroundImporter.Front, dupMessages);
        var dupAgain = BackgroundImporter.ResolvePieces(new BackgroundSettings { ArtRoot = PiecesDupRoot }, "S1_03", BackgroundImporter.Front);
        Check(string.Join(",", dup.Keys) == "1,3" && dup[1].EndsWith("tex_bg_s1_03_front_01.png") && dup[3].EndsWith("tex_bg_s1_03_front_3.png")
              && dup.SequenceEqual(dupAgain)
              && dupMessages.Any(m => m.Contains("第 01 张有 2 个文件") && m.Contains("用 tex_bg_s1_03_front_01.png"))
              && dupMessages.Any(m => m.Contains("tex_bg_s1_03_front_20.png") && m.Contains("01–19")),
            $"查分张图：front_1 和 front_01 重复时用两位数的 front_01 并提示；只有 front_3 时照样用；编号 20 超范围跳过并提示（{string.Join(" | ", dupMessages)}）");
    }

    // =================================================================
    // 工具
    // =================================================================

    private static Texture2D Blank(Vector2 size) =>
        ImageTexture.CreateFromImage(Image.CreateEmpty(Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y), false, Image.Format.Rgba8));

    private Vector2 Required(string layer) =>
        BackgroundImporter.RequiredSize(Room_Size, BackgroundImporter.ProjectScreenSize(), _settings.ScrollOf(layer), _settings.ShakeMargin);

    private Vector2 Room_Size => BackgroundImporter.TryRoomSize(Room.RoomId, out Vector2 s) ? s : Vector2.Zero;

    private Node2D Bg => Room?.GetNodeOrNull<Node2D>(BackgroundImporter.NodeName);
    private Sprite2D ImageOf(string layer) => Bg?.GetNodeOrNull<Sprite2D>($"{BackgroundImporter.LayerNodeName(layer)}/{BackgroundImporter.ImageName}");

    private void RemoveBackground()
    {
        Node2D bg = Bg;
        if (bg == null) return;
        Room.RemoveChild(bg);
        bg.QueueFree();
    }

    /// <summary>在当前房间按“每层图 = 需要尺寸 × factor”导入（替换掉已有的背景）。</summary>
    private BackgroundImporter.ImportPlan ImportSized(float factorX, float factorY)
    {
        RemoveBackground();
        var textures = BackgroundImporter.ImageLayers.ToDictionary(l => l, l => Blank(new Vector2(Mathf.Ceil(Required(l).X * factorX), Mathf.Ceil(Required(l).Y * factorY))));
        var plan = BackgroundImporter.PlanImport(Room, At(MissingRoot), l => textures[l]);
        BackgroundImporter.Apply(Room, plan);
        return plan;
    }

    /// <summary>正式配置（视差、余量）+ 指定的素材目录。</summary>
    private BackgroundSettings At(string artRoot) => new()
    {
        ArtRoot = artRoot, FarScroll = _settings.FarScroll, MidScroll = _settings.MidScroll,
        FrontScroll = _settings.FrontScroll, ToolScroll = _settings.ToolScroll, ShakeMargin = _settings.ShakeMargin,
    };

    private BackgroundImporter.ImportPlan Import(string artRoot, Func<string, Texture2D> textures = null)
    {
        var plan = BackgroundImporter.PlanImport(Room, At(artRoot), textures);
        BackgroundImporter.Apply(Room, plan);
        return plan;
    }

    private Node ToolLayer => BackgroundImporter.FindLayer(Bg, BackgroundImporter.Tool);

    /// <summary>Tool 层下标识为 key 的物件节点。</summary>
    private List<Node> ItemsOf(string key) =>
        ToolLayer?.GetChildren().Where(c => BackgroundImporter.ToolKeyOf(c) == key).ToList() ?? new List<Node>();

    private static string PathOf(Node node) => BackgroundImporter.ImageOfItem(node)?.Texture?.ResourcePath ?? "";

    /// <summary>手搭一个 Background（模拟你在编辑器里自己建的节点）。</summary>
    private Node2D BuildBackground(params Node[] children)
    {
        RemoveBackground();
        var bg = new Node2D { Name = BackgroundImporter.NodeName, ZIndex = BackgroundImporter.BackgroundZ };
        foreach (Node child in children) bg.AddChild(child);
        Room.AddChild(bg);
        Room.MoveChild(bg, 0);
        Room.AssignOwner(bg);
        return bg;
    }

    /// <summary>Background 整棵子树的快照（名字、类型、顺序、所有者、元数据、图、变换、视差），用来比对撤销 / 重做前后是否完全一样。</summary>
    private string Snapshot()
    {
        Node2D bg = Bg;
        if (bg == null) return "(无 Background)";
        var lines = new List<string>();
        void Walk(Node node, string indent)
        {
            string line = $"{indent}{node.Name}:{node.GetClass()} owner={(node.Owner == Room ? "房间" : node.Owner?.Name ?? "无")}";
            if (node.HasMeta(BackgroundImporter.ToolMeta)) line += $" meta={node.GetMeta(BackgroundImporter.ToolMeta)}";
            if (node.HasMeta(BackgroundImporter.PieceMeta)) line += $" piece={node.GetMeta(BackgroundImporter.PieceMeta)}";
            if (node.HasMeta(BackgroundImporter.VersionMeta)) line += $" version={node.GetMeta(BackgroundImporter.VersionMeta)}";
            if (node is CanvasItem ci) line += $" z={ci.ZIndex}{(ci.ZAsRelative ? "相对" : "绝对")}";
            // Parallax2D 的 position 运行时由镜头决定，不比；其余变换都比
            if (node is Node2D n2) line += $" rot={n2.Rotation} scale={n2.Scale} skew={n2.Skew}" + (node is Parallax2D ? "" : $" pos={n2.Position}");
            if (node is Sprite2D s) line += $" tex={s.Texture?.GetInstanceId()} centered={s.Centered}";
            if (node is Parallax2D p) line += $" scroll={p.ScrollScale} offset={p.ScrollOffset}";
            lines.Add(line);
            foreach (Node child in node.GetChildren()) Walk(child, indent + "  ");
        }
        Walk(bg, "");
        return $"index={bg.GetIndex()}\n" + string.Join("\n", lines);
    }

    private static Rect2 ScreenRectOf(Sprite2D image)
    {
        Transform2D xf = image.GetGlobalTransformWithCanvas();
        Rect2 local = image.GetRect();
        Vector2 a = xf * local.Position, b = xf * local.End;
        return new Rect2(a.Min(b), (b - a).Abs());
    }

    /// <summary>当前房间里已导入的每一层都盖满画面时返回 null。</summary>
    private string CoverageProblem(string onlyLayer = null)
    {
        foreach (string layer in BackgroundImporter.ImageLayers)
        {
            if (onlyLayer != null && layer != onlyLayer) continue;
            Sprite2D image = ImageOf(layer);
            if (image == null) continue;
            Rect2 r = ScreenRectOf(image);
            if (!r.Grow(0.5f).Encloses(Screen))
                return $"{Room.RoomId} {layer}：屏幕矩形 {r.Position.Round()} 大小 {r.Size.Round()}，没盖住画面（画面左上角 {ViewTopLeft.Round()}）";
        }
        return null;
    }

    /// <summary>镜头推到四个角（镜头范围限住），withShake 时再加朝外的 ±M 偏移；核对画面确实到了极端位置。</summary>
    private async Task<string> CornerProblems(bool withShake, string onlyLayer = null)
    {
        float m = _settings.ShakeMargin;
        Vector2 room = Room.GlobalPosition;
        Cam.SetProcess(false);
        try
        {
            foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                Vector2 offset = withShake ? new Vector2(sx * m, sy * m) : Vector2.Zero;
                Cam.GlobalPosition = room + new Vector2(sx * 100000, sy * 100000);
                Cam.Offset = offset;
                Cam.ResetSmoothing();
                await Frames(2);
                Vector2 expected = new Vector2(sx < 0 ? Cam.LimitLeft : Cam.LimitRight - Screen.Size.X, sy < 0 ? Cam.LimitTop : Cam.LimitBottom - Screen.Size.Y) + offset;
                if (ViewTopLeft.DistanceTo(expected) > 0.5f) return $"{Room.RoomId}：画面没到极端位置（{ViewTopLeft.Round()}，应为 {expected.Round()}）";
                string problem = CoverageProblem(onlyLayer);
                if (problem != null) return problem;
            }
            return null;
        }
        finally
        {
            Cam.Offset = Vector2.Zero;
            Cam.SetProcess(true);
            Cam.SnapToTarget();
        }
    }

    private async Task Teleport(string room, StringName spawn)
    {
        ReleaseInputs();
        World.LoadRoomImmediate(room, spawn);
        await Frames(15);
    }

    // =================================================================
    // 不再自动生成
    // =================================================================

    /// <summary>房间表里第一个还没导入过背景的房间（场景里没有 Background 节点）。美术陆续加进来，挑到的房间会变。</summary>
    private static string FirstRoomWithoutBackground()
    {
        foreach (RoomDef def in GD.Load<WorldLayout>(WorldLayout.DefaultPath).Rooms)
        {
            Node scene = GD.Load<PackedScene>(def.ScenePath).Instantiate();
            bool has = scene.GetNodeOrNull(BackgroundImporter.NodeName.ToString()) != null;
            scene.Free();
            if (!has) return def.Id;
        }
        return null;
    }

    private async Task NoAutoBackground()
    {
        string room = FirstRoomWithoutBackground();
        Check(room != null, $"房间表里有还没导入背景的房间可以测（挑到 {room}）");
        if (room == null) return;
        await Teleport(room, SpawnPoint.DefaultId);
        Check(Bg == null && Room.GetChildren(true).All(c => c.Name != BackgroundImporter.NodeName) && !Room.IsProcessing(),
            "没导入过的房间不会自动生成背景；游戏里房间节点不跑编辑器检查");
        Check(BackgroundImporter.CoverageProblems(Room, _settings).Count == 0, "没有背景的房间不报露底");
    }

    // =================================================================
    // 初始大小：图太大 / 太小 / 刚好，都缩放到刚好盖满；节点结构；四角加震屏都盖满
    // =================================================================

    private async Task FitChecks(string roomId)
    {
        await Teleport(roomId, SpawnPoint.DefaultId);
        foreach (var (name, factor) in new[] { ("图太大（2.5 倍）", 2.5f), ("图太小（0.4 倍）", 0.4f), ("刚好", 1f) })
        {
            var plan = ImportSized(factor, factor * 1.1f);
            await Frames(2);
            Node2D bg = Bg;
            var far = bg?.GetNodeOrNull<Parallax2D>("Far");
            var mid = bg?.GetNodeOrNull<Parallax2D>("Mid");
            var front = bg?.GetNodeOrNull<Parallax2D>("Front");
            bool structure = bg != null && plan.NewBackground == bg && bg.ZIndex == BackgroundImporter.BackgroundZ && Room.GetChild(0) == bg
                             && far != null && mid != null && front != null && bg.GetChildCount() == 3
                             && far.GetIndex() == 0 && mid.GetIndex() == 1 && front.GetIndex() == 2
                             && far.ScrollScale == _settings.FarScroll && mid.ScrollScale == _settings.MidScroll && front.ScrollScale == _settings.FrontScroll
                             && far.ZIndex == _settings.FarZ && mid.ZIndex == _settings.MidZ && front.ZIndex == _settings.FrontZ
                             && !far.ZAsRelative && !mid.ZAsRelative && !front.ZAsRelative
                             && BackgroundImporter.ImageLayers.All(l => ImageOf(l).ZIndex == 0 && ImageOf(l).ZAsRelative)
                             && bg.GetMeta(BackgroundImporter.VersionMeta, 0).AsInt32() == BackgroundImporter.CurrentVersion
                             && new Node[] { bg, far, mid, front, ImageOf("far"), ImageOf("mid"), ImageOf("front") }.All(n => n?.Owner == Room);
            Check(structure, $"{roomId}【{name}】导入后：Background（z -100，第一个子节点，记新版本号）→ Far / Mid / Front（视差 0.15 / 0.5 / 0.8，层级 −100 / −80 / −60 绝对，按这个顺序）→ Image（相对 0），所有者都是房间");
            bool fitted = BackgroundImporter.ImageLayers.All(l =>
            {
                Sprite2D image = ImageOf(l);
                Vector2 shown = image.Texture.GetSize() * image.Scale;
                Vector2 req = Required(l);
                return shown.X >= req.X - 0.01f && shown.Y >= req.Y - 0.01f && (Mathf.Abs(shown.X - req.X) < 1.01f || Mathf.Abs(shown.Y - req.Y) < 1.01f)
                       && (factor > 1 ? image.Scale.X < 1 : factor < 1 ? image.Scale.X > 1 : Mathf.Abs(image.Scale.X - 1) < 0.01f);
            });
            Check(fitted, $"{roomId}【{name}】三层都等比缩放到刚好盖满（远景 ×{ImageOf("far")?.Scale.X:0.###}，中景 ×{ImageOf("mid")?.Scale.X:0.###}，近景 ×{ImageOf("front")?.Scale.X:0.###}）");
            string corner = await CornerProblems(true);
            Check(corner == null, $"{roomId}【{name}】镜头在四个角再加最大震屏，三层都盖满画面" + (corner == null ? "" : "：" + corner));
            Check(BackgroundImporter.CoverageProblems(Room, _settings).Count == 0, $"{roomId}【{name}】编辑器的露底检查也认为没问题");
        }

        var packed = new PackedScene();
        Error packError = packed.Pack(Room);
        SceneState state = packed.GetState();
        var names = Enumerable.Range(0, state.GetNodeCount()).Select(i => state.GetNodeName(i).ToString()).ToList();
        Check(packError == Error.Ok && names.Contains(BackgroundImporter.NodeName) && names.Contains("Far") && names.Contains("Mid") && names.Contains("Front") && names.Count(n => n == "Image") == 3,
            $"{roomId}：保存房间场景时背景节点会被写进去（共 {names.Count} 个节点）");
        RemoveBackground();
    }

    // =================================================================
    // 再次导入：只换图，保留手动调的缩放和位置
    // =================================================================

    private async Task ReimportChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        ImportSized(1f, 1f);
        Sprite2D farImage = ImageOf("far"), midImage = ImageOf("mid");
        farImage.Scale *= 1.3f;
        farImage.Position += new Vector2(-50, -20);
        midImage.Scale *= 1.1f;
        Vector2 farScale = farImage.Scale, farPos = farImage.Position, midScale = midImage.Scale, midPos = midImage.Position;
        ulong bgId = Bg.GetInstanceId();

        Texture2D newFar = Blank(new Vector2(100, 50)), newMid = Blank(new Vector2(120, 60)), newFront = Blank(new Vector2(90, 40));
        Texture2D Pick(string l) => l == "far" ? newFar : l == "mid" ? newMid : newFront;
        var plan = BackgroundImporter.PlanImport(Room, At(MissingRoot), Pick);
        Check(plan.NewBackground == null && plan.NewNodes.Count == 0 && plan.Retextures.Count == 3, $"再次导入：只换三层的图，不新建节点（{string.Join(" / ", plan.Messages)}）");
        BackgroundImporter.Apply(Room, plan);
        await Frames(1);
        Check(Bg.GetInstanceId() == bgId && ImageOf("far") == farImage && farImage.Texture == newFar && midImage.Texture == newMid && ImageOf("front").Texture == newFront
              && farImage.Scale == farScale && farImage.Position == farPos && midImage.Scale == midScale && midImage.Position == midPos,
            "再次导入：图换了，手动调过的缩放和位置原样保留");
        var again = BackgroundImporter.PlanImport(Room, At(MissingRoot), Pick);
        Check(again.Empty && again.Messages.Count == 3, $"同样的图再导入一次：什么都不改（{string.Join(" / ", again.Messages)}）");
        RemoveBackground();
    }

    /// <summary>真的从文件加载（测试素材）：S1_03 有专属远景，中景没有 → 只建远景；之后有了中景图再导入 → 补上中景。</summary>
    private async Task FileLookupChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        var plan = Import(FixtureRoot);
        await Frames(1);
        Check(ImageOf("far")?.Texture?.ResourcePath == $"{FixtureRoot}/s1/tex_bg_s1_03_far.png" && Bg.GetNodeOrNull("Mid") == null && Bg.GetNodeOrNull("Front") == null
              && plan.Messages.Any(m => m.Contains("没有中景图") && m.Contains("tex_bg_s1_03_mid.png"))
              && plan.Messages.Any(m => m.Contains("没有近景图") && m.Contains("tex_bg_s1_03_front.png")),
            $"按命名从文件导入：S1_03 用专属远景，没有中景 / 近景图就跳过并说明找过哪些文件（{string.Join(" / ", plan.Messages)}）");

        Texture2D mid = Blank(Required("mid"));
        var later = Import(FixtureRoot, l => l == "mid" ? mid : null);
        await Frames(1);
        Node midNode = Bg.GetNodeOrNull("Mid");
        Check(later.NewNodes.Count == 1 && midNode != null && midNode.GetIndex() == Bg.GetNode("Far").GetIndex() + 1
              && ImageOf("mid")?.Texture == mid && ImageOf("mid").Owner == Room && later.Retextures.Count == 0 && later.ToolTags.Count == 0,
            "之后有了中景图再导入：往已有的 Background 里补上中景（紧跟在远景后面），远景和物件不动");
        RemoveBackground();
    }

    // =================================================================
    // 物件：每张图一个 Parallax2D（各自的视差和层级）→ Image，记物件标识
    // =================================================================

    private async Task ToolImportChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        var plan = Import(RoomToolRoot);
        await Frames(1);
        Node tool = ToolLayer;
        var cloud = tool?.GetNodeOrNull<Parallax2D>("BigCloud");
        var tree = tool?.GetNodeOrNull<Parallax2D>("Tree");
        Sprite2D treeImage = BackgroundImporter.ImageOfItem(tree);
        Check(plan.NewBackground == Bg && tool != null && tool.GetType() == typeof(Node2D) && tool.Name == "Tool" && tool.GetChildCount() == 2
              && cloud != null && tree != null && cloud.GetIndex() == 0
              && PathOf(tree) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_tree.png" && PathOf(cloud) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_big_cloud.png"
              && tree.GetMeta(BackgroundImporter.ToolMeta).AsString() == "tree" && cloud.GetMeta(BackgroundImporter.ToolMeta).AsString() == "big_cloud"
              && new Node[] { tool, tree, cloud, treeImage }.All(n => n?.Owner == Room),
            $"物件：S1_03 导入成 Background/Tool（收纳用的 Node2D）→ BigCloud、Tree（各自一个 Parallax2D）→ Image，用房间专属图，标识记在物件节点上，所有者是房间（{string.Join(" / ", plan.Messages)}）");
        Check(new[] { tree, cloud }.All(i => i.ScrollScale == _settings.ToolScroll && i.ZIndex == _settings.ToolZ && !i.ZAsRelative)
              && treeImage.ZIndex == 0 && treeImage.ZAsRelative && treeImage.Name == BackgroundImporter.ImageName,
            $"物件：每个物件自己的视差 {_settings.ToolScroll}、层级 {_settings.ToolZ}（绝对）；Image 保持相对 0，跟随物件节点");

        Vector2 req = Required(BackgroundImporter.Tool);
        Vector2 shown = treeImage.Texture.GetSize() * treeImage.Scale;
        Check(!treeImage.Centered && treeImage.Scale.X == treeImage.Scale.Y && shown.X >= req.X - 0.01f && shown.Y >= req.Y - 0.01f
              && (Mathf.Abs(shown.X - req.X) < 1.01f || Mathf.Abs(shown.Y - req.Y) < 1.01f),
            $"物件：初始按整张图等比盖满房间（{shown.Round()}，房间加余量需要 {req.Round()}）");

        var packed = new PackedScene();
        packed.Pack(Room);
        SceneState state = packed.GetState();
        int toolNodes = Enumerable.Range(0, state.GetNodeCount()).Count(i => state.GetNodePath(i).ToString().Contains("Background/Tool/"));
        Check(toolNodes == 4, $"物件：保存房间场景时物件节点和它们的 Image 都会被写进去（{toolNodes} 个）");
        RemoveBackground();
    }

    // =================================================================
    // 分张：一层拆成几张图（front_01 在最上面，编号越大越靠后）
    // =================================================================

    private Node LayerNode(string layer) => BackgroundImporter.FindLayer(Bg, layer);
    private Parallax2D Piece(string layer, int n) => LayerNode(layer)?.GetChildren().OfType<Parallax2D>().FirstOrDefault(c => BackgroundImporter.PieceNumberOf(c, layer) == n);

    private async Task PieceChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        var plan = Import(PiecesRoot);
        await Frames(1);
        Node front = LayerNode(BackgroundImporter.Front);
        Parallax2D f1 = Piece("front", 1), f2 = Piece("front", 2), f3 = Piece("front", 3);
        string P(int n) => $"{PiecesRoot}/s1/tex_bg_s1_03_front_{n:00}.png";
        Check(front != null && front.GetType() == typeof(Node2D) && front.GetChildCount() == 3
              && f1?.Name == "Front01" && f2?.Name == "Front02" && f3?.Name == "Front03" && f1.GetIndex() == 0 && f2.GetIndex() == 1 && f3.GetIndex() == 2
              && PathOf(f1) == P(1) && PathOf(f2) == P(2) && PathOf(f3) == P(3)
              && f1.GetMeta(BackgroundImporter.PieceMeta).AsString() == "front_01" && f3.GetMeta(BackgroundImporter.PieceMeta).AsString() == "front_03"
              && new Node[] { front, f1, f2, f3, BackgroundImporter.ImageOfItem(f2) }.All(n => n?.Owner == Room),
            $"分张：近景三张导入成 Background/Front（收纳用的 Node2D）→ Front01、Front02、Front03（各一个 Parallax2D）→ Image，场景树里 01 在最上面，所有者是房间（{string.Join(" / ", plan.Messages)}）");
        Check(new[] { f1, f2, f3 }.All(f => f.ScrollScale == _settings.FrontScroll && !f.ZAsRelative)
              && f1.ZIndex == _settings.FrontZ && f2.ZIndex == _settings.FrontZ - 1 && f3.ZIndex == _settings.FrontZ - 2
              && EffectiveZ(BackgroundImporter.ImageOfItem(f1)) > EffectiveZ(BackgroundImporter.ImageOfItem(f2))
              && EffectiveZ(BackgroundImporter.ImageOfItem(f2)) > EffectiveZ(BackgroundImporter.ImageOfItem(f3))
              && EffectiveZ(BackgroundImporter.ImageOfItem(f3)) > _settings.MidZ,
            $"分张：视差都是近景的 {_settings.FrontScroll}；层级 {f1.ZIndex}/{f2.ZIndex}/{f3.ZIndex}，01 画在最上面，03 最靠后，仍在中景前面");
        Parallax2D far1 = Piece("far", 1), far2 = Piece("far", 2);
        Check(LayerNode(BackgroundImporter.Far)?.GetType() == typeof(Node2D) && far1 != null && far2 != null
              && far1.ZIndex == _settings.FarZ && far2.ZIndex == _settings.FarZ - 1 && far1.ScrollScale == _settings.FarScroll
              && plan.Messages.Any(m => m.Contains("tex_bg_s1_03_front.png 没用上")),
            "分张：远景同样能分张（Far01 −100、Far02 −101）；同时有单张 front.png 时用分张，提示单张没用上");

        // 手调 Front02 后用另一套图再导入：只换图，补一张 04，调过的都保留
        Sprite2D f2Image = BackgroundImporter.ImageOfItem(f2);
        f2.ScrollScale = new Vector2(0.7f, 0.7f);
        f2.ZIndex = -50;
        f2Image.Position += new Vector2(33, -12);
        f2Image.Scale *= 1.25f;
        Vector2 tunedPos = f2Image.Position, tunedScale = f2Image.Scale;
        var more = Import(PiecesMoreRoot);
        await Frames(1);
        Parallax2D f4 = Piece("front", 4);
        string M(int n) => $"{PiecesMoreRoot}/s1/tex_bg_s1_03_front_{n:00}.png";
        Check(Piece("front", 2) == f2 && f2.ScrollScale == new Vector2(0.7f, 0.7f) && f2.ZIndex == -50 && f2Image.Position == tunedPos && f2Image.Scale == tunedScale
              && PathOf(f1) == M(1) && PathOf(f2) == M(2) && PathOf(f3) == M(3)
              && f4 != null && f4.Name == "Front04" && f4.GetIndex() == 3 && f4.ZIndex == _settings.FrontZ - 3 && front.GetChildCount() == 4
              && more.Retextures.Count == 3 && more.NewNodes.Count == 1,
            $"分张再导入：三张原节点换图（Front02 调过的视差 0.7、层级 −50、位置、缩放都保留），补一张 Front04（层级 {f4?.ZIndex}，排在最后）（{string.Join(" / ", more.Messages)}）");
        var again = BackgroundImporter.PlanImport(Room, At(PiecesMoreRoot));
        Check(again.NewNodes.Count == 0 && again.Retextures.Count == 0 && again.PieceTags.Count == 0,
            "分张再导入：图都一样时什么都不做，不重复建节点");

        // 保存后重新打开，再导入也不重复建
        var packed = new PackedScene();
        packed.Pack(Room);
        var reopened = packed.Instantiate<RoomRoot>();
        Node reFront = BackgroundImporter.FindLayer(reopened.GetNodeOrNull(BackgroundImporter.NodeName), BackgroundImporter.Front);
        var replan = BackgroundImporter.PlanImport(reopened, At(PiecesMoreRoot));
        Check(reFront?.GetChildCount() == 4 && reFront.GetChildren().All(c => c.HasMeta(BackgroundImporter.PieceMeta))
              && replan.NewNodes.Count == 0 && replan.PieceTags.Count == 0 && replan.Retextures.Count == 0,
            "分张：保存、重新打开后分张标识都在，再导入不重复建节点");
        reopened.Free();

        // 露底检查：远景分张时看编号最大的兜底图
        var settings = At(PiecesRoot);
        Sprite2D far2Image = BackgroundImporter.ImageOfItem(far2), far1Image = BackgroundImporter.ImageOfItem(far1);
        bool clean = BackgroundImporter.CoverageProblems(Room, settings, checkNodeMoves: false).Count == 0;
        Vector2 far1Scale = far1Image.Scale;
        far1Image.Scale = far1Scale * 0.3f;
        bool firstIgnored = BackgroundImporter.CoverageProblems(Room, settings, checkNodeMoves: false).Count == 0;
        far1Image.Scale = far1Scale;
        Vector2 far2Scale = far2Image.Scale;
        far2Image.Scale = far2Scale * 0.3f;
        List<string> shrunk = BackgroundImporter.CoverageProblems(Room, settings, checkNodeMoves: false);
        far2Image.Scale = far2Scale;
        Check(clean && firstIgnored && shrunk.Count > 0 && shrunk.All(p => p.Contains("兜底图 Far02")),
            $"露底检查：远景分张时用编号最大的 Far02 兜底；缩小 Far01 不报，缩小 Far02 报“兜底图 Far02 露底”（{shrunk.FirstOrDefault()}）");

        // 已经是单张的 Front：不自动拆
        RemoveBackground();
        var single = new Parallax2D { Name = "Front", ZIndex = -60, ZAsRelative = false };
        single.AddChild(new Sprite2D { Name = "Image", Texture = Blank(new Vector2(64, 36)) });
        BuildBackground(single).SetMeta(BackgroundImporter.VersionMeta, BackgroundImporter.CurrentVersion);
        var kept = Import(PiecesRoot);
        Check(LayerNode(BackgroundImporter.Front) == single && single.GetChildCount() == 1 && single.GetChild(0).Name == "Image"
              && kept.Messages.Any(m => m.Contains("已经是单张图") && m.Contains("先删掉 Front")),
            $"分张：房间里已经是单张的 Front 时不自动拆，提示先删掉再导入（{kept.Messages.FirstOrDefault(m => m.Contains("单张图"))}）");

        // 自己摆的分张（没有标识）：按图片文件名认出编号，补记标识；缺的按编号插到对的位置
        RemoveBackground();
        var mine = new Parallax2D { Name = "Mine", ZIndex = -70, ZAsRelative = false };
        mine.AddChild(new Sprite2D { Name = "Image", Texture = GD.Load<Texture2D>(P(2)) });
        var holder = new Node2D { Name = "Front" };
        holder.AddChild(mine);
        BuildBackground(holder).SetMeta(BackgroundImporter.VersionMeta, BackgroundImporter.CurrentVersion);
        var tagged = Import(PiecesMoreRoot);
        string order = string.Join(",", holder.GetChildren().Select(c => c.Name.ToString()));
        Check(order == "Front01,Mine,Front03,Front04" && mine.GetMeta(BackgroundImporter.PieceMeta, "").AsString() == "front_02" && mine.ZIndex == -70 && PathOf(mine) == M(2)
              && tagged.PieceTags.Count == 1,
            $"分张：自己摆的 Mine 按图片认出是第 02 张，补记标识、换图、层级不动；缺的 01、03、04 按编号插到它前后（{order}）");
        RemoveBackground();
    }

    /// <summary>审查 BI1：区域图生成的物件，改名、手动调整后，换成房间专属图 → 原节点换图，不重复建。</summary>
    private async Task UpgradeChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        Import(FixtureRoot);
        await Frames(1);
        var tree = ItemsOf("tree").FirstOrDefault() as Parallax2D;
        Sprite2D image = BackgroundImporter.ImageOfItem(tree);
        Check(tree != null && tree.Name == "Tree" && PathOf(tree) == $"{FixtureRoot}/s1/tex_bg_s1_tool_tree.png" && ItemsOf("rock").Count == 1,
            "区域图升级：只有区域图时导入，生成 Tree（区域图）和 Rock");
        if (tree == null) return;

        tree.Name = "tooltree";
        tree.ZIndex = -70;
        tree.ScrollScale = new Vector2(0.6f, 0.6f);
        image.Position += new Vector2(321, -45);
        image.Scale *= 0.7f;
        Vector2 pos = image.Position, scale = image.Scale;
        int before = ToolLayer.GetChildCount();

        var plan = Import(RoomToolRoot);
        await Frames(1);
        Check(ItemsOf("tree").Count == 1 && ItemsOf("tree")[0] == tree && tree.Name == "tooltree" && tree.ZIndex == -70 && tree.ScrollScale == new Vector2(0.6f, 0.6f)
              && BackgroundImporter.ImageOfItem(tree) == image && image.Position == pos && image.Scale == scale
              && PathOf(tree) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_tree.png" && plan.Retextures.Count(r => r.image == image) == 1,
            $"区域图升级：加上房间专属图再导入 → 原节点 tooltree 换成专属图，名字、层级、视差、位置、缩放都保留，没有第二棵树（{string.Join(" / ", plan.Messages)}）");
        Check(ToolLayer.GetChildCount() == before + 1 && ItemsOf("big_cloud").Count == 1 && ItemsOf("rock").Count == 1,
            "区域图升级：同时补上新的物件 BigCloud，新目录里没有的 Rock 原样保留");

        var again = BackgroundImporter.PlanImport(Room, At(RoomToolRoot));
        Check(again.Empty, $"区域图升级：再点一次，没有任何变化（{string.Join(" / ", again.Messages)}）");

        // 保存后重新打开
        var packed = new PackedScene();
        Error error = packed.Pack(Room);
        var reopened = packed.Instantiate<RoomRoot>();
        Node reTool = BackgroundImporter.FindLayer(reopened.GetNodeOrNull(BackgroundImporter.NodeName), BackgroundImporter.Tool);
        var reTree = reTool?.GetNodeOrNull<Parallax2D>("tooltree");
        var replan = BackgroundImporter.PlanImport(reopened, At(RoomToolRoot));
        Check(error == Error.Ok && reTree != null && reTree.GetMeta(BackgroundImporter.ToolMeta, "").AsString() == "tree" && reTree.ZIndex == -70
              && BackgroundImporter.ImageOfItem(reTree)?.Position == pos && replan.Empty && reTool.GetChildCount() == before + 1,
            $"区域图升级：保存后重新打开，物件标识和层级都在，再点一次仍然不新增节点（{string.Join(" / ", replan.Messages)}）");
        reopened.Free();
        RemoveBackground();
    }

    /// <summary>你在 S1_03 里那样手建的旧版节点：小写的 front / tool，tool 是 Parallax2D，物件 tooltree 直接放在下面、居中、没有标识。</summary>
    private async Task HandBuiltChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        Texture2D frontTex = Blank(new Vector2(64, 32));
        var front = new Parallax2D { Name = "front", ScrollScale = new Vector2(0.15f, 0.15f) };
        front.AddChild(new Sprite2D { Name = "Image", Centered = false, Texture = frontTex, Position = new Vector2(-30, 116) });
        var tool = new Parallax2D { Name = "tool" };
        var handTree = new Sprite2D { Name = "tooltree", Texture = GD.Load<Texture2D>($"{FixtureRoot}/s1/tex_bg_s1_tool_tree.png"), Position = new Vector2(3047, 658), Scale = new Vector2(0.32f, 0.28f) };
        tool.AddChild(handTree);
        var notes = new Node2D { Name = "Notes" };
        BuildBackground(front, tool, notes);

        var plan = Import(RoomToolRoot, l => l == "front" ? frontTex : null);
        await Frames(1);
        var names = Bg.GetChildren().Select(c => c.Name.ToString()).ToList();
        Check(string.Join(",", names) == "front,tool,Notes" && front.ScrollScale == new Vector2(0.15f, 0.15f) && front.ZIndex == _settings.FrontZ && !front.ZAsRelative,
            $"旧版手建节点：小写的 front / tool 被认出来，没有重复建层；视差不改；front 原来是旧默认的 0，补上层级 {_settings.FrontZ}；你自己的节点不动（{string.Join(",", names)}）");
        var wrapper = tool.GetNodeOrNull<Parallax2D>("tooltree");
        Check(wrapper != null && wrapper.GetIndex() == 0 && wrapper.GetMeta(BackgroundImporter.ToolMeta, "").AsString() == "tree"
              && wrapper.ZIndex == _settings.ToolZ && !wrapper.ZAsRelative && wrapper.ScrollScale == Vector2.One
              && handTree.GetParent() == wrapper && handTree.Name == BackgroundImporter.ImageName && !handTree.HasMeta(BackgroundImporter.ToolMeta)
              && handTree.Centered && handTree.Position == new Vector2(3047, 658) && handTree.Owner == Room && wrapper.Owner == Room
              && PathOf(wrapper) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_tree.png" && plan.Wraps.Count == 1,
            $"旧版手建节点：tooltree 包一层自己的视差节点（名字、位置不变，标识记在外层，层级 {_settings.ToolZ}），并换成专属图（{string.Join(" / ", plan.Messages)}）");
        Check(ItemsOf("big_cloud").Count == 1 && ItemsOf("big_cloud")[0].GetParent() == tool && ItemsOf("big_cloud")[0].Owner == Room
              && Bg.GetMeta(BackgroundImporter.VersionMeta, 0).AsInt32() == BackgroundImporter.CurrentVersion,
            "旧版手建节点：缺的物件 BigCloud 补进你建的 tool；背景记上新版本号");
        Check(BackgroundImporter.PlanImport(Room, At(RoomToolRoot), l => l == "front" ? frontTex : null).Empty, "旧版手建节点：再点一次没有变化");
        RemoveBackground();
    }

    /// <summary>审查 Minor：只在旧版第一次升级时把“0 且相对”当作旧默认补层级；你设的值不动；升级后不再自动补。</summary>
    private async Task ZFillChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        var far = new Parallax2D { Name = "Far" };
        var mid = new Parallax2D { Name = "Mid", ZIndex = -95, ZAsRelative = false };
        var front = new Parallax2D { Name = "Front", ZIndex = 5 };
        BuildBackground(far, mid, front);
        var plan = Import(MissingRoot);
        Check(far.ZIndex == _settings.FarZ && !far.ZAsRelative && mid.ZIndex == -95 && !mid.ZAsRelative && front.ZIndex == 5 && front.ZAsRelative
              && Bg.GetMeta(BackgroundImporter.VersionMeta, 0).AsInt32() == BackgroundImporter.CurrentVersion,
            $"补层级：旧版里还是 0 且相对的 Far 补成 {_settings.FarZ}；你设过的 Mid −95、Front 相对 5 都不动；记上版本号（{string.Join(" / ", plan.Messages)}）");

        far.ZIndex = 0;
        far.ZAsRelative = true;
        var again = Import(MissingRoot);
        Check(again.Empty && far.ZIndex == 0 && far.ZAsRelative, "补层级：升级过以后你再把 Far 改回 0（相对），再点按钮也不会被改掉");
        RemoveBackground();
    }

    /// <summary>旧版 Tool 是有视差、偏移、缩放的 Parallax2D：升级后镜头在任何位置，物件在屏幕上的位置、大小都和原来一样。</summary>
    private async Task MigrationFidelityChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        var tool = new Parallax2D { Name = "Tool", ScrollScale = new Vector2(0.4f, 0.3f), ScrollOffset = new Vector2(20, -10), Scale = new Vector2(1.1f, 1.1f) };
        var rock = new Sprite2D { Name = "Rock", Centered = false, Texture = GD.Load<Texture2D>($"{FixtureRoot}/s1/tex_bg_s1_tool_rock.png"), Position = new Vector2(1500, 300), Scale = new Vector2(30, 30) };
        var deco = new Sprite2D { Name = "Deco", Centered = false, Texture = Blank(new Vector2(40, 20)), Position = new Vector2(2600, 500), Scale = new Vector2(3, 3) };
        tool.AddChild(rock);
        tool.AddChild(deco);
        BuildBackground(tool);
        await Frames(1);

        Vector2[] cams = { new(0, 0), new(900, 300), new(2400, 700) };
        async Task<List<Rect2>> Sample()
        {
            var rects = new List<Rect2>();
            Cam.SetProcess(false);
            foreach (Vector2 c in cams)
            {
                Cam.Offset = Vector2.Zero;
                Cam.GlobalPosition = Room.GlobalPosition + c + Screen.Size / 2;
                Cam.ResetSmoothing();
                await Frames(2);
                rects.Add(ScreenRectOf(rock));
                rects.Add(ScreenRectOf(deco));
            }
            Cam.SetProcess(true);
            Cam.SnapToTarget();
            return rects;
        }

        List<Rect2> before = await Sample();
        var plan = Import(MissingRoot);
        List<Rect2> after = await Sample();
        float worst = before.Zip(after, (a, b) => Mathf.Max(a.Position.DistanceTo(b.Position), a.Size.DistanceTo(b.Size))).Max();
        Check(worst <= 0.5f, $"旧版 Tool 升级：镜头在 3 个位置时，两个物件在屏幕上的位置和大小都和升级前一样（最大差 {worst:0.###} 像素）");
        var rockWrapper = rock.GetParent() as Parallax2D;
        var decoWrapper = deco.GetParent() as Parallax2D;
        Check(rockWrapper != null && decoWrapper != null && rockWrapper.GetParent() == tool && decoWrapper.GetParent() == tool
              && rockWrapper.Name == "Rock" && decoWrapper.Name == "Deco" && rockWrapper.GetIndex() == 0 && decoWrapper.GetIndex() == 1
              && rockWrapper.ScrollScale == new Vector2(0.4f, 0.3f) && rockWrapper.ScrollOffset == new Vector2(20, -10) && rockWrapper.Scale == new Vector2(1.1f, 1.1f)
              && rockWrapper.GetMeta(BackgroundImporter.ToolMeta, "").AsString() == "rock" && !decoWrapper.HasMeta(BackgroundImporter.ToolMeta)
              && tool.ScrollScale == Vector2.One && tool.ScrollOffset == Vector2.Zero && tool.Scale == Vector2.One && plan.Wraps.Count == 2,
            $"旧版 Tool 升级：原来 Tool 的视差 / 偏移 / 缩放搬到每个物件上（认不出的 Deco 也一样包，只是不记标识），Tool 自己恢复成不起作用的收纳节点（{string.Join(" / ", plan.Messages)}）");
        RemoveBackground();
    }

    /// <summary>层级：按 Godot 的规则算出实际层级（绝对值直接用，相对值加上父节点的），核对谁画在谁前面；物件改层级 / 视差后生效。</summary>
    private async Task DepthChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        var textures = BackgroundImporter.ImageLayers.ToDictionary(l => l, l => Blank(Required(l)));
        Import(RoomToolRoot, l => textures[l]);
        await Frames(1);
        var tree = ItemsOf("tree").First() as Parallax2D;
        int far = EffectiveZ(ImageOf("far")), mid = EffectiveZ(ImageOf("mid")), front = EffectiveZ(ImageOf("front"));
        int item = EffectiveZ(BackgroundImporter.ImageOfItem(tree)), player = EffectiveZ(Player), geometry = EffectiveZ(Room.GetNode<CanvasItem>("Geometry"));
        Check(far == _settings.FarZ && mid == _settings.MidZ && front == _settings.FrontZ && item == _settings.ToolZ && far < mid && mid < front && front < item && item < player && player == 0 && geometry == 0,
            $"层级：远景 {far} < 中景 {mid} < 近景 {front} < 物件 {item} < 主角 {player}（地形 {geometry}）");

        tree.ZIndex = 10;
        Check(EffectiveZ(BackgroundImporter.ImageOfItem(tree)) == 10 && EffectiveZ(BackgroundImporter.ImageOfItem(tree)) > player, "层级：物件改成 10 → 画在主角前面");

        tree.ZIndex = -90;
        tree.ScrollScale = new Vector2(0.3f, 0.3f);
        int between = EffectiveZ(BackgroundImporter.ImageOfItem(tree));
        Cam.SetProcess(false);
        Cam.Offset = Vector2.Zero;
        Cam.GlobalPosition = Room.GlobalPosition + new Vector2(900, 500);
        Cam.ResetSmoothing();
        await Frames(2);
        Vector2 v1 = ViewTopLeft, p1 = ScreenRectOf(BackgroundImporter.ImageOfItem(tree)).Position;
        Cam.GlobalPosition = Room.GlobalPosition + new Vector2(2100, 700);
        Cam.ResetSmoothing();
        await Frames(2);
        Vector2 dc = ViewTopLeft - v1, moved = ScreenRectOf(BackgroundImporter.ImageOfItem(tree)).Position - p1, expected = -0.3f * dc;
        Cam.SetProcess(true);
        Cam.SnapToTarget();
        Check(between > far && between < mid && dc.X > 500 && moved.DistanceTo(expected) <= 1f,
            $"层级 + 视差：物件改成 −90、视差 0.3 → 夹在远景和中景之间（{between}），镜头移动 {dc.Round()} 时它移动 {moved.Round()}（应为 {expected.Round()}）");
        RemoveBackground();
    }

    /// <summary>Godot 的实际层级：绝对的直接用自己的值；相对的加上父节点（一直往上到不是 CanvasItem 为止）。</summary>
    private static int EffectiveZ(CanvasItem item)
    {
        int z = 0;
        for (Node node = item; node is CanvasItem ci; node = node.GetParent())
        {
            z += ci.ZIndex;
            if (!ci.ZAsRelative) break;
        }
        return z;
    }

    /// <summary>补层顺序：已有 Tool（前面还有你自己的节点），再补 Far / Mid / Front，必须是 Far → Mid → Front → Tool。</summary>
    private async Task OrderChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        BuildBackground(new Node2D { Name = "Notes" }, new Parallax2D { Name = "Tool" });
        var textures = BackgroundImporter.ImageLayers.ToDictionary(l => l, l => Blank(Required(l)));
        var plan = Import(MissingRoot, l => textures[l]);
        await Frames(1);
        string order = string.Join(",", Bg.GetChildren().Select(c => c.Name.ToString()));
        Check(order == "Notes,Far,Mid,Front,Tool" && plan.NewNodes.Count == 3,
            $"补层顺序：已有 Tool 时补上的三层插在它前面，顺序 Far → Mid → Front → Tool，你自己的节点不动（{order}）");

        BuildBackground(new Parallax2D { Name = "Far" }, new Parallax2D { Name = "Tool" });
        Bg.GetNode("Far").AddChild(new Sprite2D { Name = "Image", Centered = false, Texture = textures["far"] });
        Import(MissingRoot, l => l == "front" ? textures["front"] : null);
        string first = string.Join(",", Bg.GetChildren().Select(c => c.Name.ToString()));
        Import(MissingRoot, l => l == "mid" ? textures["mid"] : null);
        string second = string.Join(",", Bg.GetChildren().Select(c => c.Name.ToString()));
        Check(first == "Far,Front,Tool" && second == "Far,Mid,Front,Tool", $"补层顺序：先补 Front 再补 Mid，每次都插到正确位置（{first} → {second}）");
        RemoveBackground();
    }

    // =================================================================
    // 编辑器视差预览：和游戏里一致；保存时还原；预览里改的 Image 保留
    // =================================================================

    private async Task PreviewChecks()
    {
        // 1) 预览用的算法和游戏运行时一致：在真实房间里把镜头放到同一个位置，比各个 Parallax2D 的位置
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        var textures = BackgroundImporter.ImageLayers.ToDictionary(l => l, l => Blank(Required(l)));
        Import(RoomToolRoot, l => textures[l]);
        var tree = ItemsOf("tree").First() as Parallax2D;
        tree.ScrollScale = new Vector2(0.3f, 0.45f);
        tree.ScrollOffset = new Vector2(15, -8);
        Cam.SetProcess(false);
        float worst = 0;
        int samples = 0;
        foreach (Vector2 c in new Vector2[] { new(0, 0), new(1100, 200), new(2304, 324) })
        {
            Cam.Offset = Vector2.Zero;
            Cam.GlobalPosition = Room.GlobalPosition + c + Screen.Size / 2;
            Cam.ResetSmoothing();
            await Frames(2);
            Vector2 local = ViewTopLeft - Room.GlobalPosition;
            foreach (Parallax2D p in BackgroundImporter.ParallaxNodes(Bg))
            {
                worst = Mathf.Max(worst, p.Position.DistanceTo(BackgroundImporter.RuntimePosition(p, local)));
                samples++;
            }
        }
        Cam.SetProcess(true);
        Cam.SnapToTarget();
        Check(samples >= 15 && worst <= 0.5f, $"预览：用的算法和游戏里一致（镜头 3 个位置 × {samples / 3} 个视差节点，最大差 {worst:0.###} 像素）");
        RemoveBackground();

        // 2) 预览开关和保存：在房间场景的独立实例上做（和编辑器里打开的场景一样，不在游戏里）
        var room = GD.Load<PackedScene>("res://assets/scenes/world/s1/S1_03.tscn").Instantiate<RoomRoot>();
        Node oldBg = room.GetNodeOrNull(BackgroundImporter.NodeName);
        if (oldBg != null) { room.RemoveChild(oldBg); oldBg.Free(); }
        BackgroundImporter.Apply(room, BackgroundImporter.PlanImport(room, At(RoomToolRoot), l => textures[l]));
        Node bg = room.GetNode(BackgroundImporter.NodeName);
        List<Parallax2D> nodes = BackgroundImporter.ParallaxNodes(bg);
        var original = nodes.ToDictionary(p => bg.GetPathTo(p).ToString(), p => p.Position);
        Sprite2D treeImage = BackgroundImporter.ImageOfItem(BackgroundImporter.FindLayer(bg, BackgroundImporter.Tool).GetNode("Tree"));

        room.SetPreview(true, 0.6f, 1f);
        Vector2 cam = room.PreviewCameraTopLeft();
        Vector2 travel = BackgroundImporter.TryRoomSize(room.RoomId, out Vector2 size) ? (size - BackgroundImporter.ProjectScreenSize()).Max(Vector2.Zero) : Vector2.Zero;
        bool previewed = cam.IsEqualApprox(travel * new Vector2(0.6f, 1f)) && nodes.All(p => p.Position.IsEqualApprox(BackgroundImporter.RuntimePosition(p, cam)))
                         && nodes.Any(p => !p.Position.IsEqualApprox(original[bg.GetPathTo(p).ToString()]))
                         && room.Get("preview_parallax").AsBool() && Mathf.IsEqualApprox((float)room.Get("preview_camera_x").AsDouble(), 0.6f);
        Check(previewed, $"预览：打开后各层按镜头位置 {cam.Round()} 显示视差效果");

        treeImage.Position += new Vector2(37, 11);
        Vector2 editedImage = treeImage.Position;
        room.Notification((int)Node.NotificationEditorPreSave);
        bool restoredForSave = nodes.All(p => p.Position == original[bg.GetPathTo(p).ToString()]);
        var packed = new PackedScene();
        Error error = packed.Pack(room);
        room.Notification((int)Node.NotificationEditorPostSave);
        bool previewBack = nodes.All(p => p.Position.IsEqualApprox(BackgroundImporter.RuntimePosition(p, cam)));

        SceneState state = packed.GetState();
        bool previewStored = Enumerable.Range(0, state.GetNodePropertyCount(0)).Any(i => state.GetNodePropertyName(0, i).ToString().StartsWith("preview_"));
        var saved = packed.Instantiate<RoomRoot>();
        Node savedBg = saved.GetNode(BackgroundImporter.NodeName);
        bool savedPositions = BackgroundImporter.ParallaxNodes(savedBg).All(p => p.Position == original[savedBg.GetPathTo(p).ToString()]);
        Vector2 savedImage = BackgroundImporter.ImageOfItem(BackgroundImporter.FindLayer(savedBg, BackgroundImporter.Tool).GetNode("Tree")).Position;
        bool savedPreviewOff = !saved.PreviewOn;
        saved.Free();
        Check(error == Error.Ok && restoredForSave && previewBack && savedPositions && !previewStored && savedPreviewOff && savedImage == editedImage,
            $"预览：保存前自动还原（{restoredForSave}），存下的场景里各层是原来的位置（{savedPositions}）、没有预览开关（{!previewStored}），预览里挪的 Image 保留（{savedImage}）；保存后继续预览（{previewBack}）");

        var probe = new Parallax2D();
        long usage = probe.GetPropertyList().First(d => d["name"].AsString() == "position")["usage"].AsInt64();
        probe.Free();
        Check((usage & (long)PropertyUsageFlags.Storage) == 0,
            $"预览：Godot 本来就不把 Parallax2D 的位置存进场景（usage = {usage}），预览改的位置从机制上写不进场景文件；保存前还原是多一层保险");

        room.SetPreview(false, 0.6f, 1f);
        Check(nodes.All(p => p.Position == original[bg.GetPathTo(p).ToString()]) && treeImage.Position == editedImage,
            "预览：关闭后各层回到原来的位置，预览时挪的 Image 保留");
        room.Free();
    }

    // =================================================================
    // 撤销 / 重做：和编辑器同一份操作，交给普通 UndoRedo
    // =================================================================

    private sealed class UndoRedoSink : BackgroundImporter.IUndoSink
    {
        private readonly UndoRedo _undo;
        public UndoRedoSink(UndoRedo undo) => _undo = undo;
        public void DoMethod(GodotObject target, StringName method, params Variant[] args) => _undo.AddDoMethod(UndoCall.Make(target, method, args));
        public void UndoMethod(GodotObject target, StringName method, params Variant[] args) => _undo.AddUndoMethod(UndoCall.Make(target, method, args));
        public void DoProperty(GodotObject target, StringName property, Variant value) => _undo.AddDoProperty(target, property, value);
        public void UndoProperty(GodotObject target, StringName property, Variant value) => _undo.AddUndoProperty(target, property, value);
        public void DoReference(GodotObject target) => _undo.AddDoReference(target);
    }

    private async Task UndoRedoChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        var textures = BackgroundImporter.ImageLayers.ToDictionary(l => l, l => Blank(Required(l)));

        async Task Case(string name, Action setup, string artRoot, Func<string, Texture2D> pick)
        {
            setup();
            await Frames(1);
            string before = Snapshot();
            var plan = BackgroundImporter.PlanImport(Room, At(artRoot), pick);
            var undo = new UndoRedo();
            undo.CreateAction(name);
            BackgroundImporter.Record(Room, plan, new UndoRedoSink(undo));
            undo.CommitAction();
            await Frames(1);
            string after = Snapshot();
            bool ok = !plan.Empty && after != before;
            string detail = "";
            for (int round = 1; round <= 2 && ok; round++)
            {
                undo.Undo();
                string undone = Snapshot();
                undo.Redo();
                string redone = Snapshot();
                if (undone != before) { ok = false; detail = $"第 {round} 次撤销后不一样：\n{undone}\n应为：\n{before}"; }
                else if (redone != after) { ok = false; detail = $"第 {round} 次重做后不一样：\n{redone}\n应为：\n{after}"; }
            }
            Check(ok, $"撤销 / 重做【{name}】：撤销后和导入前完全一样，重做后和导入后完全一样，来回两次" + (detail == "" ? "" : "：" + detail));
            undo.Free();
            RemoveBackground();
        }

        await Case("新建整个 Background", RemoveBackground, RoomToolRoot, l => textures[l]);
        await Case("往已有 Background 补层", () => BuildBackground(new Node2D { Name = "Notes" }, new Parallax2D { Name = "Tool" }), MissingRoot, l => textures[l]);
        await Case("往已有 Tool 补物件、换图", () =>
        {
            RemoveBackground();
            Import(FixtureRoot);
        }, RoomToolRoot, null);
        await Case("新版物件补记标识、换图", () =>
        {
            var item = new Parallax2D { Name = "tooltree", ZIndex = -40, ZAsRelative = false };
            item.AddChild(new Sprite2D { Name = "Image", Texture = GD.Load<Texture2D>($"{FixtureRoot}/s1/tex_bg_s1_tool_tree.png") });
            var tool = new Node2D { Name = "Tool" };
            tool.AddChild(item);
            BuildBackground(tool).SetMeta(BackgroundImporter.VersionMeta, BackgroundImporter.CurrentVersion);
        }, RoomToolRoot, null);
        await Case("分张：新建 Front、Far 收纳节点和各张", RemoveBackground, PiecesRoot, null);
        await Case("分张：已有分张手调后换图，并补一张 04", () =>
        {
            RemoveBackground();
            Import(PiecesRoot);
            Parallax2D f2 = Piece("front", 2);
            f2.ScrollScale = new Vector2(0.7f, 0.7f);
            f2.ZIndex = -50;
            BackgroundImporter.ImageOfItem(f2).Position += new Vector2(33, -12);
        }, PiecesMoreRoot, null);
        await Case("旧版升级：补层级、物件包装、Tool 复位、换图", () =>
        {
            var front = new Parallax2D { Name = "front" };
            front.AddChild(new Sprite2D { Name = "Image", Texture = textures["front"] });
            var tool = new Parallax2D { Name = "tool", ScrollScale = new Vector2(0.4f, 0.4f), ScrollOffset = new Vector2(5, 5) };
            tool.AddChild(new Sprite2D { Name = "tooltree", Texture = GD.Load<Texture2D>($"{FixtureRoot}/s1/tex_bg_s1_tool_tree.png") });
            var deco = new Sprite2D { Name = "Deco", Texture = textures["front"] };
            deco.SetMeta("note", "自己加的");
            tool.AddChild(deco);
            BuildBackground(front, tool);
        }, RoomToolRoot, null);
    }

    // =================================================================
    // 露底提醒（编辑器警告）与游戏里实际画面一致
    // =================================================================

    private async Task WarningChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        ImportSized(1f, 1f);
        Sprite2D far = ImageOf("far");
        Vector2 scale = far.Scale, pos = far.Position;
        Check(BackgroundImporter.CoverageProblems(Room, _settings).Count == 0, "露底提醒：刚导入时没有提醒");

        far.Scale = scale * 0.8f;
        var shrunk = BackgroundImporter.CoverageProblems(Room, _settings);
        string game = await CornerProblems(true, "far");
        Check(shrunk.Any(p => p.Contains("右下角") && p.Contains("右边") && p.Contains("下边")) && game != null,
            $"露底提醒：远景缩小到 0.8 倍 → 提醒右下角露底，游戏画面里也确实露底（{string.Join(" | ", shrunk.Take(2))}）");

        far.Scale = scale;
        Check(BackgroundImporter.CoverageProblems(Room, _settings).Count == 0 && await CornerProblems(true, "far") == null, "露底提醒：恢复后提醒消失，画面盖满");

        // 只往右挪一点点：不震屏时盖得住，震屏到最左时露底
        float nudge = 0.6f * _settings.FarScroll.X * _settings.ShakeMargin;
        far.Position = pos + new Vector2(nudge, 0);
        var withShake = BackgroundImporter.CoverageProblems(Room, _settings);
        var noShake = BackgroundImporter.CoverageProblems(Room, _settings, withShake: false);
        string gameNoShake = await CornerProblems(false, "far");
        string gameShake = await CornerProblems(true, "far");
        Check(withShake.Any(p => p.Contains("左") && p.Contains("含震屏")) && noShake.Count == 0 && gameNoShake == null && gameShake != null,
            $"露底提醒：远景右移 {nudge:0.##} 像素 → 只在震屏到最左时露底，提醒也这样说，和游戏画面一致（{string.Join(" | ", withShake.Take(1))}）");
        far.Position = pos;

        var farNode = Bg.GetNode<Parallax2D>("Far");
        farNode.Position = new Vector2(200, 0);
        var moved = BackgroundImporter.CoverageProblems(Room, _settings, checkNodeMoves: true);
        Check(moved.Any(p => p.Contains("Background/Far") && p.Contains("Image")), $"露底提醒：挪了 Far 节点本身 → 提醒改调 Image（{moved.FirstOrDefault()}）");
        farNode.Position = farNode.ScrollOffset;
        RemoveBackground();
    }

    // =================================================================
    // 视差位移（屏幕上的位移 = −p × 镜头位移）
    // =================================================================

    private async Task ParallaxShiftChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        ImportSized(1f, 1f);
        Vector2 room = Room.GlobalPosition;
        Cam.SetProcess(false);
        Cam.Offset = Vector2.Zero;
        var before = new Dictionary<string, Vector2>();
        Cam.GlobalPosition = room + new Vector2(900, 500);
        Cam.ResetSmoothing();
        await Frames(2);
        Vector2 v1 = ViewTopLeft;
        foreach (string layer in BackgroundImporter.ImageLayers) before[layer] = ScreenRectOf(ImageOf(layer)).Position;
        Cam.GlobalPosition = room + new Vector2(2100, 700);
        Cam.ResetSmoothing();
        await Frames(2);
        Vector2 dc = ViewTopLeft - v1;
        foreach (string layer in BackgroundImporter.ImageLayers)
        {
            Vector2 p = _settings.ScrollOf(layer);
            Vector2 moved = ScreenRectOf(ImageOf(layer)).Position - before[layer];
            Vector2 expected = -p * dc;
            Check(dc.X > 500 && dc.Y > 50 && moved.DistanceTo(expected) <= 1f,
                $"视差：镜头移动 {dc.Round()}，{layer} 在屏幕上移动 {moved.Round()}（应为 −{p} × 镜头位移 = {expected.Round()}）");
        }
        Cam.SetProcess(true);
        Cam.SnapToTarget();
    }

    // =================================================================
    // 动态逐帧：真实按键跑过 S1_03、两头最大震屏、竖井掉落（镜头平滑开着）；切房间后背景跟着释放
    // =================================================================

    private async Task DynamicChecks()
    {
        Check(Cam.PositionSmoothingEnabled, "动态检查时镜头平滑是开着的");
        int frames = 0;
        var problems = new List<string>();
        async Task Watch(int count, Func<bool> stop = null)
        {
            for (int i = 0; i < count; i++)
            {
                await Frames(1);
                frames++;
                string p = CoverageProblem();
                if (p != null && problems.Count < 5) problems.Add($"第 {frames} 帧 {p}");
                if (stop != null && stop()) return;
            }
        }

        await Teleport("S1_03", "From_S1_02");
        ImportSized(1f, 1f);
        ulong bgId = Bg.GetInstanceId();
        float x0 = Player.GlobalPosition.X;
        Input.ActionPress(PlayerActions.Right);
        await Watch(600, () => World.CurrentRoomDef?.Id != "S1_03" || Player.GlobalPosition.X - Room.GlobalPosition.X > 3300);
        Input.ActionRelease(PlayerActions.Right);
        float run = Player.GlobalPosition.X - x0;

        float maxShake = Cam.Settings.MaxShakeOffset;
        foreach (string spawn in new[] { "From_S1_02", "From_S1_04" })
        {
            Player.GlobalPosition = Room.GetNode<Node2D>($"Spawns/{spawn}").GlobalPosition;
            Player.Velocity = Vector2.Zero;
            Cam.SnapToTarget();
            await Frames(2);
            Cam.Shake(maxShake, 1f);
            await Watch(60);
        }

        await Teleport("S4_02", SpawnPoint.DefaultId);
        bool oldFreed = await WaitUntil(() => !GodotObject.IsInstanceIdValid(bgId), 30);
        ImportSized(1f, 1f);
        Player.GlobalPosition = Room.GlobalPosition + new Vector2(1152, 120);
        Player.Velocity = Vector2.Zero;
        Cam.SnapToTarget();
        float y0 = Player.GlobalPosition.Y;
        await Watch(300);
        float fell = Player.GlobalPosition.Y - y0;

        Check(problems.Count == 0, $"动态逐帧（{frames} 帧，S1_03 跑了 {run:0} 像素，竖井掉了 {fell:0} 像素，边界处震屏 {maxShake}）：每一帧三层都盖满画面"
                                   + (problems.Count == 0 ? "" : "：" + string.Join(" | ", problems)));
        Check(run > 600 && fell > 600, $"动态检查确实动起来了（跑 {run:0}，掉 {fell:0}）");
        Check(oldFreed, "切房间后，旧房间的背景跟着房间一起释放");

        var geometry = Room.GetNodeOrNull<CanvasItem>("Geometry");
        Check(Bg.ZIndex < 0 && geometry != null && geometry.ZIndex >= 0 && Player.ZIndex >= 0,
            $"前后顺序：背景 z = {Bg.ZIndex}，瓦片 z = {geometry?.ZIndex}，玩家 z = {Player.ZIndex}，背景画在最后面");
    }
}
