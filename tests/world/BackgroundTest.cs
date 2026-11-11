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
/// 房间背景（计划“房间背景组件 v2”第 8 节）。从正式入口 main_game.tscn 开新游戏，用真实镜头（平滑、震屏、MetSys 镜头范围）。
/// 所有“盖满屏幕”的判定都在屏幕坐标下做：每层 Image 的 GetGlobalTransformWithCanvas() × GetRect() 必须盖住整个可见画面。
/// </summary>
public partial class BackgroundTest : FlowTestBase
{
    protected override string SuiteName => "background";
    protected override string ResultPrefix => "world";

    private const string FixtureRoot = "res://tests/world/fixtures/bg";
    private const string MissingRoot = "res://tests/world/fixtures/bg_none";

    private Rect2 Screen => GetViewport().GetVisibleRect();
    private Vector2 ViewTopLeft => -GetViewport().GetCanvasTransform().Origin;
    private RoomBackground Bg => World?.CurrentRoom?.Background;

    protected override async Task RunChecks()
    {
        PureChecks();
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        Check(started && World != null && Cam != null && Player != null, "正式入口开新游戏成功，有 WorldGame、RoomCamera、玩家");
        if (World == null || Cam == null || Player == null) return;

        await AllRoomsChecks();
        await ScenarioChecks("S1_03");
        await ScenarioChecks("S4_02");
        await FixtureLookupInGame();
        await ParallaxShiftChecks();
        await DynamicChecks();
        await StructureChecks();
    }

    // =================================================================
    // 纯计算：查图顺序、需要的尺寸、配置一致
    // =================================================================

    private void PureChecks()
    {
        var fixtures = new BackgroundSettings { ArtRoot = FixtureRoot };
        string roomFar = RoomBackground.ResolvePath(fixtures, "S1_03", RoomBackground.Far);
        string regionFar = RoomBackground.ResolvePath(fixtures, "S1_02", RoomBackground.Far);
        Check(roomFar == $"{FixtureRoot}/s1/tex_bg_s1_03_far.png", $"查图：有房间专属图时优先用它（{roomFar}）");
        Check(regionFar == $"{FixtureRoot}/s1/tex_bg_s1_far.png", $"查图：没有房间专属图时用区域通用图（{regionFar}）");
        Check(RoomBackground.ResolvePath(fixtures, "S1_02", RoomBackground.Mid) == null && RoomBackground.ResolvePath(fixtures, "S2_01", RoomBackground.Far) == null,
            "查图：两种都没有时返回 null（用占位图）");

        var settings = BackgroundSettings.LoadDefault();
        Vector2 screen = RoomBackground.ProjectScreenSize();
        Vector2 s103Mid = RoomBackground.RequiredSize(new Vector2(3456, 972), screen, settings.MidScroll, settings.ShakeMargin);
        Vector2 s402Far = RoomBackground.RequiredSize(new Vector2(2304, 3240), screen, settings.FarScroll, settings.ShakeMargin);
        Check(Mathf.CeilToInt(s103Mid.X) == 2328 && Mathf.CeilToInt(s103Mid.Y) == 834, $"尺寸：S1_03 中景至少 2328×834（{s103Mid}）");
        Check(Mathf.CeilToInt(s402Far.X) == 1332 && Mathf.CeilToInt(s402Far.Y) == 1044, $"尺寸：S4_02 远景至少 1332×1044（{s402Far}）");
        Check(RoomBackground.TryRoomSize("S4_02", out Vector2 shaft) && shaft == new Vector2(2304, 3240), $"尺寸：房间像素尺寸从房间清单和 MetSys 格子算出（S4_02 = {shaft}）");

        var camera = ResourceLoader.Exists("res://assets/data/camera/camera_settings.tres")
            ? GD.Load<CameraSettings>("res://assets/data/camera/camera_settings.tres") : null;
        float maxShake = (camera ?? new CameraSettings()).MaxShakeOffset;
        Check(settings.ShakeMargin >= maxShake, $"配置一致：背景的震屏余量 {settings.ShakeMargin} ≥ 镜头最大震屏 {maxShake}");
    }

    // =================================================================
    // 屏幕坐标判定
    // =================================================================

    /// <summary>两层都盖满可见画面时返回 null，否则返回说明。</summary>
    private string CoverageProblem(RoomBackground bg)
    {
        if (bg == null) return "房间没有背景";
        foreach (string layer in RoomBackground.Layers)
        {
            Sprite2D image = bg.ImageOf(layer);
            if (image == null || image.Texture == null) return $"{layer} 没有图";
            Rect2 r = ScreenRectOf(image);
            if (!r.Grow(0.5f).Encloses(Screen))
                return $"{bg.RoomId} {layer}：屏幕矩形 {r.Position.Round()} 大小 {r.Size.Round()}，没盖住画面（画面左上角 {ViewTopLeft.Round()}）";
        }
        return null;
    }

    private static Rect2 ScreenRectOf(Sprite2D image)
    {
        Transform2D xf = image.GetGlobalTransformWithCanvas();
        Rect2 local = image.GetRect();
        Vector2 a = xf * local.Position, b = xf * local.End;
        return new Rect2(a.Min(b), (b - a).Abs());
    }

    /// <summary>
    /// 把镜头推到四个角（镜头范围限住），withShake 时再加上朝外的 ±M 偏移（Camera2D.Offset 会越过镜头范围）。
    /// 同时核对画面确实到了极端位置，否则这项检查等于没测。
    /// </summary>
    private async Task<string> CornerProblems(RoomBackground bg, bool withShake)
    {
        float m = bg.Settings.ShakeMargin;
        Vector2 room = World.CurrentRoom.GlobalPosition;
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
                if (ViewTopLeft.DistanceTo(expected) > 0.5f) return $"{bg.RoomId}：画面没到极端位置（{ViewTopLeft.Round()}，应为 {expected.Round()}）";
                string problem = CoverageProblem(bg);
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
    // 27 个房间：自动生成背景，四角（含震屏）都盖满
    // =================================================================

    private async Task AllRoomsChecks()
    {
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        var missing = new List<string>();
        var corner = new List<string>();
        var shake = new List<string>();
        var warned = new List<string>();
        foreach (RoomDef def in layout.Rooms)
        {
            await Teleport(def.Id, SpawnPoint.DefaultId);
            RoomBackground bg = Bg;
            if (World.CurrentRoomDef?.Id != def.Id || bg == null || RoomBackground.Layers.Any(l => bg.ImageOf(l)?.Texture == null))
            {
                missing.Add(def.Id);
                continue;
            }
            if (bg.Warnings.Count > 0 && RoomBackground.Layers.All(l => bg.TryGetLayer(l, out var s) && s.Placeholder)) warned.Add(def.Id);
            string a = await CornerProblems(bg, false);
            if (a != null) corner.Add(a);
            string b = await CornerProblems(bg, true);
            if (b != null) shake.Add(b);
        }
        Check(missing.Count == 0, $"{layout.Rooms.Count} 个房间进场时都自动生成了远景和中景" + Fmt(missing));
        Check(warned.Count == 0, "只用占位图的房间没有警告" + Fmt(warned));
        Check(corner.Count == 0, "每个房间：镜头在四个角时两层都盖满画面" + Fmt(corner));
        Check(shake.Count == 0, "每个房间：镜头在四个角再加朝外的最大震屏偏移时，两层仍盖满画面" + Fmt(shake));
    }

    // =================================================================
    // 图片尺寸不够的各种情况（S1_03 最宽、S4_02 竖井）
    // =================================================================

    private static Texture2D Blank(float w, float h) =>
        ImageTexture.CreateFromImage(Image.CreateEmpty(Math.Max(1, (int)w), Math.Max(1, (int)h), false, Image.Format.Rgba8));

    private async Task ScenarioChecks(string roomId)
    {
        await Teleport(roomId, SpawnPoint.DefaultId);
        RoomBackground bg = Bg;
        Check(bg != null && World.CurrentRoomDef?.Id == roomId, $"{roomId}：进场，有背景");
        if (bg == null) return;
        var settings = BackgroundSettings.LoadDefault();
        Vector2 room = bg.RoomSize, screen = bg.ScreenSize;
        Vector2 Req(string layer) => RoomBackground.RequiredSize(room, screen, settings.ScrollOf(layer), settings.ShakeMargin);

        var cases = new (string name, Func<Vector2, Vector2> size)[]
        {
            ("刚好够大", r => new Vector2(Mathf.Ceil(r.X), Mathf.Ceil(r.Y))),
            ("只是宽度不够", r => new Vector2(Mathf.Floor(r.X * 0.6f), Mathf.Ceil(r.Y))),
            ("只是高度不够", r => new Vector2(Mathf.Ceil(r.X), Mathf.Floor(r.Y * 0.6f))),
            ("宽和高都不够", r => new Vector2(Mathf.Floor(r.X * 0.3f), Mathf.Floor(r.Y * 0.5f))),
            ("缺图", null),
        };
        foreach (var (name, size) in cases)
        {
            var textures = new Dictionary<string, Texture2D>();
            if (size != null)
                foreach (string layer in RoomBackground.Layers)
                {
                    Vector2 s = size(Req(layer));
                    textures[layer] = Blank(s.X, s.Y);
                }
            bg.Settings = new BackgroundSettings { ArtRoot = MissingRoot };
            bg.Build(roomId, layer => textures.GetValueOrDefault(layer));
            await Frames(2);

            bg.TryGetLayer(RoomBackground.Far, out var far);
            bg.TryGetLayer(RoomBackground.Mid, out var mid);
            Vector2 farReq = Req(RoomBackground.Far), midReq = Req(RoomBackground.Mid);
            (bool ok, string expect) = name switch
            {
                "刚好够大" => (!far.Fit.Scaled && !far.Fit.Tiled && !mid.Fit.Scaled && !mid.Fit.Tiled && bg.Warnings.Count == 0,
                    "两层都不缩放、不平铺，没有警告"),
                "只是宽度不够" => (far.Fit.Tiled && !far.Fit.Scaled && mid.Fit.Scaled && !mid.Fit.Tiled
                                   && Mathf.IsEqualApprox(mid.Fit.Scale, midReq.X / mid.TextureSize.X, 0.001f) && bg.Warnings.Count == 2,
                    "远景（横向无缝）平铺、不缩放；中景（不是无缝）等比放大到够宽；每层警告一次"),
                "只是高度不够" => (far.Fit.Scaled && !far.Fit.Tiled && mid.Fit.Scaled && !mid.Fit.Tiled
                                   && Mathf.IsEqualApprox(far.Fit.Scale, farReq.Y / far.TextureSize.Y, 0.001f)
                                   && Mathf.IsEqualApprox(mid.Fit.Scale, midReq.Y / mid.TextureSize.Y, 0.001f) && bg.Warnings.Count == 2,
                    "两层都等比放大到够高（不靠横向平铺）；每层警告一次"),
                "宽和高都不够" => (far.Fit.Scaled && far.Fit.Tiled && Mathf.IsEqualApprox(far.Fit.Scale, farReq.Y / far.TextureSize.Y, 0.001f)
                                   && mid.Fit.Scaled && !mid.Fit.Tiled && Mathf.IsEqualApprox(mid.Fit.Scale, midReq.X / mid.TextureSize.X, 0.001f)
                                   && bg.Warnings.Count == 2,
                    "远景按高度放大、宽度靠平铺；中景按较大的倍数（这里是宽度）放大；每层警告一次"),
                _ => (far.Placeholder && mid.Placeholder && bg.Warnings.Count == 0, "两层都用占位图，没有警告"),
            };
            Check(ok, $"{roomId}【{name}】{expect}（远景 {far.TextureSize}→×{far.Fit.Scale:0.###}{(far.Fit.Tiled ? " 平铺" : "")}，"
                      + $"中景 {mid.TextureSize}→×{mid.Fit.Scale:0.###}{(mid.Fit.Tiled ? " 平铺" : "")}，警告 {bg.Warnings.Count} 条）");
            string corner = await CornerProblems(bg, true);
            Check(corner == null, $"{roomId}【{name}】四个角加最大震屏，两层都盖满画面" + (corner == null ? "" : "：" + corner));
        }
        bg.Settings = null;
        bg.Build(roomId);
    }

    /// <summary>真的从文件加载：测试素材目录里 S1_03 有专属远景，S1_02 只有区域远景，中景都没有。</summary>
    private async Task FixtureLookupInGame()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RoomBackground bg = Bg;
        if (bg == null) { Check(false, "S1_03 没有背景"); return; }
        bg.Settings = new BackgroundSettings { ArtRoot = FixtureRoot };
        bg.Build("S1_03");
        await Frames(2);
        bg.TryGetLayer(RoomBackground.Far, out var far);
        bg.TryGetLayer(RoomBackground.Mid, out var mid);
        Check(far.Source == $"{FixtureRoot}/s1/tex_bg_s1_03_far.png" && !far.Placeholder && far.Fit.Scaled && far.Fit.Tiled && mid.Placeholder,
            $"按命名从文件加载：S1_03 远景用专属图（8×8，放大并平铺），中景没图用占位（{far.Source} / {mid.Source}）");
        string corner = await CornerProblems(bg, true);
        Check(corner == null, "8×8 的小图放大平铺后，四个角加最大震屏仍盖满画面" + (corner == null ? "" : "：" + corner));
        bg.Settings = null;
        bg.Build("S1_03");
    }

    // =================================================================
    // 视差位移（屏幕上的位移 = −p × 镜头位移）
    // =================================================================

    private async Task ParallaxShiftChecks()
    {
        await Teleport("S1_03", SpawnPoint.DefaultId);
        RoomBackground bg = Bg;
        if (bg == null) { Check(false, "S1_03 没有背景"); return; }
        Vector2 room = World.CurrentRoom.GlobalPosition;
        Cam.SetProcess(false);
        Cam.Offset = Vector2.Zero;
        var before = new Dictionary<string, Vector2>();
        Cam.GlobalPosition = room + new Vector2(900, 500);
        Cam.ResetSmoothing();
        await Frames(2);
        Vector2 v1 = ViewTopLeft;
        foreach (string layer in RoomBackground.Layers) before[layer] = ScreenRectOf(bg.ImageOf(layer)).Position;
        Cam.GlobalPosition = room + new Vector2(2100, 700);
        Cam.ResetSmoothing();
        await Frames(2);
        Vector2 dc = ViewTopLeft - v1;
        foreach (string layer in RoomBackground.Layers)
        {
            Vector2 p = bg.Settings.ScrollOf(layer);
            Vector2 moved = ScreenRectOf(bg.ImageOf(layer)).Position - before[layer];
            Vector2 expected = -p * dc;
            Check(dc.X > 500 && dc.Y > 50 && moved.DistanceTo(expected) <= 1f,
                $"视差：镜头移动 {dc.Round()}，{layer} 在屏幕上移动 {moved.Round()}（应为 −{p} × 镜头位移 = {expected.Round()}）");
        }
        Cam.SetProcess(true);
        Cam.SnapToTarget();
    }

    // =================================================================
    // 动态逐帧：真实按键跑过 S1_03、边界处最大震屏、竖井掉落；镜头平滑开着
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
                string p = CoverageProblem(Bg);
                if (p != null && problems.Count < 5) problems.Add($"第 {frames} 帧 {p}");
                if (stop != null && stop()) return;
            }
        }

        // S1_03 从左往右跑
        await Teleport("S1_03", "From_S1_02");
        float x0 = Player.GlobalPosition.X;
        Input.ActionPress(PlayerActions.Right);
        await Watch(600, () => World.CurrentRoomDef?.Id != "S1_03" || Player.GlobalPosition.X - World.CurrentRoom.GlobalPosition.X > 3300);
        Input.ActionRelease(PlayerActions.Right);
        float run = Player.GlobalPosition.X - x0;

        // 左右两头各震一次最大强度
        float maxShake = Cam.Settings.MaxShakeOffset;
        foreach (string spawn in new[] { "From_S1_02", "From_S1_04" })
        {
            await Teleport("S1_03", spawn);
            Cam.Shake(maxShake, 1f);
            await Watch(60);
        }

        // 竖井从上往下掉
        await Teleport("S4_02", SpawnPoint.DefaultId);
        Player.GlobalPosition = World.CurrentRoom.GlobalPosition + new Vector2(1152, 120);
        Player.Velocity = Vector2.Zero;
        Cam.SnapToTarget();
        float y0 = Player.GlobalPosition.Y;
        await Watch(300);
        float fell = Player.GlobalPosition.Y - y0;

        Check(problems.Count == 0, $"动态逐帧（{frames} 帧，S1_03 跑了 {run:0} 像素，竖井掉了 {fell:0} 像素，边界处震屏 {maxShake}）：每一帧两层都盖满画面" + Fmt(problems));
        Check(run > 600 && fell > 600, $"动态检查确实动起来了（跑 {run:0}，掉 {fell:0}）");
    }

    // =================================================================
    // 结构：前后顺序、只在编辑器里跑预览、不存进场景、跟着房间释放
    // =================================================================

    private async Task StructureChecks()
    {
        await Teleport("S1_05", SpawnPoint.DefaultId);
        RoomRoot room = World.CurrentRoom;
        RoomBackground bg = Bg;
        if (bg == null) { Check(false, "S1_05 没有背景"); return; }
        var geometry = room.GetNodeOrNull<CanvasItem>("Geometry");
        Check(bg.ZIndex == RoomBackground.BackgroundZ && bg.ZAsRelative && room.GetChild(0, true) == bg && room.ZIndex == 0
              && geometry != null && geometry.ZIndex >= 0 && Player.ZIndex >= 0,
            $"前后顺序：背景 z = {bg.ZIndex}（房间里第一个子节点），瓦片 z = {geometry?.ZIndex}，玩家 z = {Player.ZIndex}，背景画在最后面");
        Check(!room.GetChildren().Contains(bg), "背景是内部子节点，不会被 InRoom / 出生点搜索之类的遍历扫到");
        Check(!bg.IsProcessing(), "游戏里背景不跑编辑器预览的每帧逻辑");

        var packed = new PackedScene();
        Error packError = packed.Pack(room);
        SceneState state = packed.GetState();
        var names = Enumerable.Range(0, state.GetNodeCount()).Select(i => state.GetNodeName(i).ToString()).ToList();
        Check(packError == Error.Ok && names.Count > 3 && !names.Contains(RoomBackground.NodeName) && !names.Contains("Far") && !names.Contains("Mid"),
            $"把房间重新打包成场景时不含背景（编辑器保存房间场景时不会把背景写进去；共 {names.Count} 个节点）");

        ulong id = bg.GetInstanceId();
        await Teleport("S1_04", SpawnPoint.DefaultId);
        bool freed = await WaitUntil(() => !GodotObject.IsInstanceIdValid(id), 30);
        Check(freed && Bg != null && Bg.GetInstanceId() != id, "切房间后旧背景已释放，新房间有自己的背景");
    }

    private static string Fmt(List<string> items) => items.Count == 0 ? "" : "：" + string.Join(" | ", items.Take(5));
}
