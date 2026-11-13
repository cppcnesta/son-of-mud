using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 横排序列帧图的共用处理（主角、敌人都用）：按命名载入、切成正方形帧加成动画、算播放速度倍率。
/// - 帧是正方形：帧数 = 图宽 ÷ 图高；只有一张图（包括竖长的单张图）就是 1 帧。
/// - 图宽不是图高的整数倍时，右边多出来的部分不用，并给出警告。
/// </summary>
public static class SpriteStrip
{
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
    private static readonly System.Collections.Generic.Dictionary<string, Rect2[]> GridCache = new();

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
            var list = new System.Collections.Generic.List<Rect2>();
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
