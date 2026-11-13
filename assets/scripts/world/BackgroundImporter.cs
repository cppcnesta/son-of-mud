using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间背景“按命名导入一次，之后手动调”。导入在房间场景里建普通节点（存进场景文件）：
/// <code>
/// Background（Node2D，z -100，第一个子节点）
///   Far  （Parallax2D，远景视差，Z −100）→ Image（Sprite2D，不居中）   tex_bg_s1_03_far.png
///   Mid  （Parallax2D，中景视差，Z −80） → Image                      tex_bg_s1_03_mid.png
///   Front（Parallax2D，近景视差，Z −60） → Image                      tex_bg_s1_03_front.png
///   Tool （Node2D，只是收纳）
///     Tree（Parallax2D，物件视差，Z −40）→ Image                      tex_bg_s1_03_tool_tree.png
///     Cloud …（每张物件图一个，各自的视差和层级）
/// </code>
/// 一层也可以分成几张图（tex_bg_s1_03_front_01.png、_front_02.png …）：这一层的节点变成收纳用的 Node2D，
/// 下面 Front01、Front02 …各一个 Parallax2D，默认视差同这一层，层级 −60、−61 …（01 在最上面，编号越大越靠后）。见 <see cref="PlanPieces"/>。
/// 层级都是绝对值（Z As Relative 关掉），直接和地形、主角的 0 比；Image 保持相对 0，跟随所在的层。
/// 先找房间专属图（s1_03），再找区域通用图（s1）。导入后这些节点完全归人手动调整。
///
/// 再次导入只补缺的、只换图：
/// - 层按名字认（不分大小写），Image 同理；补层时插到 Far → Mid → Front → Tool 的正确位置；
/// - 物件按“物件标识”认（节点元数据 <see cref="ToolMeta"/>，没有时按它的图片文件名认出并补记），
///   和节点名、当前用哪张图无关，所以区域图换成房间专属图时是原节点换图，位置、缩放、名字都保留。
///
/// 旧版背景（Background 上没有 <see cref="VersionMeta"/> = <see cref="CurrentVersion"/>）第一次再导入时升级一次，之后不再自动补值：
/// - Far / Mid / Front 还是“0 且相对层级”的，补上默认层级（视为旧默认值）；
/// - 直接放在 Tool 下的物件（Sprite2D 等），各包一层自己的 Parallax2D：原来 Tool 的视差、偏移、变换搬到每个物件上，
///   Tool 本身变成不起作用的收纳节点，所以镜头在任何位置时画面都和原来一样。
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
    /// <summary>分张节点上记的元数据：层和编号（如 "front_01"）。</summary>
    public const string PieceMeta = "bg_piece";
    /// <summary>一层最多分几张：默认层级每张差 1，19 张以内不会碰到下一层（层与层之间差 20）。</summary>
    public const int MaxPieces = 19;
    /// <summary>Background 上记的版本号。没有或小于 <see cref="CurrentVersion"/> 的旧版背景，再导入时升级一次。</summary>
    public const string VersionMeta = "bg_version";
    public const int CurrentVersion = 2;
    public const int BackgroundZ = -100;

    /// <summary>所有层，按绘制顺序（后面的盖住前面的）。</summary>
    public static readonly string[] Layers = { Far, Mid, Front, Tool };
    /// <summary>一层一张图（Image）的层。</summary>
    public static readonly string[] ImageLayers = { Far, Mid, Front };

    /// <summary>旧版 Tool（Parallax2D）迁移时搬到每个物件上、然后在 Tool 上恢复成默认值的属性。</summary>
    private static readonly string[] ParallaxProperties =
    {
        "scroll_scale", "scroll_offset", "repeat_size", "repeat_times", "autoscroll", "screen_offset",
        "limit_begin", "limit_end", "follow_viewport", "ignore_camera_scroll", "position", "rotation", "scale", "skew",
    };

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
        /// <summary>旧版物件包一层自己的 Parallax2D（oldMeta：物件节点原来记的标识，撤销时还原）。</summary>
        public readonly List<(Node2D item, Parallax2D wrapper, string oldMeta)> Wraps = new();
        /// <summary>属性改动：升级时补层级、把旧 Tool 恢复成不起作用的收纳节点。</summary>
        public readonly List<(GodotObject target, StringName property, Variant value, Variant old)> Properties = new();
        public readonly List<(Sprite2D image, Texture2D texture)> Retextures = new();
        /// <summary>给已有的物件节点补记物件标识。</summary>
        public readonly List<(Node node, string key)> ToolTags = new();
        /// <summary>给已有的分张节点补记分张标识（front_01）。</summary>
        public readonly List<(Node node, string label)> PieceTags = new();
        /// <summary>要记上版本号的已有 Background（旧版升级）。</summary>
        public Node UpgradeBackground;
        public readonly List<string> Messages = new();

        public bool Empty => NewBackground == null && NewNodes.Count == 0 && Wraps.Count == 0 && Properties.Count == 0
                             && Retextures.Count == 0 && ToolTags.Count == 0 && PieceTags.Count == 0 && UpgradeBackground == null;
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
        bool upgrade = existing != null && existing.GetMeta(VersionMeta, 0).AsInt32() < CurrentVersion;
        if (upgrade)
        {
            plan.UpgradeBackground = existing;
            plan.Messages.Add($"{room.RoomId} 的背景是旧版，这次顺带升级（只做这一次）：补默认层级、物件各包一层自己的视差节点。");
        }

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
                if (plan.NewBackground == null)
                {
                    plan.NewBackground = new Node2D { Name = NodeName, ZIndex = BackgroundZ };
                    plan.NewBackground.SetMeta(VersionMeta, CurrentVersion);
                }
                plan.NewBackground.AddChild(node);
                return;
            }
            int at = InsertIndex(order, known, layer);
            order.Insert(at, node);
            plan.NewNodes.Add((existing, node, at));
        }

        foreach (string layer in ImageLayers)
        {
            if (upgrade && known.TryGetValue(layer, out Node old) && old is CanvasItem item && item.ZIndex == 0 && item.ZAsRelative)
            {
                plan.Properties.Add((item, CanvasItem.PropertyName.ZIndex, settings.ZOf(layer), 0));
                plan.Properties.Add((item, CanvasItem.PropertyName.ZAsRelative, false, true));
                plan.Messages.Add($"{room.RoomId} 的 {NodeName}/{old.Name} 补上层级 {settings.ZOf(layer)}。");
            }

            // 分张：tex_bg_s1_03_front_01.png、_front_02.png …，每张一个节点
            SortedDictionary<int, string> pieces = ResolvePieces(settings, room.RoomId, layer, plan.Messages);
            if (pieces.Count > 0)
            {
                PlanPieces(plan, room.RoomId, layer, pieces, settings, roomSize, screen, known, AddLayer);
                continue;
            }

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
            var parallax = new Parallax2D { Name = LayerNodeName(layer), ScrollScale = p, ZIndex = settings.ZOf(layer), ZAsRelative = false };
            (Sprite2D image, float scale) = Fitted(ImageName, texture, roomSize, screen, p, settings.ShakeMargin);
            parallax.AddChild(image);
            plan.Messages.Add($"{room.RoomId} 导入{LayerTitle(layer)} {source}（{texture.GetWidth()}×{texture.GetHeight()}，缩放到 {scale:0.###} 倍刚好盖满，层级 {settings.ZOf(layer)}）。");
            AddLayer(layer, parallax);
        }

        // 物件：每张图一个 Parallax2D（各自的视差和层级）→ Image
        Node toolLayer = known.GetValueOrDefault(Tool);
        var items = new Dictionary<string, (Node node, Sprite2D image)>();
        var names = new HashSet<string>();
        if (toolLayer != null)
        {
            bool neutral = toolLayer is not Parallax2D container || IsNeutral(container);
            foreach (Node child in toolLayer.GetChildren())
            {
                names.Add(child.Name);
                string key = ToolKeyOf(child);
                if (upgrade && child is Node2D legacy && child is not Parallax2D && (key != null || !neutral))
                {
                    Parallax2D wrapper = WrapperFor(legacy, toolLayer as Parallax2D, key, settings);
                    plan.Wraps.Add((legacy, wrapper, legacy.HasMeta(ToolMeta) ? legacy.GetMeta(ToolMeta).AsString() : ""));
                    plan.Messages.Add($"{room.RoomId} 的物件 {legacy.Name} 包一层自己的视差节点（视差 {wrapper.ScrollScale}，层级 {wrapper.ZIndex}{(wrapper.ZAsRelative ? "（相对）" : "")}），画面上的位置、大小不变。");
                    if (key != null && !items.ContainsKey(key)) items[key] = (wrapper, legacy as Sprite2D);
                    continue;
                }
                if (key != null && !items.ContainsKey(key)) items[key] = (child, ImageOfItem(child));
            }
            if (upgrade && toolLayer is Parallax2D oldContainer && !neutral)
            {
                Dictionary<string, Variant> defaults = ParallaxDefaults();
                foreach (string property in ParallaxProperties)
                {
                    Variant now = oldContainer.Get(property), fresh = defaults[property];
                    if (!SameValue(now, fresh)) plan.Properties.Add((oldContainer, property, fresh, now));
                }
                plan.Messages.Add($"{room.RoomId} 的 {NodeName}/{toolLayer.Name} 原来的视差搬到了每个物件上，它自己恢复成不起作用的收纳节点。");
            }
        }

        SortedDictionary<string, string> tools = ResolveTools(settings, room.RoomId);
        Node2D newToolLayer = null;
        Vector2 toolScroll = settings.ScrollOf(Tool);
        foreach ((string key, string path) in tools)
        {
            Texture2D texture = GD.Load<Texture2D>(path);
            if (texture == null)
            {
                plan.Messages.Add($"物件图 {path} 加载失败，跳过。");
                continue;
            }
            if (items.TryGetValue(key, out var found))
            {
                bool wrapped = plan.Wraps.Any(w => w.wrapper == found.node);
                if (!wrapped && !found.node.HasMeta(ToolMeta))
                {
                    plan.ToolTags.Add((found.node, key));
                    plan.Messages.Add($"{room.RoomId} 认出物件节点 {found.node.Name} 是 {key}，记上物件标识。");
                }
                if (found.image != null) PlanRetexture(plan, found.image, texture, $"{room.RoomId} 的物件 {key}（{found.node.Name}）", path);
                else plan.Messages.Add($"{room.RoomId} 的物件 {key}（{found.node.Name}）下面没有 {ImageName}，没有换图。");
                continue;
            }

            string name = UniqueName(ToolNodeName(key), names);
            names.Add(name);
            var item = new Parallax2D { Name = name, ScrollScale = toolScroll, ZIndex = settings.ToolZ, ZAsRelative = false };
            item.SetMeta(ToolMeta, key);
            (Sprite2D image, float scale) = Fitted(ImageName, texture, roomSize, screen, toolScroll, settings.ShakeMargin);
            item.AddChild(image);
            if (toolLayer != null) plan.NewNodes.Add((toolLayer, item, -1));
            else (newToolLayer ??= new Node2D { Name = LayerNodeName(Tool) }).AddChild(item);
            plan.Messages.Add($"{room.RoomId} 导入物件 {key} {path}（节点 {name}，视差 {toolScroll}，层级 {settings.ToolZ}，缩放到 {scale:0.###} 倍，整张图盖满房间）。");
        }
        if (newToolLayer != null) AddLayer(Tool, newToolLayer);
        return plan;
    }

    /// <summary>
    /// 一层分成几张图：这一层的节点（Front 等）只是收纳用的 Node2D，下面每张一个 Parallax2D（Front01、Front02 …）→ Image。
    /// 第 n 张默认层级 = 这一层的层级 −(n−1)：01 在最上面，编号越大越靠后；视差默认都用这一层的。
    /// 已有的分张按 <see cref="PieceMeta"/> 认（没有时按图片文件名），只换图；缺的补上，按编号插到对的位置。
    /// 这一层已经是单张图（Parallax2D 下面直接是 Image）时不自动拆，只提示。
    /// </summary>
    private static void PlanPieces(ImportPlan plan, string roomId, string layer, SortedDictionary<int, string> pieces, BackgroundSettings settings,
                                   Vector2 roomSize, Vector2 screen, Dictionary<string, Node> known, Action<string, Node> addLayer)
    {
        string title = LayerTitle(layer);
        if (ResolvePath(settings, roomId, layer) is string single)
            plan.Messages.Add($"{roomId} 的{title}有分张图，单张的 {single.GetFile()} 没用上。");

        Node container = known.GetValueOrDefault(layer);
        if (container != null && (container is Parallax2D || FindChild(container, ImageName) is Sprite2D))
        {
            plan.Messages.Add($"{roomId} 的 {NodeName}/{container.Name} 已经是单张图，没有自动拆成分张（会丢掉你调好的位置）。"
                              + $"要按分张导入，先删掉 {container.Name} 节点再点导入。");
            return;
        }

        // 模拟收纳节点的子节点顺序：新的一张插到编号比它大的第一张前面
        var order = container?.GetChildren().ToList() ?? new List<Node>();
        var existing = new Dictionary<int, Node>();
        var names = new HashSet<string>(order.Select(n => n.Name.ToString()));
        foreach (Node child in order)
            if (PieceNumberOf(child, layer) is int n && !existing.ContainsKey(n)) existing[n] = child;
        var numbers = order.ToDictionary(n => n, n => PieceNumberOf(n, layer) ?? int.MaxValue);

        Node2D fresh = null;
        Vector2 p = settings.ScrollOf(layer);
        foreach ((int number, string path) in pieces)
        {
            Texture2D texture = GD.Load<Texture2D>(path);
            if (texture == null)
            {
                plan.Messages.Add($"分张图 {path} 加载失败，跳过。");
                continue;
            }
            string label = $"{layer}_{number:00}";
            if (existing.TryGetValue(number, out Node found))
            {
                if (!found.HasMeta(PieceMeta))
                {
                    plan.PieceTags.Add((found, label));
                    plan.Messages.Add($"{roomId} 认出 {found.Name} 是{title}第 {number:00} 张，记上分张标识。");
                }
                if (ImageOfItem(found) is Sprite2D image) PlanRetexture(plan, image, texture, $"{roomId} 的{title}第 {number:00} 张（{found.Name}）", path);
                else plan.Messages.Add($"{roomId} 的{title}第 {number:00} 张（{found.Name}）下面没有 {ImageName}，没有换图。");
                continue;
            }

            string name = UniqueName($"{LayerNodeName(layer)}{number:00}", names);
            names.Add(name);
            int z = settings.ZOf(layer) - (number - 1);
            var piece = new Parallax2D { Name = name, ScrollScale = p, ZIndex = z, ZAsRelative = false };
            piece.SetMeta(PieceMeta, label);
            (Sprite2D sprite, float scale) = Fitted(ImageName, texture, roomSize, screen, p, settings.ShakeMargin);
            piece.AddChild(sprite);
            plan.Messages.Add($"{roomId} 导入{title}第 {number:00} 张 {path}（节点 {name}，视差 {p}，层级 {z}，缩放到 {scale:0.###} 倍刚好盖满）。");

            if (container == null)
            {
                fresh ??= new Node2D { Name = LayerNodeName(layer) };
                fresh.AddChild(piece);
                continue;
            }
            int at = order.FindIndex(n => numbers[n] > number);
            if (at < 0) at = order.Count;
            order.Insert(at, piece);
            numbers[piece] = number;
            plan.NewNodes.Add((container, piece, at));
        }
        if (fresh != null) addLayer(layer, fresh);
    }

    /// <summary>分张节点的编号：有元数据（far_02）用元数据，没有就按它的图片文件名认。认不出返回 null。</summary>
    public static int? PieceNumberOf(Node node, string layer)
    {
        if (node == null) return null;
        if (node.HasMeta(PieceMeta))
        {
            string meta = node.GetMeta(PieceMeta).AsString().ToLowerInvariant();
            string prefix = layer + "_";
            return meta.StartsWith(prefix) && int.TryParse(meta[prefix.Length..], out int n) ? n : null;
        }
        return PieceOfPath(ImageOfItem(node)?.Texture?.ResourcePath) is (string l, int number) && l == layer ? number : null;
    }

    private static readonly System.Text.RegularExpressions.Regex PiecePattern =
        new(@"^tex_bg_([a-z0-9]+(?:_[a-z0-9]+)?)_(far|mid|front)_(\d{1,2})\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>tex_bg_s1_03_front_01.png → (front, 1)；tex_bg_s1_mid_2.png → (mid, 2)；不是分张图（含物件图）返回 null。</summary>
    public static (string layer, int number)? PieceOfPath(string path)
    {
        var match = PiecePattern.Match((path ?? "").GetFile());
        if (!match.Success) return null;
        int number = int.Parse(match.Groups[3].Value);
        return number >= 1 && number <= MaxPieces ? (match.Groups[2].Value.ToLowerInvariant(), number) : null;
    }

    /// <summary>
    /// 一层的分张图：区域通用的 tex_bg_s1_front_01.png 和房间专属的 tex_bg_s1_03_front_01.png，按编号排序；同一个编号两边都有时用房间专属的。
    /// 同一边同一个编号写法重复（front_1 和 front_01）时用两位数的写法，其余的提示没用上，不看文件顺序。
    /// </summary>
    public static SortedDictionary<int, string> ResolvePieces(BackgroundSettings settings, string roomId, string layer, List<string> messages = null)
    {
        var result = new SortedDictionary<int, string>();
        if (settings == null || !TryParseRoom(roomId, out string region, out string roomKey)) return result;
        string dir = $"{settings.ArtRoot.TrimEnd('/')}/{region}";
        SortedSet<string> files = PngFiles(dir);
        foreach (string owner in new[] { region, roomKey }) // 后面的覆盖前面的：房间专属优先
        {
            var mine = new SortedDictionary<int, List<string>>();
            foreach (string file in files)
            {
                var match = PiecePattern.Match(file);
                if (!match.Success || !string.Equals(match.Groups[1].Value, owner, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(match.Groups[2].Value, layer, StringComparison.OrdinalIgnoreCase)) continue;
                int number = int.Parse(match.Groups[3].Value);
                if (number < 1 || number > MaxPieces)
                {
                    messages?.Add($"分张图 {file} 的编号要在 01–{MaxPieces:00} 之间，跳过。");
                    continue;
                }
                (mine.TryGetValue(number, out var list) ? list : mine[number] = new List<string>()).Add(file);
            }
            foreach ((int number, List<string> list) in mine)
            {
                string standard = $"tex_bg_{owner}_{layer}_{number:00}.png";
                string pick = list.FirstOrDefault(f => string.Equals(f, standard, StringComparison.OrdinalIgnoreCase)) ?? list.Min(StringComparer.Ordinal);
                if (list.Count > 1)
                    messages?.Add($"{LayerTitle(layer)}第 {number:00} 张有 {list.Count} 个文件（{string.Join("、", list)}），用 {pick}，其余没用上。");
                result[number] = $"{dir}/{pick}";
            }
        }
        return result;
    }

    /// <summary>目录里的 png 文件名（导出后的游戏里只剩 .import / .remap，去掉后缀还原），按名字排序；目录不存在时为空。</summary>
    private static SortedSet<string> PngFiles(string dir)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        if (!DirAccess.DirExistsAbsolute(dir)) return files;
        foreach (string file in DirAccess.GetFilesAt(dir))
        {
            string name = file;
            foreach (string suffix in new[] { ".import", ".remap" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name[..^suffix.Length];
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) files.Add(name);
        }
        return files;
    }

    /// <summary>
    /// 旧版物件的包装节点：原来 Tool（Parallax2D）的视差、偏移、变换都搬过来，物件自己的变换不动，所以画面上和原来一模一样。
    /// 层级：物件原来是“0 且相对”（旧默认）就用默认物件层级（绝对）；否则保持相对 0，层级算法和原来一样。
    /// </summary>
    private static Parallax2D WrapperFor(Node2D legacy, Parallax2D container, string key, BackgroundSettings settings)
    {
        var wrapper = new Parallax2D { Name = legacy.Name };
        if (container != null)
            foreach (string property in ParallaxProperties) wrapper.Set(property, container.Get(property));
        bool oldDefault = legacy.ZIndex == 0 && legacy.ZAsRelative;
        wrapper.ZIndex = oldDefault ? settings.ToolZ : 0;
        wrapper.ZAsRelative = !oldDefault;
        if (key != null) wrapper.SetMeta(ToolMeta, key);
        return wrapper;
    }

    private static bool IsNeutral(Parallax2D container)
    {
        Dictionary<string, Variant> defaults = ParallaxDefaults();
        return ParallaxProperties.All(p => SameValue(container.Get(p), defaults[p]));
    }

    private static Dictionary<string, Variant> _parallaxDefaults;

    /// <summary>Parallax2D 各属性的默认值（建一个临时节点读完就释放）。</summary>
    private static Dictionary<string, Variant> ParallaxDefaults()
    {
        if (_parallaxDefaults != null) return _parallaxDefaults;
        var probe = new Parallax2D();
        try { _parallaxDefaults = ParallaxProperties.ToDictionary(p => p, p => probe.Get(p)); }
        finally { probe.Free(); }
        return _parallaxDefaults;
    }

    private static bool SameValue(Variant a, Variant b) => GD.VarToStr(a) == GD.VarToStr(b);

    /// <summary>物件节点下的图：包装节点（Parallax2D）找它的 Image；Sprite2D 本身就是图。</summary>
    public static Sprite2D ImageOfItem(Node item) => item switch
    {
        Sprite2D sprite => sprite,
        null => null,
        _ => FindChild(item, ImageName) as Sprite2D,
    };

    /// <summary>Background 下所有的 Parallax2D（各层、各物件），按树顺序。</summary>
    public static List<Parallax2D> ParallaxNodes(Node background)
    {
        var result = new List<Parallax2D>();
        void Walk(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child is Parallax2D p) result.Add(p);
                Walk(child);
            }
        }
        if (background != null) Walk(background);
        return result;
    }

    /// <summary>镜头画面左上角在 C（房间坐标）时，这个 Parallax2D 在运行时的位置（编辑器预览用同一个算法）。</summary>
    public static Vector2 RuntimePosition(Parallax2D parallax, Vector2 c) => parallax.ScrollOffset + (Vector2.One - parallax.ScrollScale) * c;

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

    /// <summary>物件节点的标识：有元数据用元数据，没有就按它的图片文件名认（…_tool_tree.png → tree；包装节点看它的 Image）。认不出返回 null。</summary>
    public static string ToolKeyOf(Node node)
    {
        if (node == null) return null;
        if (node.HasMeta(ToolMeta)) return node.GetMeta(ToolMeta).AsString().ToLowerInvariant();
        return ToolKeyOfPath(ImageOfItem(node)?.Texture?.ResourcePath);
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
    /// 把导入写成一组撤销 / 重做操作。各条撤销操作互不依赖（谁先执行都一样）：移除新加的节点、拆掉包装节点（放回它原来的位置）、
    /// 换回原来的属性和图、去掉补记的标识和版本号。
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
        foreach ((Node2D item, Parallax2D wrapper, string oldMeta) in plan.Wraps)
        {
            sink.DoMethod(room, RoomRoot.MethodName.WrapToolItem, item, wrapper);
            sink.DoReference(wrapper);
            sink.UndoMethod(room, RoomRoot.MethodName.UnwrapToolItem, item, wrapper, oldMeta);
        }
        foreach ((GodotObject target, StringName property, Variant value, Variant old) in plan.Properties)
        {
            sink.DoProperty(target, property, value);
            sink.UndoProperty(target, property, old);
        }
        if (plan.UpgradeBackground != null)
        {
            sink.DoMethod(plan.UpgradeBackground, GodotObject.MethodName.SetMeta, VersionMeta, CurrentVersion);
            sink.UndoMethod(plan.UpgradeBackground, GodotObject.MethodName.RemoveMeta, VersionMeta);
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
        foreach ((Node node, string label) in plan.PieceTags)
        {
            sink.DoMethod(node, GodotObject.MethodName.SetMeta, PieceMeta, label);
            sink.UndoMethod(node, GodotObject.MethodName.RemoveMeta, PieceMeta);
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
            foreach (Parallax2D moved in ParallaxNodes(background))
                if (!moved.Position.IsEqualApprox(moved.ScrollOffset))
                    problems.Add($"{NodeName}/{background.GetPathTo(moved)} 节点本身被挪到了 {moved.Position.Round()}，游戏运行时它会被视差重置回 {moved.ScrollOffset.Round()}。"
                                 + $"要挪动它，请调它下面的 {ImageName}（或改 Scroll Offset）。");
        Node farLayer = FindLayer(background, Far);
        string what = "最远景";
        Parallax2D far = farLayer as Parallax2D;
        if (far == null && farLayer != null)
        {
            // 远景分张：用编号最大的那张兜底（默认它在最后面），它要能盖满画面
            far = farLayer.GetChildren().OfType<Parallax2D>().Where(c => PieceNumberOf(c, Far) != null).MaxBy(c => PieceNumberOf(c, Far));
            if (far != null) what = $"最远景的兜底图 {far.Name}";
        }
        if (far == null) return problems;
        Sprite2D image = FindChild(far, ImageName) as Sprite2D;
        if (image?.Texture == null)
        {
            problems.Add($"{NodeName}/{background.GetPathTo(far)} 下面没有带图的 {ImageName}，没法检查会不会露底。");
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
            Rect2 onScreen = TransformRect(background.Transform * (far.GetParent() is Node2D holder && holder != background ? holder.Transform : Transform2D.Identity),
                                           new Rect2(local.Position + parallaxPos, local.Size));
            onScreen.Position -= c;
            float left = onScreen.Position.X, top = onScreen.Position.Y;
            float right = screen.X - onScreen.End.X, bottom = screen.Y - onScreen.End.Y;
            var gaps = new List<string>();
            if (left > 0.5f) gaps.Add($"左边 {Mathf.CeilToInt(left)}");
            if (right > 0.5f) gaps.Add($"右边 {Mathf.CeilToInt(right)}");
            if (top > 0.5f) gaps.Add($"上边 {Mathf.CeilToInt(top)}");
            if (bottom > 0.5f) gaps.Add($"下边 {Mathf.CeilToInt(bottom)}");
            if (gaps.Count > 0)
                problems.Add($"镜头在房间{corners[index]}角{(withShake ? "（含震屏）" : "")}时，{what}{string.Join("、", gaps)} 像素露底。");
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
        SortedSet<string> files = PngFiles(dir);
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
