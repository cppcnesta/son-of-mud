using System;
using System.Collections.Generic;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间背景“按命名导入一次，之后手动调”。导入在房间场景里建普通节点（存进场景文件）：
/// <code>
/// Background（Node2D，z -100，第一个子节点）
///   Far（Parallax2D，scroll_scale = 远景系数）→ Image（Sprite2D，不居中）
///   Mid（Parallax2D，scroll_scale = 中景系数）→ Image
/// </code>
/// 导入后这些节点完全归人手动调整；再次导入只换图，不动缩放和位置。
///
/// 位置约定（C = 实际画面左上角，含平滑和震屏；S = 屏幕；R = 房间；M = 震屏余量；p = 视差系数）：
///   运行时 Parallax2D.position = scroll_offset + (1 − p)·C（实测 Godot 4.7；在编辑器里挪 Far / Mid 节点本身，运行时会被覆盖）；
///   这一层最少要 Wreq = S.x + p.x·(R.x − S.x + 2M)，Hreq 同理；
///   导入时等比缩放到刚好盖满（太大缩小、太小放大），Image 放在 (−p.x·M, Hreq − H − p.y·M)：
///   镜头在最左（含震屏）时图的左边对齐画面左边，在最下（含震屏）时图的底边对齐画面底边。
/// </summary>
public static class BackgroundImporter
{
    public const string NodeName = "Background";
    public const string ImageName = "Image";
    public const string Far = "far";
    public const string Mid = "mid";
    public const int BackgroundZ = -100;
    public static readonly string[] Layers = { Far, Mid };

    public static string LayerNodeName(string layer) => layer == Far ? "Far" : "Mid";
    public static string LayerTitle(string layer) => layer == Far ? "最远景" : "中景";

    /// <summary>一次导入要做的事：整棵新建、往已有 Background 里补层、给已有的层换图。分开列出，编辑器里好做撤销。</summary>
    public sealed class ImportPlan
    {
        public Node2D NewBackground;
        public Node ExistingBackground;
        public readonly List<Parallax2D> NewLayers = new();
        public readonly List<(Sprite2D image, Texture2D texture)> Retextures = new();
        public readonly List<string> Messages = new();
        public bool Empty => NewBackground == null && NewLayers.Count == 0 && Retextures.Count == 0;
    }

    // =================================================================
    // 导入
    // =================================================================

    /// <summary>
    /// 算出这次导入要做什么，不改场景。<paramref name="textureOverride"/> 只给测试用：按层返回一张图（null 就照常按命名查找）。
    /// </summary>
    public static ImportPlan PlanImport(RoomRoot room, BackgroundSettings settings, Func<string, Texture2D> textureOverride = null)
    {
        var plan = new ImportPlan();
        settings ??= BackgroundSettings.LoadDefault();
        if (room == null || !TryRoomSize(room.RoomId, out Vector2 roomSize))
        {
            plan.Messages.Add($"房间清单里找不到 {room?.RoomId}，没法导入背景。");
            return plan;
        }
        Vector2 screen = ProjectScreenSize();
        Node existing = room.GetNodeOrNull(NodeName);
        plan.ExistingBackground = existing;

        foreach (string layer in Layers)
        {
            Texture2D texture = textureOverride?.Invoke(layer);
            string source = texture != null ? "测试注入" : ResolvePath(settings, room.RoomId, layer);
            if (texture == null && source != null) texture = GD.Load<Texture2D>(source);
            if (texture == null)
            {
                TryParseRoom(room.RoomId, out string region, out string key);
                plan.Messages.Add($"{room.RoomId} 没有{LayerTitle(layer)}图（找过 {settings.ArtRoot}/{region}/tex_bg_{key}_{layer}.png 和 tex_bg_{region}_{layer}.png），跳过这一层。");
                continue;
            }

            Sprite2D image = existing?.GetNodeOrNull<Sprite2D>($"{LayerNodeName(layer)}/{ImageName}");
            if (image != null)
            {
                if (image.Texture == texture || (image.Texture != null && image.Texture.ResourcePath == texture.ResourcePath && texture.ResourcePath != ""))
                    plan.Messages.Add($"{room.RoomId} 的{LayerTitle(layer)}已经是 {source}，不用更新。");
                else
                {
                    plan.Retextures.Add((image, texture));
                    plan.Messages.Add($"{room.RoomId} 的{LayerTitle(layer)}换成 {source}（缩放和位置保持你调好的）。");
                }
                continue;
            }
            if (existing != null && existing.GetNodeOrNull(LayerNodeName(layer)) != null)
            {
                plan.Messages.Add($"{room.RoomId} 的 {NodeName}/{LayerNodeName(layer)} 下面没有名为 {ImageName} 的 Sprite2D，没有改动它。");
                continue;
            }

            Vector2 p = settings.ScrollOf(layer);
            Vector2 required = RequiredSize(roomSize, screen, p, settings.ShakeMargin);
            (float scale, Vector2 offset) = CoverFit(texture.GetSize(), required, p, settings.ShakeMargin);
            var parallax = new Parallax2D { Name = LayerNodeName(layer), ScrollScale = p };
            parallax.AddChild(new Sprite2D { Name = ImageName, Centered = false, Texture = texture, Scale = Vector2.One * scale, Position = offset });
            plan.Messages.Add($"{room.RoomId} 导入{LayerTitle(layer)} {source}（{texture.GetWidth()}×{texture.GetHeight()}，缩放到 {scale:0.###} 倍刚好盖满）。");

            if (existing != null) plan.NewLayers.Add(parallax);
            else
            {
                plan.NewBackground ??= new Node2D { Name = NodeName, ZIndex = BackgroundZ };
                plan.NewBackground.AddChild(parallax);
            }
        }
        return plan;
    }

    /// <summary>直接执行导入（游戏里 / 测试用；编辑器里由 RoomRoot 通过撤销管理器执行同样的步骤）。</summary>
    public static void Apply(RoomRoot room, ImportPlan plan)
    {
        if (plan.NewBackground != null)
        {
            room.AddChild(plan.NewBackground);
            room.MoveChild(plan.NewBackground, 0);
            room.AssignOwner(plan.NewBackground);
        }
        foreach (Parallax2D layer in plan.NewLayers)
        {
            plan.ExistingBackground.AddChild(layer);
            room.AssignOwner(layer);
        }
        foreach ((Sprite2D image, Texture2D texture) in plan.Retextures) image.Texture = texture;
    }

    // =================================================================
    // 露底检查（编辑器警告 + 测试共用）
    // =================================================================

    /// <summary>
    /// 检查最底层（Far）在镜头四个角、再加朝外的最大震屏时能不能盖满画面；按游戏运行时的规则算
    /// （Parallax2D.position = scroll_offset + (1 − p)·C）。返回问题列表，空表示没问题。没有 Background / Far 时不检查。
    /// checkNodeMoves：是否提醒“挪了 Far / Mid 节点本身”。只在编辑器里有意义（游戏里它们的位置本来就跟着镜头变），默认按是否在编辑器决定。
    /// </summary>
    public static List<string> CoverageProblems(RoomRoot room, BackgroundSettings settings, bool withShake = true, bool? checkNodeMoves = null)
    {
        var problems = new List<string>();
        settings ??= BackgroundSettings.LoadDefault();
        if (room?.GetNodeOrNull(NodeName) is not Node2D background) return problems;
        if (checkNodeMoves ?? Engine.IsEditorHint())
            foreach (string layer in Layers)
                if (background.GetNodeOrNull(LayerNodeName(layer)) is Parallax2D moved && !moved.Position.IsEqualApprox(moved.ScrollOffset))
                    problems.Add($"{NodeName}/{moved.Name} 节点本身被挪到了 {moved.Position.Round()}，游戏运行时它会被视差重置回 {moved.ScrollOffset.Round()}。"
                                 + $"要挪动背景，请调它下面的 {ImageName}（或改 Scroll Offset）。");
        if (background.GetNodeOrNull(LayerNodeName(Far)) is not Parallax2D far) return problems;
        Sprite2D image = far.GetNodeOrNull<Sprite2D>(ImageName);
        if (image?.Texture == null)
        {
            problems.Add($"{NodeName}/Far 下面没有带图的 {ImageName}，没法检查会不会露底。");
            return problems;
        }
        if (!TryRoomSize(room.RoomId, out Vector2 roomSize)) return problems;

        Vector2 screen = ProjectScreenSize();
        float m = withShake ? settings.ShakeMargin : 0f;
        Vector2 travel = (roomSize - screen).Max(Vector2.Zero);
        // 图在 Far 局部坐标里的矩形（含 Image 的位置、缩放、区域）
        Rect2 local = TransformRect(image.Transform, image.GetRect());
        string[] names = { "左上", "右上", "左下", "右下" };
        int index = 0;
        foreach (int sy in new[] { -1, 1 })
        foreach (int sx in new[] { -1, 1 })
        {
            var c = new Vector2(sx < 0 ? -m : travel.X + m, sy < 0 ? -m : travel.Y + m);
            Vector2 parallaxPos = far.ScrollOffset + (Vector2.One - far.ScrollScale) * c;
            Rect2 onScreen = TransformRect(background.Transform, new Rect2(local.Position + parallaxPos, local.Size));
            onScreen.Position -= c;
            float left = onScreen.Position.X, top = onScreen.Position.Y;
            float right = screen.X - onScreen.End.X, bottom = screen.Y - onScreen.End.Y;
            var gaps = new List<string>();
            if (left > 0.5f) gaps.Add($"左边 {Mathf.CeilToInt(left)}");
            if (right > 0.5f) gaps.Add($"右边 {Mathf.CeilToInt(right)}");
            if (top > 0.5f) gaps.Add($"上边 {Mathf.CeilToInt(top)}");
            if (bottom > 0.5f) gaps.Add($"下边 {Mathf.CeilToInt(bottom)}");
            if (gaps.Count > 0)
                problems.Add($"镜头在房间{names[index]}角{(withShake ? "（含震屏）" : "")}时，最远景{string.Join("、", gaps)} 像素露底。");
            index++;
        }
        return problems;
    }

    private static Rect2 TransformRect(Transform2D xf, Rect2 rect)
    {
        Vector2 a = xf * rect.Position, b = xf * rect.End;
        return new Rect2(a.Min(b), (b - a).Abs());
    }

    // =================================================================
    // 纯计算
    // =================================================================

    /// <summary>这一层最少需要的显示尺寸：S + p·(R − S + 2M)。房间比屏幕小时按屏幕算。</summary>
    public static Vector2 RequiredSize(Vector2 room, Vector2 screen, Vector2 p, float margin)
    {
        Vector2 travel = (room - screen).Max(Vector2.Zero) + Vector2.One * (2f * margin);
        return screen + p * travel;
    }

    /// <summary>导入时的初始摆放：等比缩放到刚好盖满 required（太大缩小、太小放大），左边、底边按约定对齐。</summary>
    public static (float scale, Vector2 offset) CoverFit(Vector2 texture, Vector2 required, Vector2 p, float margin)
    {
        float scale = Mathf.Max(required.X / texture.X, required.Y / texture.Y);
        Vector2 display = texture * scale;
        return (scale, new Vector2(-p.X * margin, required.Y - display.Y - p.Y * margin));
    }

    /// <summary>S1_03 → ("s1", "s1_03")。</summary>
    public static bool TryParseRoom(string roomId, out string region, out string roomKey)
    {
        roomKey = (roomId ?? "").ToLowerInvariant();
        int split = roomKey.IndexOf('_');
        region = split > 0 ? roomKey[..split] : "";
        return split > 0;
    }

    /// <summary>按命名查找：房间专属图 → 区域通用图；都没有返回 null。</summary>
    public static string ResolvePath(BackgroundSettings settings, string roomId, string layer)
    {
        if (settings == null || !TryParseRoom(roomId, out string region, out string roomKey)) return null;
        string dir = $"{settings.ArtRoot.TrimEnd('/')}/{region}";
        string roomPath = $"{dir}/tex_bg_{roomKey}_{layer}.png";
        if (ResourceLoader.Exists(roomPath)) return roomPath;
        string regionPath = $"{dir}/tex_bg_{region}_{layer}.png";
        return ResourceLoader.Exists(regionPath) ? regionPath : null;
    }

    /// <summary>
    /// 房间像素尺寸 = 房间清单里的格数 × MetSys 设置里的格子尺寸。
    /// 编辑器里也要能用：WorldLayout / RoomDef 不是 [Tool] 脚本，在编辑器里加载出来只是普通 Resource（强转会抛异常），所以按属性名读取。
    /// </summary>
    public static bool TryRoomSize(string roomId, out Vector2 size)
    {
        size = Vector2.Zero;
        if (string.IsNullOrEmpty(roomId) || !ResourceLoader.Exists(WorldLayout.DefaultPath)) return false;
        Resource layout = GD.Load<Resource>(WorldLayout.DefaultPath);
        Vector2I cells = Vector2I.Zero;
        foreach (Variant item in layout.Get(nameof(WorldLayout.Rooms)).AsGodotArray())
        {
            if (item.AsGodotObject() is not Resource def || def.Get(nameof(RoomDef.Id)).AsString() != roomId) continue;
            cells = def.Get(nameof(RoomDef.Size)).AsVector2I();
            break;
        }
        if (cells == Vector2I.Zero) return false;
        string settingsPath = ProjectSettings.GetSetting("addons/metroidvania_system/settings_file", "").AsString();
        if (string.IsNullOrEmpty(settingsPath) || !ResourceLoader.Exists(settingsPath)) return false;
        Vector2 cell = GD.Load<Resource>(settingsPath).Get("in_game_cell_size").AsVector2();
        size = new Vector2(cells.X * cell.X, cells.Y * cell.Y);
        return size.X > 0 && size.Y > 0;
    }

    public static Vector2 ProjectScreenSize() => new(
        ProjectSettings.GetSetting("display/window/size/viewport_width", 1152).AsInt32(),
        ProjectSettings.GetSetting("display/window/size/viewport_height", 648).AsInt32());
}
