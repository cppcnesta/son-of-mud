using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 序列帧图的共用处理（主角、敌人都用）：按命名找图、切帧加成动画、算播放速度倍率。
///
/// 新规则（文件名写行列数）：<c>&lt;名字&gt;_&lt;列&gt;x&lt;行&gt;.png</c>，例如 <c>spr_player_idle_4x1.png</c>、<c>spr_enemy_devout_idle_6x5.png</c>。
/// - 平均切：每格宽 = 图宽 ÷ 列数、高 = 图高 ÷ 行数，不要求正方形、不要求整数；格子边界取最近的整数像素。
///   从左到右、从上到下，整格透明的跳过（最后一行没排满）。
/// - 格子取整后大小可能差 1 像素：每帧用 AtlasTexture.Margin 补边到这张图里最大的格子（高度补在上面、宽度左右平分），
///   同一个动画每帧一样大，底边中点（脚底）在同一点。
/// - 显示固定比例：源图 <see cref="SourceScale"/> 像素 = 游戏 1 像素（主角、敌人一样）。
/// 不写行列数的图按旧规则切（主角：正方形横排 <see cref="AddStrip"/>；敌人：标准格子 <see cref="AddGrid"/>）。
/// </summary>
public static class SpriteStrip
{
    /// <summary>源图像素 ÷ 游戏像素：源图 4 像素显示成游戏里 1 像素。</summary>
    public const float SourceScale = 4f;

    private static readonly Regex LayoutSuffix = new(@"^(?<base>.+)_(?<cols>\d+)x(?<rows>\d+)\.png$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 解析文件名：<c>xxx_6x5.png</c> → (xxx, 6, 5)；<c>xxx.png</c> → (xxx, 0, 0)（不写行列数，按旧规则）；
    /// 不是 .png 返回 null。列、行至少 1。
    /// </summary>
    public static (string baseName, int cols, int rows)? ParseLayout(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".png")) return null;
        Match m = LayoutSuffix.Match(fileName);
        if (m.Success && int.TryParse(m.Groups["cols"].Value, out int cols) && int.TryParse(m.Groups["rows"].Value, out int rows) && cols >= 1 && rows >= 1)
            return (m.Groups["base"].Value, cols, rows);
        return (fileName[..^4], 0, 0);
    }

    /// <summary>
    /// 目录列表里的名字还原成原始文件名：导出后的游戏里列出来的是 <c>xxx.png.import</c> / <c>xxx.png.remap</c>，去掉后缀；去重。
    /// </summary>
    public static IEnumerable<string> SourceFileNames(IEnumerable<string> listing)
    {
        var seen = new HashSet<string>();
        foreach (string raw in listing)
        {
            string name = raw;
            if (name.EndsWith(".import")) name = name[..^7];
            else if (name.EndsWith(".remap")) name = name[..^6];
            if (seen.Add(name)) yield return name;
        }
    }

    /// <summary>
    /// 在 listing（目录里的文件名）里给 baseName 选一张图：带行列数的优先。
    /// - 多张带行列数的（如 idle_4x1 和 idle_2x2）：冲突，按文件名排序取第一张，message 是报错内容；
    /// - 带行列数的和不带的同时有：用带的，message 是警告内容；
    /// 找不到返回 null。
    /// </summary>
    public static (string file, int cols, int rows, string message, bool error)? Choose(IEnumerable<string> listing, string baseName)
    {
        var suffixed = new List<(string file, int cols, int rows)>();
        bool plain = false;
        foreach (string name in SourceFileNames(listing))
        {
            var layout = ParseLayout(name);
            if (layout == null || layout.Value.baseName != baseName)
            {
                if (name == baseName + ".png") plain = true;
                continue;
            }
            if (layout.Value.cols > 0) suffixed.Add((name, layout.Value.cols, layout.Value.rows));
            else plain = true;
        }
        if (suffixed.Count == 0) return plain ? (baseName + ".png", 0, 0, null, false) : null;
        suffixed.Sort((a, b) => string.CompareOrdinal(a.file, b.file));
        var pick = suffixed[0];
        if (suffixed.Count > 1)
            return (pick.file, pick.cols, pick.rows, $"{baseName}：同时有 {string.Join("、", suffixed.Select(s => s.file))}，按文件名排序用 {pick.file}，其余不用。", true);
        if (plain)
            return (pick.file, pick.cols, pick.rows, $"{baseName}：同时有 {baseName}.png 和 {pick.file}，用带行列数的 {pick.file}。", false);
        return (pick.file, pick.cols, pick.rows, null, false);
    }

    /// <summary>目录列表的缓存（同一次运行里多次找图只列一次目录）。</summary>
    private static readonly Dictionary<string, string[]> ListingCache = new();

    /// <summary>清掉目录列表和切图缓存（测试换图后用）。</summary>
    public static void ClearCaches()
    {
        ListingCache.Clear();
        GridCache.Clear();
        EvenCache.Clear();
    }

    private static string[] Listing(string directory)
    {
        if (ListingCache.TryGetValue(directory, out string[] cached)) return cached;
        string[] files = DirAccess.DirExistsAbsolute(directory) ? DirAccess.GetFilesAt(directory) : System.Array.Empty<string>();
        ListingCache[directory] = files;
        return files;
    }

    /// <summary>
    /// 在 directory 里找 baseName 的图（<c>baseName.png</c> 或 <c>baseName_&lt;列&gt;x&lt;行&gt;.png</c>），返回贴图和行列数（旧规则为 0, 0）。
    /// 冲突报错、并存给警告（what 是对这类图的称呼）。找不到返回 null。
    /// </summary>
    public static Texture2D Find(string directory, string baseName, string what, out int cols, out int rows)
    {
        cols = rows = 0;
        if (string.IsNullOrEmpty(directory)) return null;
        string dir = directory.TrimEnd('/');
        var choice = Choose(Listing(dir), baseName);
        if (choice == null) return null;
        var (file, c, r, message, error) = choice.Value;
        if (message != null && Reported.Add($"{dir}/{message}"))
        {
            if (error) GD.PushError($"{what} {dir}/{message}");
            else GD.PushWarning($"{what} {dir}/{message}");
        }
        string path = $"{dir}/{file}";
        if (!ResourceLoader.Exists(path)) return null;
        cols = c;
        rows = r;
        return GD.Load<Texture2D>(path);
    }

    /// <summary>报过的冲突 / 并存（同一条只报一次，不然每个摆放的敌人都报一遍）。</summary>
    private static readonly HashSet<string> Reported = new();
    /// <summary>报过的冲突 / 并存内容（测试用）。</summary>
    public static IReadOnlyCollection<string> ReportedMessages => Reported;

    /// <summary>按行列数平均切的结果缓存：图路径 + 行列 → 格子区域。</summary>
    private static readonly Dictionary<string, Rect2I[]> EvenCache = new();

    /// <summary>
    /// 按 cols × rows 平均切：第 c 列的左边界 = round(c × 图宽 ÷ 列数)，行同理；相邻格子连续、不重叠、不漏像素。
    /// 从左到右、从上到下，整格透明的跳过。返回有内容的格子区域（按播放顺序）。
    /// </summary>
    public static Rect2I[] EvenGridRegions(Texture2D sheet, int cols, int rows)
    {
        if (sheet == null || cols <= 0 || rows <= 0) return System.Array.Empty<Rect2I>();
        string key = $"{sheet.ResourcePath}|{sheet.GetRid()}|{cols}x{rows}";
        if (EvenCache.TryGetValue(key, out Rect2I[] cached)) return cached;
        int w = sheet.GetWidth(), h = sheet.GetHeight();
        Image image = sheet.GetImage();
        if (image != null && image.IsCompressed()) image.Decompress();
        var list = new List<Rect2I>();
        for (int r = 0; r < rows; r++)
        {
            int y0 = Edge(r, h, rows), y1 = Edge(r + 1, h, rows);
            for (int c = 0; c < cols; c++)
            {
                int x0 = Edge(c, w, cols), x1 = Edge(c + 1, w, cols);
                if (x1 <= x0 || y1 <= y0) continue;
                var region = new Rect2I(x0, y0, x1 - x0, y1 - y0);
                if (image != null && image.GetRegion(region).IsInvisible()) continue;
                list.Add(region);
            }
        }
        Rect2I[] result = list.ToArray();
        EvenCache[key] = result;
        return result;
    }

    /// <summary>第 i 条格子边界的像素位置：round(i × size ÷ count)。</summary>
    public static int Edge(int i, int size, int count) =>
        (int)System.Math.Round((double)i * size / count, System.MidpointRounding.AwayFromZero);

    /// <summary>
    /// 按行列数平均切好加成动画 name：每帧补边到最大的格子（高度补在上面、宽度左右平分），同一动画每帧一样大、脚底在同一点。
    /// 没有任何有内容的格子时返回 false。
    /// </summary>
    public static bool AddEvenGrid(SpriteFrames frames, string name, Texture2D sheet, int cols, int rows, bool loop, float fps)
    {
        Rect2I[] regions = EvenGridRegions(sheet, cols, rows);
        if (regions.Length == 0) return false;
        int maxW = regions.Max(r => r.Size.X), maxH = regions.Max(r => r.Size.Y);
        frames.AddAnimation(name);
        frames.SetAnimationLoop(name, loop);
        frames.SetAnimationSpeed(name, fps);
        foreach (Rect2I region in regions)
        {
            int extraW = maxW - region.Size.X, extraH = maxH - region.Size.Y;
            var margin = new Rect2(extraW * 0.5f, extraH, extraW, extraH);
            frames.AddFrame(name, new AtlasTexture { Atlas = sheet, Region = new Rect2(region.Position, region.Size), Margin = margin });
        }
        return true;
    }

    /// <summary>帧贴图显示出来的高度（像素），含补边（AtlasTexture 的 Margin）。</summary>
    public static int FrameHeight(SpriteFrames frames, string animation, int index = 0)
    {
        if (frames == null || !frames.HasAnimation(animation) || index < 0 || index >= frames.GetFrameCount(animation)) return 0;
        return frames.GetFrameTexture(animation, index)?.GetHeight() ?? 0;
    }

    /// <summary>横排图的帧数：帧是正方形，帧数 = 宽 ÷ 高（至少 1；竖长的单张图也算 1 帧）。</summary>
    public static int FrameCountFor(int width, int height) =>
        width <= 0 || height <= 0 ? 0 : Mathf.Max(1, width / height);

    /// <summary>等长帧时的速度倍率：帧数 ÷ (FPS × 目标时长)；任一项 ≤ 0 时为 1。</summary>
    public static float SpeedScaleFor(int frames, float fps, float seconds) =>
        frames <= 0 || fps <= 0f || seconds <= 0f ? 1f : frames / (fps * seconds);

    /// <summary>
    /// 让 anim 整段刚好播 seconds 秒的速度倍率：各帧相对时长之和 ÷ (动画实际 FPS × 目标时长)。
    /// 手动做的动画（非 12 FPS、帧时长不等）也算得对；自动切出的等长帧结果同 <see cref="SpeedScaleFor(int,float,float)"/>。
    /// 动画不存在、没有帧、FPS 或时长 ≤ 0 时为 1。
    /// </summary>
    public static float SpeedScaleFor(SpriteFrames frames, StringName anim, float seconds)
    {
        if (frames == null || !frames.HasAnimation(anim) || seconds <= 0f) return 1f;
        int count = frames.GetFrameCount(anim);
        float fps = (float)frames.GetAnimationSpeed(anim);
        if (count <= 0 || fps <= 0f) return 1f;
        float total = 0f;
        for (int i = 0; i < count; i++) total += frames.GetFrameDuration(anim, i);
        return total <= 0f ? 1f : total / (fps * seconds);
    }

    /// <summary>directory/file 存在就载入成贴图，否则返回 null。</summary>
    public static Texture2D Load(string directory, string file)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        string path = $"{directory.TrimEnd('/')}/{file}";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>网格切图的结果缓存（同一张图被多个敌人用时只算一次）：图路径 + 格子边长 → 有内容的格子。</summary>
    private static readonly Dictionary<string, Rect2[]> GridCache = new();

    /// <summary>
    /// 严格按 cell × cell 的格子切图：从左到右、从上到下，整格透明的跳过。
    /// - 图的宽或高比一格小：整张图算 1 帧（照样显示，给警告）；
    /// - 宽或高不是 cell 的整数倍：多出来的右边 / 下边不用，给警告；
    /// - 只有一行就是原来的横排图。
    /// 返回帧在图里的区域（按播放顺序）；没有任何有内容的格子时返回空数组。
    /// </summary>
    public static Rect2[] GridRegions(Texture2D sheet, int cell, string what)
    {
        if (sheet == null || cell <= 0) return System.Array.Empty<Rect2>();
        int w = sheet.GetWidth(), h = sheet.GetHeight();
        string key = $"{sheet.ResourcePath}|{sheet.GetRid()}|{cell}";
        if (GridCache.TryGetValue(key, out Rect2[] cached)) return cached;

        Rect2[] result;
        if (w < cell || h < cell)
        {
            GD.PushWarning($"{what} {sheet.ResourcePath}：{w}×{h} 比一格 {cell}×{cell} 小，整张图当 1 帧（照样显示）。");
            result = new[] { new Rect2(0, 0, w, h) };
        }
        else
        {
            int cols = w / cell, rows = h / cell;
            if (w % cell != 0 || h % cell != 0)
                GD.PushWarning($"{what} {sheet.ResourcePath}：{w}×{h} 不是 {cell} 的整数倍，按 {cols} 列 × {rows} 行切，右边 / 下边多出来的部分不用。");
            Image image = sheet.GetImage();
            if (image != null && image.IsCompressed()) image.Decompress();
            var list = new List<Rect2>();
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var region = new Rect2I(c * cell, r * cell, cell, cell);
                if (image != null && image.GetRegion(region).IsInvisible()) continue;   // 整格透明：最后一行没排满
                list.Add(new Rect2(region.Position, region.Size));
            }
            result = list.ToArray();
        }
        GridCache[key] = result;
        return result;
    }

    /// <summary>按 <see cref="GridRegions"/> 切好加成动画 name；没有任何帧时返回 false。</summary>
    public static bool AddGrid(SpriteFrames frames, string name, Texture2D sheet, int cell, bool loop, float fps, string what)
    {
        Rect2[] regions = GridRegions(sheet, cell, what);
        if (regions.Length == 0) return false;
        frames.AddAnimation(name);
        frames.SetAnimationLoop(name, loop);
        frames.SetAnimationSpeed(name, fps);
        foreach (Rect2 region in regions)
        {
            bool whole = region.Position == Vector2.Zero && region.Size == sheet.GetSize();
            frames.AddFrame(name, whole ? sheet : new AtlasTexture { Atlas = sheet, Region = region });
        }
        return true;
    }

    /// <summary>
    /// 把一张横排图切成正方形帧加成动画 name。图宽不是图高的整数倍时，多出来的部分不用，并给出警告（what 是警告里对这张图的称呼，例如「主角美术图」）。
    /// </summary>
    public static bool AddStrip(SpriteFrames frames, string name, Texture2D strip, bool loop, float fps, string what)
    {
        int w = strip.GetWidth(), h = strip.GetHeight();
        int count = FrameCountFor(w, h);
        if (count <= 0) return false;
        if (w > h && w % h != 0)
            GD.PushWarning($"{what} {strip.ResourcePath}：宽 {w} 不是高 {h} 的整数倍，按 {count} 帧切，右边多出的部分不用。");
        frames.AddAnimation(name);
        frames.SetAnimationLoop(name, loop);
        frames.SetAnimationSpeed(name, fps);
        if (count == 1 && w <= h)
        {
            frames.AddFrame(name, strip);
            return true;
        }
        for (int i = 0; i < count; i++)
            frames.AddFrame(name, new AtlasTexture { Atlas = strip, Region = new Rect2(i * h, 0, h, h) });
        return true;
    }
}
