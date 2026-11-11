using System;
using System.Collections.Generic;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间背景：最远景（Far）和中景（Mid）两层视差图，由 <see cref="RoomRoot"/> 自动生成（内部子节点，不存进场景文件）。
/// 结构：Background（z -100）→ Far / Mid（Parallax2D，scroll_scale = p）→ Image（Sprite2D，不居中）。
///
/// 位置约定（C = 实际画面左上角，含平滑和震屏；S = 屏幕；R = 房间；M = 震屏余量；W/H = 图缩放后的显示尺寸）：
///   这一层最少需要 Wreq = S.x + p.x·(R.x − S.x + 2M)，Hreq 同理；
///   图在世界里的位置 X = (1 − p.x)·C.x − p.x·M，Y = (1 − p.y)·C.y + Hreq − H − p.y·M。
///   Parallax2D 自己负责 (1 − p)·C 这一项（实测 Godot 4.7 如此），Image 的位置固定为余下的偏移。
///   含义：镜头在最左（含震屏）时图的左边对齐画面左边，镜头在最下（含震屏）时图的底边对齐画面底边；多出来的裁掉右边和顶上。
///
/// 编辑器里同样会生成（预览），按编辑器 2D 视图中心模拟镜头位置，平移视图就能看到视差效果。
/// </summary>
[Tool]
public partial class RoomBackground : Node2D
{
    public const string NodeName = "Background";
    public const string Far = "far";
    public const string Mid = "mid";
    public const int BackgroundZ = -100;
    public static readonly string[] Layers = { Far, Mid };

    /// <summary>占位图按 1/4 分辨率生成再放大，省内存。</summary>
    private const int PlaceholderScale = 4;

    /// <summary>一层的摆放结果。RegionSize 是用到的图片区域（图片像素），DisplaySize = RegionSize × Scale。</summary>
    public readonly record struct LayerFit(float Scale, bool Tiled, Vector2 RegionSize, Vector2 DisplaySize, Vector2 Offset)
    {
        public bool Scaled => Scale > 1.0001f;
    }

    /// <summary>一层的来源和摆放，测试和配置警告用。</summary>
    public readonly record struct LayerState(string Source, bool Placeholder, Vector2 Required, Vector2 TextureSize, LayerFit Fit);

    public BackgroundSettings Settings { get; set; }
    public string RoomId { get; private set; } = "";
    public Vector2 RoomSize { get; private set; }
    public Vector2 ScreenSize { get; private set; }

    private readonly Dictionary<string, LayerState> _states = new();
    private readonly List<string> _warnings = new();
    /// <summary>已经打印过的警告（同一张图、同一个房间、同一层只打印一次）。只存字符串，不持有 Godot 对象。</summary>
    private static readonly HashSet<string> Printed = new();

    public IReadOnlyList<string> Warnings => _warnings;
    public bool TryGetLayer(string layer, out LayerState state) => _states.TryGetValue(layer, out state);
    public Sprite2D ImageOf(string layer) => GetNodeOrNull<Sprite2D>($"{LayerNodeName(layer)}/Image");
    public Parallax2D ParallaxOf(string layer) => GetNodeOrNull<Parallax2D>(LayerNodeName(layer));
    public static string LayerNodeName(string layer) => layer == Far ? "Far" : "Mid";

    public override void _Ready()
    {
        SetProcess(Engine.IsEditorHint());
    }

    // =================================================================
    // 生成
    // =================================================================

    /// <summary>
    /// 按房间重新生成两层。<paramref name="textureOverride"/> 只给测试用：按层返回一张图（返回 null 就照常按命名查找）。
    /// </summary>
    public void Build(string roomId, Func<string, Texture2D> textureOverride = null)
    {
        Settings ??= BackgroundSettings.LoadDefault();
        RoomId = roomId ?? "";
        ZIndex = BackgroundZ;
        ZAsRelative = true;
        _states.Clear();
        _warnings.Clear();
        foreach (Node child in GetChildren(true))
        {
            RemoveChild(child);
            child.QueueFree();
        }

        ScreenSize = ProjectScreenSize();
        if (!TryRoomSize(RoomId, out Vector2 room))
        {
            RoomSize = Vector2.Zero;
            _warnings.Add($"房间清单里找不到 {RoomId}，没有生成背景。");
            return;
        }
        RoomSize = room;
        foreach (string layer in Layers) BuildLayer(layer, textureOverride?.Invoke(layer));
    }

    private void BuildLayer(string layer, Texture2D injected)
    {
        Vector2 p = Settings.ScrollOf(layer);
        float m = Settings.ShakeMargin;
        Vector2 required = RequiredSize(RoomSize, ScreenSize, p, m);

        Texture2D texture = injected;
        string source = injected != null ? "测试注入" : null;
        if (texture == null)
        {
            string path = ResolvePath(Settings, RoomId, layer);
            if (path != null)
            {
                texture = GD.Load<Texture2D>(path);
                source = path;
            }
        }

        bool placeholder = texture == null;
        LayerFit fit;
        if (placeholder)
        {
            texture = MakePlaceholder(layer, required, out Vector2 imageSize);
            Vector2 display = imageSize * PlaceholderScale;
            fit = new LayerFit(PlaceholderScale, false, imageSize, display, new Vector2(-p.X * m, required.Y - display.Y - p.Y * m));
            source = "占位";
        }
        else
        {
            fit = Fit(texture.GetSize(), required, p, m, Settings.SeamlessOf(layer));
        }

        var parallax = new Parallax2D { Name = LayerNodeName(layer), ScrollScale = p };
        var image = new Sprite2D { Name = "Image", Centered = false, Texture = texture, Scale = Vector2.One * fit.Scale, Position = fit.Offset };
        if (fit.Tiled)
        {
            image.RegionEnabled = true;
            image.RegionRect = new Rect2(Vector2.Zero, fit.RegionSize);
            image.TextureRepeat = TextureRepeatEnum.Enabled;
        }
        parallax.AddChild(image);
        AddChild(parallax);

        Vector2 textureSize = placeholder ? fit.RegionSize : texture.GetSize();
        _states[layer] = new LayerState(source, placeholder, required, textureSize, fit);

        if (!placeholder && (fit.Scaled || fit.Tiled))
        {
            string how = fit.Tiled && !fit.Scaled ? "已横向平铺"
                : fit.Tiled ? $"已等比放大 {fit.Scale:0.##} 倍并横向平铺"
                : $"已等比放大 {fit.Scale:0.##} 倍（会有点糊）";
            string message = $"房间 {RoomId} 的{(layer == Far ? "最远景" : "中景")}需要至少 {Mathf.CeilToInt(required.X)}×{Mathf.CeilToInt(required.Y)}，"
                + $"现在的图 {source} 只有 {textureSize.X}×{textureSize.Y}，{how}。可以换一张够大的图，或给这个房间画专属图。";
            _warnings.Add(message);
            if (Printed.Add(message)) GD.PushWarning(message);
        }
    }

    // =================================================================
    // 纯计算（测试直接调用）
    // =================================================================

    /// <summary>这一层最少需要的显示尺寸：S + p·(R − S + 2M)。房间比屏幕小时按屏幕算。</summary>
    public static Vector2 RequiredSize(Vector2 room, Vector2 screen, Vector2 p, float margin)
    {
        Vector2 travel = (room - screen).Max(Vector2.Zero) + Vector2.One * (2f * margin);
        return screen + p * travel;
    }

    /// <summary>
    /// 图片尺寸不够时的处理：高度不够 → 等比放大；宽度不够 → 横向无缝的层平铺（不缩放），否则等比放大；
    /// 两者都不够 → 取较大的放大倍数（无缝层只按高度放大，宽度靠平铺）。之后左边、底边对齐，多出的裁掉右边和顶上。
    /// </summary>
    public static LayerFit Fit(Vector2 texture, Vector2 required, Vector2 p, float margin, bool seamless)
    {
        const float eps = 0.01f;
        float scale = 1f;
        if (texture.Y + eps < required.Y) scale = required.Y / texture.Y;
        bool tiled = false;
        if (texture.X * scale + eps < required.X)
        {
            if (seamless) tiled = true;
            else scale = required.X / texture.X;
        }
        Vector2 region = tiled ? new Vector2(Mathf.Ceil(required.X / scale), texture.Y) : texture;
        Vector2 display = region * scale;
        return new LayerFit(scale, tiled, region, display, new Vector2(-p.X * margin, required.Y - display.Y - p.Y * margin));
    }

    /// <summary>S1_03 → ("s1", "s1_03")。</summary>
    public static bool TryParseRoom(string roomId, out string region, out string roomKey)
    {
        roomKey = (roomId ?? "").ToLowerInvariant();
        int split = roomKey.IndexOf('_');
        region = split > 0 ? roomKey[..split] : "";
        return split > 0;
    }

    /// <summary>按命名查找：房间专属图 → 区域通用图；都没有返回 null（用占位图）。</summary>
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
    /// 编辑器里也要能用：WorldLayout / RoomDef 不是 [Tool] 脚本，在编辑器里加载出来只是普通 Resource（强转会抛异常），
    /// 所以按属性名读取，游戏里和编辑器里都一样；也不依赖 MetSys 单例。
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

    // =================================================================
    // 占位图（缺图时）：最远景 = 区域色上下渐变；中景 = 按房间号固定摆放的深色柱子剪影
    // =================================================================

    private Texture2D MakePlaceholder(string layer, Vector2 required, out Vector2 imageSize)
    {
        int w = Mathf.CeilToInt(required.X / PlaceholderScale) + 1;
        int h = Mathf.CeilToInt(required.Y / PlaceholderScale) + 1;
        imageSize = new Vector2(w, h);
        TryParseRoom(RoomId, out string region, out string roomKey);
        Color baseColor = Settings.RegionColor(region);
        Image image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        if (layer == Far)
        {
            Color top = baseColor.Darkened(0.55f), bottom = baseColor.Lightened(0.15f);
            for (int y = 0; y < h; y++) image.FillRect(new Rect2I(0, y, w, 1), top.Lerp(bottom, h > 1 ? (float)y / (h - 1) : 0f));
        }
        else
        {
            image.Fill(new Color(0, 0, 0, 0));
            Color column = baseColor.Darkened(0.35f);
            ulong seed = 1469598103934665603UL;
            foreach (char ch in roomKey) seed = (seed ^ ch) * 1099511628211UL;
            int x = (int)(seed % 23);
            while (x < w)
            {
                seed = seed * 6364136223846793005UL + 1442695040888963407UL;
                int cw = 8 + (int)((seed >> 33) % 14);
                int colH = h / 3 + (int)((seed >> 40) % (ulong)Math.Max(1, h / 2));
                image.FillRect(new Rect2I(x, h - colH, Math.Min(cw, w - x), colH), column);
                x += cw + 18 + (int)((seed >> 50) % 40);
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    // =================================================================
    // 编辑器预览
    // =================================================================

    public override void _Process(double delta)
    {
#if TOOLS
        if (Engine.IsEditorHint()) EditorPreview(delta);
#endif
    }

#if TOOLS
    private double _refreshLeft;

    /// <summary>
    /// 按编辑器 2D 视图的中心模拟镜头（限制在房间范围内，不含震屏），直接摆 Image 的世界位置，不依赖 Parallax2D 在编辑器里的行为。
    /// 每秒检查一次图片有没有新增或换了尺寸，有就重新生成；C# 重新编译后字段会清空，也会重新生成。
    /// </summary>
    private void EditorPreview(double delta)
    {
        if (GetParent() is not RoomRoot room) return;
        _refreshLeft -= delta;
        if (RoomSize == Vector2.Zero || _refreshLeft <= 0)
        {
            _refreshLeft = 1.0;
            if (RoomSize == Vector2.Zero || SourcesChanged(room.RoomId))
            {
                Settings = null;
                Build(room.RoomId);
                room.UpdateConfigurationWarnings();
            }
        }
        if (RoomSize == Vector2.Zero) return;

        SubViewport view = EditorInterface.Singleton.GetEditorViewport2D();
        if (view == null) return;
        Vector2 center = view.GlobalCanvasTransform.AffineInverse() * (view.GetVisibleRect().Size / 2f);
        Vector2 camera = (room.ToLocal(center) - ScreenSize / 2f).Clamp(Vector2.Zero, (RoomSize - ScreenSize).Max(Vector2.Zero));
        foreach (string layer in Layers)
        {
            Sprite2D image = ImageOf(layer);
            if (image == null || !_states.TryGetValue(layer, out LayerState state)) continue;
            Vector2 p = Settings.ScrollOf(layer);
            image.GlobalPosition = room.ToGlobal(state.Fit.Offset + (Vector2.One - p) * camera);
        }
    }

    private bool SourcesChanged(string roomId)
    {
        if (roomId != RoomId) return true;
        BackgroundSettings settings = BackgroundSettings.LoadDefault();
        foreach (string layer in Layers)
        {
            if (!_states.TryGetValue(layer, out LayerState state)) return true;
            string path = ResolvePath(settings, roomId, layer);
            if ((path ?? "占位") != state.Source) return true;
            if (path != null && GD.Load<Texture2D>(path).GetSize() != state.TextureSize) return true;
        }
        return false;
    }
#endif
}
