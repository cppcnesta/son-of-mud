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
        var textures = BackgroundImporter.Layers.ToDictionary(l => l, l => Blank(new Vector2(Mathf.Ceil(Required(l).X * factorX), Mathf.Ceil(Required(l).Y * factorY))));
        var plan = BackgroundImporter.PlanImport(Room, new BackgroundSettings { ArtRoot = MissingRoot }, l => textures[l]);
        BackgroundImporter.Apply(Room, plan);
        return plan;
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
        foreach (string layer in BackgroundImporter.Layers)
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
            bool structure = bg != null && plan.NewBackground == bg && bg.ZIndex == BackgroundImporter.BackgroundZ && Room.GetChild(0) == bg
                             && far != null && mid != null && far.ScrollScale == _settings.FarScroll && mid.ScrollScale == _settings.MidScroll
                             && new Node[] { bg, far, mid, ImageOf("far"), ImageOf("mid") }.All(n => n?.Owner == Room);
            Check(structure, $"{roomId}【{name}】导入后：Background（z -100，第一个子节点）→ Far / Mid（视差 0.15 / 0.5）→ Image，所有者都是房间");
            bool fitted = BackgroundImporter.Layers.All(l =>
            {
                Sprite2D image = ImageOf(l);
                Vector2 shown = image.Texture.GetSize() * image.Scale;
                Vector2 req = Required(l);
                return shown.X >= req.X - 0.01f && shown.Y >= req.Y - 0.01f && (Mathf.Abs(shown.X - req.X) < 1.01f || Mathf.Abs(shown.Y - req.Y) < 1.01f)
                       && (factor > 1 ? image.Scale.X < 1 : factor < 1 ? image.Scale.X > 1 : Mathf.Abs(image.Scale.X - 1) < 0.01f);
            });
            Check(fitted, $"{roomId}【{name}】两层都等比缩放到刚好盖满（远景 ×{ImageOf("far")?.Scale.X:0.###}，中景 ×{ImageOf("mid")?.Scale.X:0.###}）");
            string corner = await CornerProblems(true);
            Check(corner == null, $"{roomId}【{name}】镜头在四个角再加最大震屏，两层都盖满画面" + (corner == null ? "" : "：" + corner));
            Check(BackgroundImporter.CoverageProblems(Room, _settings).Count == 0, $"{roomId}【{name}】编辑器的露底检查也认为没问题");
        }

        var packed = new PackedScene();
        Error packError = packed.Pack(Room);
        SceneState state = packed.GetState();
        var names = Enumerable.Range(0, state.GetNodeCount()).Select(i => state.GetNodeName(i).ToString()).ToList();
        Check(packError == Error.Ok && names.Contains(BackgroundImporter.NodeName) && names.Contains("Far") && names.Contains("Mid") && names.Count(n => n == "Image") == 2,
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

        Texture2D newFar = Blank(new Vector2(100, 50)), newMid = Blank(new Vector2(120, 60));
        var plan = BackgroundImporter.PlanImport(Room, _settings, l => l == "far" ? newFar : newMid);
        Check(plan.NewBackground == null && plan.NewLayers.Count == 0 && plan.Retextures.Count == 2, $"再次导入：只换两层的图，不新建节点（{string.Join(" / ", plan.Messages)}）");
        BackgroundImporter.Apply(Room, plan);
        await Frames(1);
        Check(Bg.GetInstanceId() == bgId && ImageOf("far") == farImage && farImage.Texture == newFar && midImage.Texture == newMid
              && farImage.Scale == farScale && farImage.Position == farPos && midImage.Scale == midScale && midImage.Position == midPos,
            "再次导入：图换了，手动调过的缩放和位置原样保留");
        var again = BackgroundImporter.PlanImport(Room, _settings, l => l == "far" ? newFar : newMid);
        Check(again.Empty && again.Messages.Count == 2, $"同样的图再导入一次：什么都不改（{string.Join(" / ", again.Messages)}）");
        RemoveBackground();
    }

    /// <summary>真的从文件加载（测试素材）：S1_03 有专属远景，中景没有 → 只建远景；之后有了中景图再导入 → 补上中景。</summary>
    private async Task FileLookupChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RemoveBackground();
        var fixtures = new BackgroundSettings { ArtRoot = FixtureRoot };
        var plan = BackgroundImporter.PlanImport(Room, fixtures);
        BackgroundImporter.Apply(Room, plan);
        await Frames(1);
        Check(ImageOf("far")?.Texture?.ResourcePath == $"{FixtureRoot}/s1/tex_bg_s1_03_far.png" && Bg.GetNodeOrNull("Mid") == null
              && plan.Messages.Any(m => m.Contains("没有中景图") && m.Contains("tex_bg_s1_03_mid.png")),
            $"按命名从文件导入：S1_03 用专属远景，没有中景图就跳过并说明找过哪些文件（{string.Join(" / ", plan.Messages)}）");

        Texture2D mid = Blank(Required("mid"));
        var later = BackgroundImporter.PlanImport(Room, fixtures, l => l == "mid" ? mid : null);
        BackgroundImporter.Apply(Room, later);
        await Frames(1);
        Check(later.NewLayers.Count == 1 && Bg.GetNodeOrNull("Mid") != null && ImageOf("mid")?.Texture == mid && ImageOf("mid").Owner == Room && later.Retextures.Count == 0,
            "之后有了中景图再导入：往已有的 Background 里补上中景，远景不动");
        RemoveBackground();
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
        foreach (string layer in BackgroundImporter.Layers) before[layer] = ScreenRectOf(ImageOf(layer)).Position;
        Cam.GlobalPosition = room + new Vector2(2100, 700);
        Cam.ResetSmoothing();
        await Frames(2);
        Vector2 dc = ViewTopLeft - v1;
        foreach (string layer in BackgroundImporter.Layers)
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

        Check(problems.Count == 0, $"动态逐帧（{frames} 帧，S1_03 跑了 {run:0} 像素，竖井掉了 {fell:0} 像素，边界处震屏 {maxShake}）：每一帧两层都盖满画面"
                                   + (problems.Count == 0 ? "" : "：" + string.Join(" | ", problems)));
        Check(run > 600 && fell > 600, $"动态检查确实动起来了（跑 {run:0}，掉 {fell:0}）");
        Check(oldFreed, "切房间后，旧房间的背景跟着房间一起释放");

        var geometry = Room.GetNodeOrNull<CanvasItem>("Geometry");
        Check(Bg.ZIndex < 0 && geometry != null && geometry.ZIndex >= 0 && Player.ZIndex >= 0,
            $"前后顺序：背景 z = {Bg.ZIndex}，瓦片 z = {geometry?.ZIndex}，玩家 z = {Player.ZIndex}，背景画在最后面");
    }
}
