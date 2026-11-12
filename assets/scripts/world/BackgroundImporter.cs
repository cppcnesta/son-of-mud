using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间背景“按命名导入一次，之后手动调”。导入在房间场景里建普通节点（存进场景文件）：
/// <code>
/// Background（Node2D，z -100，第一个子节点）
///   Far  （Parallax2D，远景系数）→ Image（Sprite2D，不居中）      tex_bg_s1_03_far.png
///   Mid  （Parallax2D，中景系数）→ Image                         tex_bg_s1_03_mid.png
///   Front（Parallax2D，近景系数）→ Image                         tex_bg_s1_03_front.png
///   Tool （Parallax2D，物件系数）→ Tree / Cloud / …（每张图一个） tex_bg_s1_03_tool_tree.png …
/// </code>
/// 先找房间专属图（s1_03），再找区域通用图（s1）。导入后这些节点完全归人手动调整。
///
/// 再次导入只补缺的、只换图：
/// - 层按名字认（不分大小写），Image 同理；补层时插到 Far → Mid → Front → Tool 的正确位置；
/// - 物件按“物件标识”认（节点元数据 <see cref="ToolMeta"/>，没有时按它当前图片的文件名认出并补记），
///   和节点名、当前用哪张图无关，所以区域图换成房间专属图时是原节点换图，位置、缩放、名字都保留。
///
/// 位置约定（C = 实际画面左上角，含平滑和震屏；S = 屏幕；R = 房间；M = 震屏余量；p = 视差系数）：
///   运行时 Parallax2D.position = scroll_offset + (1 − p)·C（实测 Godot 4.7；在编辑器里挪层节点本身，运行时会被覆盖）；
///   这一层最少要 Wreq = S.x + p.x·(R.x − S.x + 2M)，Hreq 同理；
///   导入时等比缩放到刚好盖满（太大缩小、太小放大），图放在 (−p.x·M, Hreq − H − p.y·M)：
///   镜头在最左（含震屏）时图的左边对齐画面左边，在最下（含震屏）时图的底边对齐画面底边。
/// </summary>
public static class BackgroundImporter
{
    public const string NodeName = "Background";
    public const string ImageName = "Image";
    public const string Far = "far";
    public const string Mid = "mid";
    public const string Front = "front";
    public const string Tool = "tool";
    /// <summary>物件节点上记的元数据：物件标识（如 "tree"）。</summary>
    public const string ToolMeta = "bg_tool";
    public const int BackgroundZ = -100;

    /// <summary>所有层，按绘制顺序（后面的盖住前面的）。</summary>
    public static readonly string[] Layers = { Far, Mid, Front, Tool };
    /// <summary>一层一张图（Image）的层。</summary>
    public static readonly string[] ImageLayers = { Far, Mid, Front };

    public static string LayerNodeName(string layer) => layer switch
    {
        Far => "Far",
        Mid => "Mid",
        Front => "Front",
        _ => "Tool",
    };

    public static string LayerTitle(string layer) => layer switch
    {
        Far => "最远景",
        Mid => "中景",
        Front => "近景",
        _ => "物件",
    };

    /// <summary>一次导入要做的事，分开列出，编辑器里好做撤销（见 <see cref="Record"/>）。</summary>
    public sealed class ImportPlan
    {
        /// <summary>整棵新建的 Background（房间里原来没有）。</summary>
        public Node2D NewBackground;
        public Node ExistingBackground;
        /// <summary>往已有节点里加的新节点：补层（Index = 插入位置）或往已有 Tool 里补物件（Index = −1，加在最后）。</summary>
        public readonly List<(Node parent, Node node, int index)> NewNodes = new();
        public readonly List<(Sprite2D image, Texture2D texture)> Retextures = new();
        /// <summary>给已有的物件节点补记物件标识。</summary>
        public readonly List<(Node node, string key)> ToolTags = new();
        public readonly List<string> Messages = new();
        public bool Empty => NewBackground == null && NewNodes.Count == 0 && Retextures.Count == 0 && ToolTags.Count == 0;
    }

    // =================================================================
    // 导入
    // =================================================================

    /// <summary>
    /// 算出这次导入要做什么，不改场景。<paramref name="textureOverride"/> 只给测试用：按层（far / mid / front）返回一张图
    /// （null 就照常按命名查找）；物件总是按命名从文件查找。
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
        TryParseRoom(room.RoomId, out string region, out string roomKey);

        // 模拟 Background 的子节点顺序，算出补层时的插入位置（各步按顺序执行，所以位置在执行时也成立）
        List<Node> order = existing?.GetChildren().ToList() ?? new List<Node>();
        var known = new Dictionary<string, Node>();
        foreach (string layer in Layers)
            if (FindLayer(existing, layer) is Node node) known[layer] = node;

        void AddLayer(string layer, Node node)
        {
            known[layer] = node;
            if (existing == null)
            {
                plan.NewBackground ??= new Node2D { Name = NodeName, ZIndex = BackgroundZ };
                plan.NewBackground.AddChild(node);
                return;
            }
            int at = InsertIndex(order, known, layer);
            order.Insert(at, node);
            plan.NewNodes.Add((existing, node, at));
        }

        foreach (string layer in ImageLayers)
        {
            Texture2D texture = textureOverride?.Invoke(layer);
            string source = texture != null ? "测试注入" : ResolvePath(settings, room.RoomId, layer);
            if (texture == null && source != null) texture = GD.Load<Texture2D>(source);
            if (texture == null)
            {
                plan.Messages.Add($"{room.RoomId} 没有{LayerTitle(layer)}图（找过 {settings.ArtRoot}/{region}/tex_bg_{roomKey}_{layer}.png 和 tex_bg_{region}_{layer}.png），跳过这一层。");
                continue;
            }

            if (known.TryGetValue(layer, out Node layerNode))
            {
                if (FindChild(layerNode, ImageName) is Sprite2D existingImage)
                    PlanRetexture(plan, existingImage, texture, $"{room.RoomId} 的{LayerTitle(layer)}", source);
                else
                    plan.Messages.Add($"{room.RoomId} 的 {NodeName}/{layerNode.Name} 下面没有名为 {ImageName} 的 Sprite2D，没有改动它。");
                continue;
            }

            Vector2 p = settings.ScrollOf(layer);
            var parallax = new Parallax2D { Name = LayerNodeName(layer), ScrollScale = p };
            (Sprite2D image, float scale) = Fitted(ImageName, texture, roomSize, screen, p, settings.ShakeMargin);
            parallax.AddChild(image);
            plan.Messages.Add($"{room.RoomId} 导入{LayerTitle(layer)} {source}（{texture.GetWidth()}×{texture.GetHeight()}，缩放到 {scale:0.###} 倍刚好盖满）。");
            AddLayer(layer, parallax);
        }

        // 物件：每张图一个节点
        SortedDictionary<string, string> tools = ResolveTools(settings, room.RoomId);
        if (tools.Count == 0) return plan;
        Node toolLayer = known.GetValueOrDefault(Tool);
        var items = new Dictionary<string, Node>();
        var names = new HashSet<string>();
        if (toolLayer != null)
            foreach (Node child in toolLayer.GetChildren())
            {
                names.Add(child.Name);
                if (ToolKeyOf(child) is string key && !items.ContainsKey(key)) items[key] = child;
            }

        Parallax2D newToolLayer = null;
        Vector2 toolScroll = settings.ScrollOf(Tool);
        foreach ((string key, string path) in tools)
        {
            Texture2D texture = GD.Load<Texture2D>(path);
            if (texture == null)
            {
                plan.Messages.Add($"物件图 {path} 加载失败，跳过。");
                continue;
            }
            if (items.TryGetValue(key, out Node item))
            {
                if (!item.HasMeta(ToolMeta))
                {
                    plan.ToolTags.Add((item, key));
                    plan.Messages.Add($"{room.RoomId} 认出物件节点 {item.Name} 是 {key}，记上物件标识。");
                }
                if (item is Sprite2D sprite) PlanRetexture(plan, sprite, texture, $"{room.RoomId} 的物件 {key}（{item.Name}）", path);
                else plan.Messages.Add($"{room.RoomId} 的物件 {key}（{item.Name}）不是 Sprite2D，没有换图。");
                continue;
            }

            string name = UniqueName(ToolNodeName(key), names);
            names.Add(name);
            (Sprite2D created, float scale) = Fitted(name, texture, roomSize, screen, toolScroll, settings.ShakeMargin);
            created.SetMeta(ToolMeta, key);
            if (toolLayer != null) plan.NewNodes.Add((toolLayer, created, -1));
            else (newToolLayer ??= new Parallax2D { Name = LayerNodeName(Tool), ScrollScale = toolScroll }).AddChild(created);
            plan.Messages.Add($"{room.RoomId} 导入物件 {key} {path}（节点 {name}，缩放到 {scale:0.###} 倍，整张图盖满房间）。");
        }
        if (newToolLayer != null) AddLayer(Tool, newToolLayer);
        return plan;
    }

    private static void PlanRetexture(ImportPlan plan, Sprite2D image, Texture2D texture, string what, string source)
    {
        if (SameTexture(image.Texture, texture))
            plan.Messages.Add($"{what}已经是 {source}，不用更新。");
        else
        {
            plan.Retextures.Add((image, texture));
            plan.Messages.Add($"{what}换成 {source}（缩放和位置保持你调好的）。");
        }
    }

    private static bool SameTexture(Texture2D a, Texture2D b) =>
        a == b || (a != null && b != null && b.ResourcePath != "" && a.ResourcePath == b.ResourcePath);

    /// <summary>建一个不居中的 Sprite2D，等比缩放到刚好盖满这一层需要的尺寸。</summary>
    private static (Sprite2D sprite, float scale) Fitted(string name, Texture2D texture, Vector2 roomSize, Vector2 screen, Vector2 p, float margin)
    {
        Vector2 required = RequiredSize(roomSize, screen, p, margin);
        (float scale, Vector2 offset) = CoverFit(texture.GetSize(), required, p, margin);
        return (new Sprite2D { Name = name, Centered = false, Texture = texture, Scale = Vector2.One * scale, Position = offset }, scale);
    }

    /// <summary>新层的插入位置：紧跟在它前面最近的一层后面；前面没有就放在它后面最近的一层前面；都没有放最后。</summary>
    private static int InsertIndex(List<Node> order, Dictionary<string, Node> known, string layer)
    {
        int self = Array.IndexOf(Layers, layer);
        for (int i = self - 1; i >= 0; i--)
            if (known.TryGetValue(Layers[i], out Node before) && order.IndexOf(before) is int at && at >= 0) return at + 1;
        for (int i = self + 1; i < Layers.Length; i++)
            if (known.TryGetValue(Layers[i], out Node after) && order.IndexOf(after) is int at && at >= 0) return at;
        return order.Count;
    }

    /// <summary>按名字找层节点（不分大小写）。</summary>
    public static Node FindLayer(Node background, string layer) => FindChild(background, LayerNodeName(layer));

    private static Node FindChild(Node parent, string name)
    {
        if (parent == null) return null;
        foreach (Node child in parent.GetChildren())
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    /// <summary>物件节点的标识：有元数据用元数据，没有就按当前图片文件名认（…_tool_tree.png → tree）。认不出返回 null。</summary>
    public static string ToolKeyOf(Node node)
    {
        if (node == null) return null;
        if (node.HasMeta(ToolMeta)) return node.GetMeta(ToolMeta).AsString().ToLowerInvariant();
        return node is Sprite2D sprite ? ToolKeyOfPath(sprite.Texture?.ResourcePath) : null;
    }

    /// <summary>tex_bg_s1_03_tool_tree.png / tex_bg_s1_tool_tree.png → tree；不是物件图返回 null。</summary>
    public static string ToolKeyOfPath(string path)
    {
        string file = (path ?? "").GetFile();
        if (!file.StartsWith("tex_bg_", StringComparison.OrdinalIgnoreCase) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
        string marker = $"_{Tool}_";
        int at = file.IndexOf(marker, "tex_bg_".Length - 1, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        string key = file[(at + marker.Length)..^".png".Length].ToLowerInvariant();
        return key.Length > 0 ? key : null;
    }

    /// <summary>tree → Tree，big_tree → BigTree。</summary>
    public static string ToolNodeName(string key)
    {
        string name = string.Concat(key.Split('_', '-', ' ').Where(s => s.Length > 0).Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
        return name.Length > 0 ? name : "Item";
    }

    private static string UniqueName(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!taken.Contains($"{name}{i}")) return $"{name}{i}";
    }

    // =================================================================
    // 执行（直接执行 / 编辑器撤销管理器 / 测试里的 UndoRedo 走同一份操作）
    // =================================================================

    /// <summary>撤销系统的最小接口：编辑器里接 EditorUndoRedoManager，测试里接 UndoRedo，直接执行时只做 Do。</summary>
    public interface IUndoSink
    {
        void DoMethod(GodotObject target, StringName method, params Variant[] args);
        void UndoMethod(GodotObject target, StringName method, params Variant[] args);
        void DoProperty(GodotObject target, StringName property, Variant value);
        void UndoProperty(GodotObject target, StringName property, Variant value);
        void DoReference(GodotObject target);
    }

    /// <summary>直接执行（游戏里 / 测试用）。</summary>
    public sealed class DirectSink : IUndoSink
    {
        public void DoMethod(GodotObject target, StringName method, params Variant[] args) => target.Call(method, args);
        public void UndoMethod(GodotObject target, StringName method, params Variant[] args) { }
        public void DoProperty(GodotObject target, StringName property, Variant value) => target.Set(property, value);
        public void UndoProperty(GodotObject target, StringName property, Variant value) { }
        public void DoReference(GodotObject target) { }
    }

    /// <summary>
    /// 把导入写成一组撤销 / 重做操作。各条撤销操作互不依赖（谁先执行都一样）：移除新加的节点、换回原来的图、去掉补记的标识。
    /// </summary>
    public static void Record(RoomRoot room, ImportPlan plan, IUndoSink sink)
    {
        if (plan.NewBackground != null)
        {
            sink.DoMethod(room, Node.MethodName.AddChild, plan.NewBackground);
            sink.DoMethod(room, Node.MethodName.MoveChild, plan.NewBackground, 0);
            sink.DoMethod(room, RoomRoot.MethodName.AssignOwner, plan.NewBackground);
            sink.DoReference(plan.NewBackground);
            sink.UndoMethod(room, Node.MethodName.RemoveChild, plan.NewBackground);
        }
        foreach ((Node parent, Node node, int index) in plan.NewNodes)
        {
            sink.DoMethod(parent, Node.MethodName.AddChild, node);
            if (index >= 0) sink.DoMethod(parent, Node.MethodName.MoveChild, node, index);
            sink.DoMethod(room, RoomRoot.MethodName.AssignOwner, node);
            sink.DoReference(node);
            sink.UndoMethod(parent, Node.MethodName.RemoveChild, node);
        }
        foreach ((Sprite2D image, Texture2D texture) in plan.Retextures)
        {
            sink.DoProperty(image, Sprite2D.PropertyName.Texture, texture);
            sink.UndoProperty(image, Sprite2D.PropertyName.Texture, image.Texture);
        }
        foreach ((Node node, string key) in plan.ToolTags)
        {
            sink.DoMethod(node, GodotObject.MethodName.SetMeta, ToolMeta, key);
            sink.UndoMethod(node, GodotObject.MethodName.RemoveMeta, ToolMeta);
        }
    }

    /// <summary>直接执行导入（游戏里 / 测试用；编辑器里由 RoomRoot 通过撤销管理器执行同样的步骤）。</summary>
    public static void Apply(RoomRoot room, ImportPlan plan) => Record(room, plan, new DirectSink());

    // =================================================================
    // 露底检查（编辑器警告 + 测试共用）
    // =================================================================

    /// <summary>
    /// 检查最底层（Far）在镜头四个角、再加朝外的最大震屏时能不能盖满画面；按游戏运行时的规则算
    /// （Parallax2D.position = scroll_offset + (1 − p)·C）。返回问题列表，空表示没问题。没有 Background / Far 时不检查。
    /// checkNodeMoves：是否提醒“挪了层节点本身”。只在编辑器里有意义（游戏里它们的位置本来就跟着镜头变），默认按是否在编辑器决定。
    /// </summary>
    public static List<string> CoverageProblems(RoomRoot room, BackgroundSettings settings, bool withShake = true, bool? checkNodeMoves = null)
    {
        var problems = new List<string>();
        settings ??= BackgroundSettings.LoadDefault();
        if (room?.GetNodeOrNull(NodeName) is not Node2D background) return problems;
        if (checkNodeMoves ?? Engine.IsEditorHint())
            foreach (string layer in Layers)
                if (FindLayer(background, layer) is Parallax2D moved && !moved.Position.IsEqualApprox(moved.ScrollOffset))
                    problems.Add($"{NodeName}/{moved.Name} 节点本身被挪到了 {moved.Position.Round()}，游戏运行时它会被视差重置回 {moved.ScrollOffset.Round()}。"
                                 + $"要挪动背景，请调它下面的 {ImageName} 或物件节点（或改 Scroll Offset）。");
        if (FindLayer(background, Far) is not Parallax2D far) return problems;
        Sprite2D image = FindChild(far, ImageName) as Sprite2D;
        if (image?.Texture == null)
        {
            problems.Add($"{NodeName}/{far.Name} 下面没有带图的 {ImageName}，没法检查会不会露底。");
            return problems;
        }
        if (!TryRoomSize(room.RoomId, out Vector2 roomSize)) return problems;

        Vector2 screen = ProjectScreenSize();
        float m = withShake ? settings.ShakeMargin : 0f;
        Vector2 travel = (roomSize - screen).Max(Vector2.Zero);
        // 图在 Far 局部坐标里的矩形（含 Image 的位置、缩放、区域）
        Rect2 local = TransformRect(image.Transform, image.GetRect());
        string[] corners = { "左上", "右上", "左下", "右下" };
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
                problems.Add($"镜头在房间{corners[index]}角{(withShake ? "（含震屏）" : "")}时，最远景{string.Join("、", gaps)} 像素露底。");
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
    // 纯计算 / 查找
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

    /// <summary>按命名查找（far / mid / front）：房间专属图 → 区域通用图；都没有返回 null。</summary>
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
    /// 物件图：区域通用的 tex_bg_s1_tool_*.png 和房间专属的 tex_bg_s1_03_tool_*.png，按物件标识排序；
    /// 同一个标识两边都有时用房间专属的。
    /// </summary>
    public static SortedDictionary<string, string> ResolveTools(BackgroundSettings settings, string roomId)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (settings == null || !TryParseRoom(roomId, out string region, out string roomKey)) return result;
        string dir = $"{settings.ArtRoot.TrimEnd('/')}/{region}";
        if (!DirAccess.DirExistsAbsolute(dir)) return result;
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in DirAccess.GetFilesAt(dir))
        {
            string name = file;
            foreach (string suffix in new[] { ".import", ".remap" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) files.Add(name);
        }
        foreach (string prefix in new[] { $"tex_bg_{region}_{Tool}_", $"tex_bg_{roomKey}_{Tool}_" }) // 后面的覆盖前面的：房间专属优先
            foreach (string file in files)
                if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && ToolKeyOfPath(file) is string key)
                    result[key] = $"{dir}/{file}";
        return result;
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
