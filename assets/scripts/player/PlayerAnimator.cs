using System.Collections.Generic;
using Godot;
using SonOfMud.Player.States;

namespace SonOfMud.Player;

/// <summary>
/// 按「形态前缀_动画名」播放动画（例如 mud_attack），缺少带前缀的动画时退回无前缀的名字。
/// 动作时长以 .tres 为准，动画只负责表现：SpeedScale = 帧数 ÷ (基础 FPS × 目标时长)（规格第 6 节）。
/// SpriteFrames 没有配置时，自动生成色块占位（技术占位，不等美术）。
/// </summary>
[GlobalClass]
public partial class PlayerAnimator : Node
{
    [Export] public AnimatedSprite2D Sprite { get; set; }
    [Export] public float BaseFps { get; set; } = 12f;

    /// <summary>占位动画：名字 → (水帧数, 泥帧数, 循环)。帧数取自资源清单。</summary>
    private static readonly Dictionary<string, (int water, int mud, bool loop)> Placeholder = new()
    {
        ["idle"] = (6, 6, true), ["run"] = (8, 8, true),
        ["jump_windup"] = (2, 4, false), ["rise"] = (2, 2, true), ["fall"] = (2, 2, true),
        ["wall_climb"] = (6, 6, true),
        ["attack"] = (5, 7, false), ["attack_down"] = (4, 4, false), ["attack_recover"] = (0, 6, false),
        ["slam_fall"] = (2, 2, true), ["slam_land"] = (4, 4, false),
        ["dodge"] = (4, 4, false), ["block"] = (3, 3, true),
        ["hurt"] = (2, 2, false), ["stunned"] = (4, 4, true), ["death"] = (8, 8, false),
    };

    private Player _player;

    public string CurrentAnimation => Sprite?.Animation.ToString() ?? "";
    public float CurrentSpeedScale => Sprite?.SpeedScale ?? 1f;

    public static float SpeedScaleFor(int frames, float baseFps, float seconds) =>
        frames <= 0 || baseFps <= 0f || seconds <= 0f ? 1f : frames / (baseFps * seconds);

    public void Bind(Player player)
    {
        _player = player;
        Sprite ??= player.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        if (Sprite != null && Sprite.SpriteFrames == null) Sprite.SpriteFrames = BuildPlaceholderFrames(player);
        player.Machine.StateChanged += (_, _) => Refresh();
        player.Forms.FormChanged += _ => Refresh();
        Refresh();
    }

    /// <summary>按当前状态和形态选动画并设置播放速度。</summary>
    public void Refresh()
    {
        if (_player == null || Sprite?.SpriteFrames == null) return;
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
        Sprite.Modulate = _player.Forms.Current.Tint;
    }

    private SpriteFrames BuildPlaceholderFrames(Player player)
    {
        var frames = new SpriteFrames();
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        foreach (var (prefix, isMud) in new[] { ("water", false), ("mud", true) })
        {
            foreach (var (anim, info) in Placeholder)
            {
                int count = isMud ? info.mud : info.water;
                if (count <= 0) continue;
                string name = $"{prefix}_{anim}";
                frames.AddAnimation(name);
                frames.SetAnimationLoop(name, info.loop);
                frames.SetAnimationSpeed(name, BaseFps);
                for (int i = 0; i < count; i++) frames.AddFrame(name, MakeBlock(i, count));
            }
        }
        return frames;
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
