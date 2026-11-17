using System.Collections.Generic;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player.States;

namespace SonOfMud.Player;

/// <summary>
/// 按「形态前缀_动画名」播放动画（例如 mud_attack），缺少带前缀的动画时退回无前缀的名字。
/// 动作时长以 .tres 为准，动画只负责表现：SpeedScale = 帧数 ÷ (基础 FPS × 目标时长)（规格第 6 节）。
///
/// 美术图按命名自动载入（不用在编辑器里手动做 SpriteFrames）：
/// - 每个动作一张图，放在 ArtDirectory 下。先找分形态的 spr_player_water_&lt;动作&gt; / spr_player_mud_&lt;动作&gt;，
///   没有就找两形态共用的 spr_player_&lt;动作&gt;，都没有就用色块占位。
/// - 文件名可以写行列数（例如 spr_player_idle_4x1.png、spr_player_jump_windup_3x2.png）：按行列平均切，格子不要求正方形
///   （见 SpriteStrip）；不写行列数的按旧规则切（正方形横排：帧数 = 图宽 ÷ 图高，只有一张图就是 1 帧）。
/// - 固定比例显示：源图 4 像素 = 游戏 1 像素（SpriteStrip.SourceScale），画布大的动作角色大小不变。
/// - 每帧的底边中点就是脚底，对齐到身体碰撞框的底边。
/// - 真图不染形态色（占位色块仍按形态着色）；角色朝左时水平翻转。
/// - 水形态缺图时先用泥形态的同名图（水形态还没画）。
/// - 替补表（<see cref="AnimFallbacks"/>）：美术表里没画的动作用现成动画拼，只在这个动作没有专门的图、而拿来拼的那段有图时生效：
///   攀墙 = 循环播上升（贴墙不动时停在第一帧）；下砸下落 = 停在下劈最后一帧；下砸落地 = 起跳蓄力播一遍。
/// - 泥收势（攻击后能走动的 1.5 秒）没有专门的图时：前 <see cref="RecoverHoldSeconds"/> 秒停在刚结束的那一下攻击的最后一帧
///   （第 1 下后是 attack、连招第 2 下后是 attack_down），之后按有没有在走播待机 / 跑。
/// </summary>
[GlobalClass]
public partial class PlayerAnimator : Node
{
    public const string DefaultArtDirectory = "res://assets/art/player";

    [Export] public AnimatedSprite2D Sprite { get; set; }
    [Export] public float BaseFps { get; set; } = 12f;
    /// <summary>美术图所在文件夹。</summary>
    [Export] public string ArtDirectory { get; set; } = DefaultArtDirectory;

    /// <summary>
    /// 替补表：动作 → 拿来拼的动作[:方式]。方式：不写 = 原样播；last = 停在最后一帧；once = 播一遍不循环。
    /// 只在「这个动作没有专门的图」且「拿来拼的动作有图」时生效。
    /// </summary>
    [Export] public Godot.Collections.Dictionary<string, string> AnimFallbacks { get; set; } = new()
    {
        ["wall_climb"] = "rise",
        ["slam_fall"] = "attack_down:last",
        ["slam_land"] = "jump_windup:once",
    };

    /// <summary>泥收势没有专门的图时，先停在刚结束那一下攻击最后一帧的时长（秒）。</summary>
    [Export] public float RecoverHoldSeconds { get; set; } = 0.25f;

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
    /// <summary>按替补表拼出来的动画 → 拿来拼的动画。</summary>
    private readonly Dictionary<string, string> _fallbackOf = new();
    /// <summary>泥收势替补中：已过的时间（秒），停在哪一下攻击的最后一帧。</summary>
    private bool _recoverMode;
    private float _recoverElapsed;
    private string _recoverHold = "";
    /// <summary>下砸落地后正在播落地动画：已过的时间、要播多久（之后换成收势）。</summary>
    private bool _slamLandMode;
    private float _slamLandElapsed;
    private float _slamLandSeconds;
    private Vector2 _bodySize = new(48f, 48f);

    /// <summary>正在播下砸落地动画（收势的前一段）。</summary>
    public bool SlamLandActive => _slamLandMode;

    public string CurrentAnimation => Sprite?.Animation.ToString() ?? "";
    public float CurrentSpeedScale => Sprite?.SpeedScale ?? 1f;
    /// <summary>当前播放的动画是不是美术图（不是色块占位）。</summary>
    public bool CurrentIsArt => _artHeight.ContainsKey(CurrentAnimation);
    public bool IsArt(string animation) => _artHeight.ContainsKey(animation);
    /// <summary>这个动画是不是按替补表拼出来的（返回拿来拼的动画）。</summary>
    public string FallbackOf(string animation) => _fallbackOf.TryGetValue(animation, out string src) ? src : "";
    /// <summary>正在用「泥收势替补」（先停帧、再按移动播待机 / 跑）。</summary>
    public bool RecoverFallbackActive => _recoverMode;
    /// <summary>身体碰撞框底边在玩家本地坐标里的 y（脚底）。</summary>
    public float FootY { get; private set; }

    public static float SpeedScaleFor(int frames, float baseFps, float seconds) => SpriteStrip.SpeedScaleFor(frames, baseFps, seconds);

    /// <summary>横排图的帧数：帧是正方形，帧数 = 宽 ÷ 高（至少 1；竖长的单张图也算 1 帧）。</summary>
    public static int FrameCountFor(int width, int height) => SpriteStrip.FrameCountFor(width, height);

    public void Bind(PlayerCharacter player)
    {
        _player = player;
        Sprite ??= player.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        FootY = FindFootY(player);
        if (player.GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape is RectangleShape2D body) _bodySize = body.Size;
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
        if (_player == null || !IsInstanceValid(_player) || Sprite == null || !IsInstanceValid(Sprite)) return;
        Sprite.FlipH = _player.Facing < 0f;
        string current = Sprite.Animation.ToString();
        // 攀墙用上升拼的：贴墙不动时停在第一帧，动了再播
        if (_player.Machine.Current is WallClimbState && FallbackOf(current) != "")
        {
            bool moving = Mathf.Abs(_player.Velocity.Y) > 1f;
            if (!moving && Sprite.IsPlaying()) { Sprite.Pause(); Sprite.Frame = 0; }
            else if (moving && !Sprite.IsPlaying()) Sprite.Play(current);
        }
        if (_slamLandMode)
        {
            _slamLandElapsed += (float)delta;
            if (_slamLandElapsed < _slamLandSeconds) return;
            _slamLandMode = false;
            if (_player.Machine.Current is AttackRecoveryState recovery) PlayRecoveryAfterSlam(recovery);
            return;
        }
        if (_recoverMode)
        {
            _recoverElapsed += (float)delta;
            if (_recoverElapsed < RecoverHoldSeconds) return;
            string prefix = _player.Forms.Current.AnimPrefix;
            string target = Mathf.Abs(_player.Velocity.X) > 1f ? $"{prefix}_run" : $"{prefix}_idle";
            if (current != target && Sprite.SpriteFrames.HasAnimation(target))
            {
                Sprite.Play(target);
                Sprite.SpeedScale = 1f;
                ApplyLayout(target);
            }
        }
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
        string previous = Sprite.Animation.ToString();
        _recoverMode = false;
        _slamLandMode = false;
        // 下砸落地：收势的前一段先播落地动画（占用收势时间），之后在 _Process 里换成收势
        if (state is AttackRecoveryState { FromSlam: true } slamRecovery && Choose("slam_land") is string land)
        {
            _slamLandMode = true;
            _slamLandElapsed = 0f;
            _slamLandSeconds = slamRecovery.SlamLandSeconds;
            Sprite.FlipH = _player.Facing < 0f;
            Sprite.Play(land);
            Sprite.SpeedScale = SpriteStrip.SpeedScaleFor(Sprite.SpriteFrames, land, _slamLandSeconds);
            ApplyLayout(land);
            return;
        }
        string name = state.AnimName;
        if (string.IsNullOrEmpty(name)) return;
        string prefixed = $"{_player.Forms.Current.AnimPrefix}_{name}";
        string chosen = Sprite.SpriteFrames.HasAnimation(prefixed) ? prefixed : Sprite.SpriteFrames.HasAnimation(name) ? name : null;
        if (chosen == null) return;
        Sprite.FlipH = _player.Facing < 0f;
        // 泥收势没有专门的图，但刚才那一下攻击有图：先停在那一下的最后一帧，之后在 _Process 里按移动换待机 / 跑
        if (state is AttackRecoveryState && !_artHeight.ContainsKey(chosen) && _artHeight.ContainsKey(previous)
            && (previous.EndsWith("_attack") || previous.EndsWith("_attack_down")))
        {
            _recoverMode = true;
            _recoverElapsed = 0f;
            _recoverHold = previous;
            Sprite.Play(previous);
            Sprite.Pause();
            Sprite.Frame = Sprite.SpriteFrames.GetFrameCount(previous) - 1;
            ApplyLayout(previous);
            return;
        }
        Sprite.Play(chosen);
        // 实际 FPS 和各帧相对时长都算进去：手动做的 SpriteFrames 也能刚好播满动作时长
        Sprite.SpeedScale = SpriteStrip.SpeedScaleFor(Sprite.SpriteFrames, chosen, state.AnimDuration);
        ApplyLayout(chosen);
    }

    /// <summary>当前形态下 name 对应的动画（先找带形态前缀的）；都没有返回 null。</summary>
    private string Choose(string name)
    {
        string prefixed = $"{_player.Forms.Current.AnimPrefix}_{name}";
        return Sprite.SpriteFrames.HasAnimation(prefixed) ? prefixed : Sprite.SpriteFrames.HasAnimation(name) ? name : null;
    }

    /// <summary>
    /// 下砸落地动画播完、还在收势里：收势有专门的图就播剩下的时间；没有图（泥收势替补）就直接按有没有在走播待机 / 跑，
    /// 不再停在哪一下攻击的最后一帧（落地前不是攻击）。
    /// </summary>
    private void PlayRecoveryAfterSlam(AttackRecoveryState recovery)
    {
        string chosen = Choose(recovery.AnimName);
        if (chosen != null && _artHeight.ContainsKey(chosen))
        {
            Sprite.Play(chosen);
            Sprite.SpeedScale = SpriteStrip.SpeedScaleFor(Sprite.SpriteFrames, chosen, recovery.Remaining);
            ApplyLayout(chosen);
            return;
        }
        _recoverMode = true;
        _recoverElapsed = RecoverHoldSeconds;
        _recoverHold = "";
    }

    /// <summary>按动画是美术图还是色块，设好缩放、脚底对齐和着色。</summary>
    private void ApplyLayout(string chosen)
    {
        Sprite.Centered = true;
        if (_artHeight.TryGetValue(chosen, out int height))
        {
            // 美术图：固定比例（源图 4 像素 = 游戏 1 像素），帧底边中点放在脚底；不染色。
            // height 是补边后的帧高，同一动画每帧一样大，所以脚底每帧都在同一点
            float scale = 1f / SpriteStrip.SourceScale;
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

    /// <summary>每个形态、每个动作：分形态的图 → 共用的图 →（水形态）泥形态的图 → 替补表 → 色块占位。</summary>
    private SpriteFrames BuildFrames()
    {
        _artHeight.Clear();
        _fallbackOf.Clear();
        var frames = new SpriteFrames();
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        foreach (string form in Forms)
        {
            bool isMud = form == "mud";
            foreach (var (anim, info) in Placeholder)
            {
                string name = $"{form}_{anim}";
                float fps = ArtFps.TryGetValue(anim, out float f) ? f : BaseFps;
                Texture2D art = SpriteStrip.Find(ArtDirectory, $"spr_player_{form}_{anim}", "主角美术图", out int cols, out int rows)
                                ?? SpriteStrip.Find(ArtDirectory, $"spr_player_{anim}", "主角美术图", out cols, out rows);
                // 水形态还没画：缺图时先用泥形态的同名图
                if (art == null && !isMud) art = SpriteStrip.Find(ArtDirectory, $"spr_player_mud_{anim}", "主角美术图", out cols, out rows);
                bool added = art != null && (cols > 0
                    ? SpriteStrip.AddEvenGrid(frames, name, art, cols, rows, info.loop, fps)
                    : SpriteStrip.AddStrip(frames, name, art, info.loop, fps, "主角美术图"));
                if (added)
                {
                    _artHeight[name] = SpriteStrip.FrameHeight(frames, name);
                    continue;
                }
                int count = isMud ? info.mud : info.water;
                if (count <= 0) continue;
                frames.AddAnimation(name);
                frames.SetAnimationLoop(name, info.loop);
                frames.SetAnimationSpeed(name, BaseFps);
                for (int i = 0; i < count; i++) frames.AddFrame(name, MakeBlock(i, count, _bodySize));
            }
            ApplyFallbacks(frames, form);
        }
        return frames;
    }

    /// <summary>按替补表拼动画：这个动作没有专门的图、拿来拼的动作有图时，用那段图的帧替换掉色块。</summary>
    private void ApplyFallbacks(SpriteFrames frames, string form)
    {
        if (AnimFallbacks == null) return;
        foreach (var (anim, rule) in AnimFallbacks)
        {
            string name = $"{form}_{anim}";
            if (_artHeight.ContainsKey(name) || string.IsNullOrEmpty(rule)) continue;
            string[] parts = rule.Split(':');
            string src = $"{form}_{parts[0]}";
            string mode = parts.Length > 1 ? parts[1] : "";
            if (!_artHeight.ContainsKey(src) || !frames.HasAnimation(src)) continue;
            if (frames.HasAnimation(name)) frames.RemoveAnimation(name);
            frames.AddAnimation(name);
            int count = frames.GetFrameCount(src);
            bool loop = mode switch { "last" => true, "once" => false, _ => Placeholder.TryGetValue(anim, out var info) ? info.loop : frames.GetAnimationLoop(src) };
            frames.SetAnimationLoop(name, loop);
            frames.SetAnimationSpeed(name, frames.GetAnimationSpeed(src));
            if (mode == "last") frames.AddFrame(name, frames.GetFrameTexture(src, count - 1));
            else
                for (int i = 0; i < count; i++) frames.AddFrame(name, frames.GetFrameTexture(src, i), frames.GetFrameDuration(src, i));
            _artHeight[name] = _artHeight[src];
            _fallbackOf[name] = src;
        }
    }

    /// <summary>和身体碰撞框一样大的灰度色块，每帧亮度略有不同，方便肉眼看出动画在播。实际颜色由 Modulate 着色。</summary>
    private static ImageTexture MakeBlock(int index, int count, Vector2 size)
    {
        var image = Image.CreateEmpty(Mathf.Max(1, (int)size.X), Mathf.Max(1, (int)size.Y), false, Image.Format.Rgba8);
        float shade = 0.7f + 0.3f * (count <= 1 ? 1f : index / (float)(count - 1));
        image.Fill(new Color(shade, shade, shade));
        return ImageTexture.CreateFromImage(image);
    }
}
