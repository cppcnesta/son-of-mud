using System.Collections.Generic;
using Godot;
using SonOfMud.Player.States;

namespace SonOfMud.Player;

/// <summary>
/// 按「形态前缀_动画名」播放动画（例如 mud_attack），缺少带前缀的动画时退回无前缀的名字。
/// 动作时长以 .tres 为准，动画只负责表现：SpeedScale = 帧数 ÷ (基础 FPS × 目标时长)（规格第 6 节）。
///
/// 美术图按命名自动载入（不用在编辑器里手动做 SpriteFrames）：
/// - 每个动作一张横排图，放在 ArtDirectory 下。先找分形态的 spr_player_water_&lt;动作&gt;.png / spr_player_mud_&lt;动作&gt;.png，
///   没有就找两形态共用的 spr_player_&lt;动作&gt;.png，都没有就用色块占位。
/// - 帧是正方形：帧数 = 图宽 ÷ 图高（只有一张图就是 1 帧）。图的分辨率随意，游戏里每帧都缩放成 ArtCanvas（128）大小。
/// - 每帧的底边中点就是脚底，对齐到身体碰撞框的底边。
/// - 真图不染形态色（占位色块仍按形态着色）；角色朝左时水平翻转。
/// </summary>
[GlobalClass]
public partial class PlayerAnimator : Node
{
    public const string DefaultArtDirectory = "res://assets/art/player";

    [Export] public AnimatedSprite2D Sprite { get; set; }
    [Export] public float BaseFps { get; set; } = 12f;
    /// <summary>美术图所在文件夹。</summary>
    [Export] public string ArtDirectory { get; set; } = DefaultArtDirectory;
    /// <summary>游戏里一帧画布的边长（像素）。源图不管多大，都缩放到这个大小。</summary>
    [Export] public float ArtCanvas { get; set; } = 128f;

    /// <summary>占位动画：名字 → (水帧数, 泥帧数, 循环)。也是会去找美术图的全部动作。</summary>
    private static readonly Dictionary<string, (int water, int mud, bool loop)> Placeholder = new()
    {
        ["idle"] = (6, 6, true), ["run"] = (8, 8, true),
        ["jump_windup"] = (2, 4, false), ["rise"] = (2, 2, true), ["fall"] = (2, 2, true),
        ["wall_climb"] = (6, 6, true),
        ["attack"] = (5, 7, false), ["attack_down"] = (4, 4, false), ["attack_recover"] = (0, 6, false),
        ["slam_fall"] = (2, 2, true), ["slam_land"] = (4, 4, false),
        ["dodge"] = (4, 4, false), ["block"] = (3, 3, true),
        ["throw"] = (3, 3, false),
        ["hurt"] = (2, 2, false), ["stunned"] = (4, 4, true), ["death"] = (8, 8, false),
    };

    /// <summary>循环动作用美术图时的播放速度（帧 / 秒），没列的用 BaseFps。有固定时长的动作不看这里（按时长缩放）。</summary>
    private static readonly Dictionary<string, float> ArtFps = new() { ["idle"] = 6f };

    private static readonly string[] Forms = { "water", "mud" };

    private PlayerCharacter _player;
    /// <summary>用了美术图的动画 → 源图一帧的高度（像素），用来算缩放和脚底。</summary>
    private readonly Dictionary<string, int> _artHeight = new();

    public string CurrentAnimation => Sprite?.Animation.ToString() ?? "";
    public float CurrentSpeedScale => Sprite?.SpeedScale ?? 1f;
    /// <summary>当前播放的动画是不是美术图（不是色块占位）。</summary>
    public bool CurrentIsArt => _artHeight.ContainsKey(CurrentAnimation);
    public bool IsArt(string animation) => _artHeight.ContainsKey(animation);
    /// <summary>身体碰撞框底边在玩家本地坐标里的 y（脚底）。</summary>
    public float FootY { get; private set; }

    public static float SpeedScaleFor(int frames, float baseFps, float seconds) =>
        frames <= 0 || baseFps <= 0f || seconds <= 0f ? 1f : frames / (baseFps * seconds);

    /// <summary>横排图的帧数：帧是正方形，帧数 = 宽 ÷ 高（至少 1；竖长的单张图也算 1 帧）。</summary>
    public static int FrameCountFor(int width, int height) =>
        width <= 0 || height <= 0 ? 0 : Mathf.Max(1, width / height);

    public void Bind(PlayerCharacter player)
    {
        _player = player;
        Sprite ??= player.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        FootY = FindFootY(player);
        if (Sprite != null && Sprite.SpriteFrames == null) Sprite.SpriteFrames = BuildFrames();
        player.Machine.StateChanged += OnStateChanged;
        player.Forms.FormChanged += OnFormChanged;
        Refresh();
    }

    /// <summary>换一个美术文件夹重新载入（测试和以后换皮用）。</summary>
    public void UseArtDirectory(string directory)
    {
        ArtDirectory = directory;
        if (Sprite == null || !IsInstanceValid(Sprite)) return;
        Sprite.SpriteFrames = BuildFrames();
        Refresh();
    }

    private void OnStateChanged(StringName from, StringName to) => Refresh();
    private void OnFormChanged(int form) => Refresh();

    public override void _Process(double delta)
    {
        if (_player != null && IsInstanceValid(_player) && Sprite != null && IsInstanceValid(Sprite))
            Sprite.FlipH = _player.Facing < 0f;
    }

    public override void _ExitTree()
    {
        if (_player == null || !IsInstanceValid(_player)) return;
        if (_player.Machine != null && IsInstanceValid(_player.Machine)) _player.Machine.StateChanged -= OnStateChanged;
        if (_player.Forms != null && IsInstanceValid(_player.Forms)) _player.Forms.FormChanged -= OnFormChanged;
    }

    /// <summary>按当前状态和形态选动画并设置播放速度、缩放、脚底对齐和着色。</summary>
    public void Refresh()
    {
        if (!IsInsideTree() || _player == null || !IsInstanceValid(_player) || _player.Forms?.Current == null
            || Sprite == null || !IsInstanceValid(Sprite) || Sprite.SpriteFrames == null) return;
        if (_player.Machine.Current is not PlayerState state) return;
        string name = state.AnimName;
        if (string.IsNullOrEmpty(name)) return;
        string prefixed = $"{_player.Forms.Current.AnimPrefix}_{name}";
        string chosen = Sprite.SpriteFrames.HasAnimation(prefixed) ? prefixed : Sprite.SpriteFrames.HasAnimation(name) ? name : null;
        if (chosen == null) return;
        int frames = Sprite.SpriteFrames.GetFrameCount(chosen);
        float fps = (float)Sprite.SpriteFrames.GetAnimationSpeed(chosen);
        Sprite.Play(chosen);
        Sprite.SpeedScale = SpeedScaleFor(frames, fps, state.AnimDuration);
        Sprite.FlipH = _player.Facing < 0f;
        Sprite.Centered = true;
        if (_artHeight.TryGetValue(chosen, out int height))
        {
            // 美术图：缩放到 ArtCanvas，帧底边中点放在脚底；不染色
            float scale = ArtCanvas / height;
            Sprite.Scale = new Vector2(scale, scale);
            Sprite.Offset = new Vector2(0f, -height * 0.5f);
            Sprite.Position = new Vector2(0f, FootY);
            Sprite.Modulate = Colors.White;
        }
        else
        {
            // 色块占位：和身体碰撞框一样居中，按形态着色
            Sprite.Scale = Vector2.One;
            Sprite.Offset = Vector2.Zero;
            Sprite.Position = Vector2.Zero;
            Sprite.Modulate = _player.Forms.Current.Tint;
        }
    }

    private static float FindFootY(PlayerCharacter player)
    {
        var body = player.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
        return body?.Shape is RectangleShape2D rect ? body.Position.Y + rect.Size.Y * 0.5f : 24f;
    }

    /// <summary>每个形态、每个动作：分形态的图 → 共用的图 → 色块占位。</summary>
    private SpriteFrames BuildFrames()
    {
        _artHeight.Clear();
        var frames = new SpriteFrames();
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        foreach (string form in Forms)
        {
            bool isMud = form == "mud";
            foreach (var (anim, info) in Placeholder)
            {
                string name = $"{form}_{anim}";
                Texture2D art = LoadArt($"spr_player_{form}_{anim}.png") ?? LoadArt($"spr_player_{anim}.png");
                if (art != null && AddStrip(frames, name, art, info.loop, ArtFps.TryGetValue(anim, out float f) ? f : BaseFps))
                {
                    _artHeight[name] = art.GetHeight();
                    continue;
                }
                int count = isMud ? info.mud : info.water;
                if (count <= 0) continue;
                frames.AddAnimation(name);
                frames.SetAnimationLoop(name, info.loop);
                frames.SetAnimationSpeed(name, BaseFps);
                for (int i = 0; i < count; i++) frames.AddFrame(name, MakeBlock(i, count));
            }
        }
        return frames;
    }

    private Texture2D LoadArt(string file)
    {
        if (string.IsNullOrEmpty(ArtDirectory)) return null;
        string path = $"{ArtDirectory.TrimEnd('/')}/{file}";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>把一张横排图切成正方形帧加成动画。图宽不是图高的整数倍时，多出来的部分不用，并给出警告。</summary>
    private static bool AddStrip(SpriteFrames frames, string name, Texture2D strip, bool loop, float fps)
    {
        int w = strip.GetWidth(), h = strip.GetHeight();
        int count = FrameCountFor(w, h);
        if (count <= 0) return false;
        if (w > h && w % h != 0)
            GD.PushWarning($"主角美术图 {strip.ResourcePath}：宽 {w} 不是高 {h} 的整数倍，按 {count} 帧切，右边多出的部分不用。");
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

    /// <summary>48×48 的灰度色块，每帧亮度略有不同，方便肉眼看出动画在播。实际颜色由 Modulate 着色。</summary>
    private static ImageTexture MakeBlock(int index, int count)
    {
        var image = Image.CreateEmpty(48, 48, false, Image.Format.Rgba8);
        float shade = 0.7f + 0.3f * (count <= 1 ? 1f : index / (float)(count - 1));
        image.Fill(new Color(shade, shade, shade));
        return ImageTexture.CreateFromImage(image);
    }
}
