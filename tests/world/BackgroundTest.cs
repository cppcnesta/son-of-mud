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
        await UpgradeChecks();
        await HandBuiltChecks();
        await OrderChecks();
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

    private static string PathOf(Node node) => (node as Sprite2D)?.Texture?.ResourcePath ?? "";

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
            if (node is Sprite2D s) line += $" tex={s.Texture?.GetInstanceId()} pos={s.Position} scale={s.Scale} centered={s.Centered}";
            if (node is Parallax2D p) line += $" scroll={p.ScrollScale}";
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

    private async Task NoAutoBackground()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
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
                             && new Node[] { bg, far, mid, front, ImageOf("far"), ImageOf("mid"), ImageOf("front") }.All(n => n?.Owner == Room);
            Check(structure, $"{roomId}【{name}】导入后：Background（z -100，第一个子节点）→ Far / Mid / Front（视差 0.15 / 0.5 / 0.8，按这个顺序）→ Image，所有者都是房间");
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
    // 物件层：每张图一个节点，记物件标识
    // =================================================================

    private async Task ToolImportChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        var plan = Import(RoomToolRoot);
        await Frames(1);
        var tool = ToolLayer as Parallax2D;
        var cloud = tool?.GetNodeOrNull<Sprite2D>("BigCloud");
        var tree = tool?.GetNodeOrNull<Sprite2D>("Tree");
        Check(plan.NewBackground == Bg && tool != null && tool.Name == "Tool" && tool.ScrollScale == _settings.ToolScroll && tool.GetChildCount() == 2
              && cloud != null && tree != null && cloud.GetIndex() == 0
              && PathOf(tree) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_tree.png" && PathOf(cloud) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_big_cloud.png"
              && tree.GetMeta(BackgroundImporter.ToolMeta).AsString() == "tree" && cloud.GetMeta(BackgroundImporter.ToolMeta).AsString() == "big_cloud"
              && new Node[] { tool, tree, cloud }.All(n => n.Owner == Room),
            $"物件：S1_03 导入成 Background/Tool（视差 {_settings.ToolScroll}）→ BigCloud、Tree，用房间专属图，记了物件标识，所有者是房间（{string.Join(" / ", plan.Messages)}）");

        Vector2 req = Required(BackgroundImporter.Tool);
        Vector2 shown = tree.Texture.GetSize() * tree.Scale;
        Check(!tree.Centered && tree.Scale.X == tree.Scale.Y && shown.X >= req.X - 0.01f && shown.Y >= req.Y - 0.01f
              && (Mathf.Abs(shown.X - req.X) < 1.01f || Mathf.Abs(shown.Y - req.Y) < 1.01f),
            $"物件：初始按整张图等比盖满房间（{shown.Round()}，房间加余量需要 {req.Round()}）");

        var packed = new PackedScene();
        packed.Pack(Room);
        SceneState state = packed.GetState();
        int toolNodes = Enumerable.Range(0, state.GetNodeCount()).Count(i => state.GetNodePath(i).ToString().Contains("Background/Tool/"));
        Check(toolNodes == 2, $"物件：保存房间场景时物件节点会被写进去（{toolNodes} 个）");
        RemoveBackground();
    }

    /// <summary>审查 BI1：区域图生成的物件，改名、手动调整后，换成房间专属图 → 原节点换图，不重复建。</summary>
    private async Task UpgradeChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        Import(FixtureRoot);
        await Frames(1);
        Sprite2D tree = ItemsOf("tree").FirstOrDefault() as Sprite2D;
        Check(tree != null && tree.Name == "Tree" && PathOf(tree) == $"{FixtureRoot}/s1/tex_bg_s1_tool_tree.png" && ItemsOf("rock").Count == 1,
            "区域图升级：只有区域图时导入，生成 Tree（区域图）和 Rock");
        if (tree == null) return;

        tree.Name = "tooltree";
        tree.Position += new Vector2(321, -45);
        tree.Scale *= 0.7f;
        Vector2 pos = tree.Position, scale = tree.Scale;
        int before = ToolLayer.GetChildCount();

        var plan = Import(RoomToolRoot);
        await Frames(1);
        Check(ItemsOf("tree").Count == 1 && ItemsOf("tree")[0] == tree && tree.Name == "tooltree" && tree.Position == pos && tree.Scale == scale
              && PathOf(tree) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_tree.png" && plan.Retextures.Count(r => r.image == tree) == 1,
            $"区域图升级：加上房间专属图再导入 → 原节点 tooltree 换成专属图，名字、位置、缩放都保留，没有第二棵树（{string.Join(" / ", plan.Messages)}）");
        Check(ToolLayer.GetChildCount() == before + 1 && ItemsOf("big_cloud").Count == 1 && ItemsOf("rock").Count == 1,
            "区域图升级：同时补上新的物件 BigCloud，新目录里没有的 Rock 原样保留");

        var again = BackgroundImporter.PlanImport(Room, At(RoomToolRoot));
        Check(again.Empty, $"区域图升级：再点一次，没有任何变化（{string.Join(" / ", again.Messages)}）");

        // 保存后重新打开
        var packed = new PackedScene();
        Error error = packed.Pack(Room);
        var reopened = packed.Instantiate<RoomRoot>();
        Node reTool = BackgroundImporter.FindLayer(reopened.GetNodeOrNull(BackgroundImporter.NodeName), BackgroundImporter.Tool);
        var reTree = reTool?.GetNodeOrNull<Sprite2D>("tooltree");
        var replan = BackgroundImporter.PlanImport(reopened, At(RoomToolRoot));
        Check(error == Error.Ok && reTree != null && reTree.GetMeta(BackgroundImporter.ToolMeta, "").AsString() == "tree" && reTree.Position == pos
              && replan.Empty && reTool.GetChildCount() == before + 1,
            $"区域图升级：保存后重新打开，物件标识还在，再点一次仍然不新增节点（{string.Join(" / ", replan.Messages)}）");
        reopened.Free();
        RemoveBackground();
    }

    /// <summary>你在 S1_03 里那样手建的节点：小写的 front / tool，物件叫 tooltree、居中、没有标识。</summary>
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
        Check(string.Join(",", names) == "front,tool,Notes" && front.ScrollScale == new Vector2(0.15f, 0.15f),
            $"手建节点：小写的 front / tool 被认出来，没有重复建层；你设的视差不改；你自己的节点不动（{string.Join(",", names)}）");
        Check(ItemsOf("tree").Count == 1 && handTree.GetMeta(BackgroundImporter.ToolMeta, "").AsString() == "tree" && handTree.Name == "tooltree"
              && handTree.Centered && handTree.Position == new Vector2(3047, 658) && PathOf(handTree) == $"{RoomToolRoot}/s1/tex_bg_s1_03_tool_tree.png"
              && plan.ToolTags.Count == 1,
            $"手建节点：没有标识的 tooltree 按图片文件名认出是 tree，补记标识并换成专属图，其余不动（{string.Join(" / ", plan.Messages)}）");
        Check(ItemsOf("big_cloud").Count == 1 && ItemsOf("big_cloud")[0].GetParent() == tool && ItemsOf("big_cloud")[0].Owner == Room,
            "手建节点：缺的物件 BigCloud 补进你建的 tool 层");
        Check(BackgroundImporter.PlanImport(Room, At(RoomToolRoot), l => l == "front" ? frontTex : null).Empty, "手建节点：再点一次没有变化");
        RemoveBackground();
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
        await Case("手建物件补记标识、换图", () =>
        {
            var tool = new Parallax2D { Name = "tool" };
            tool.AddChild(new Sprite2D { Name = "tooltree", Texture = GD.Load<Texture2D>($"{FixtureRoot}/s1/tex_bg_s1_tool_tree.png") });
            BuildBackground(tool);
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
