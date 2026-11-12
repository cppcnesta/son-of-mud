using System.Collections.Generic;
using Godot;
using SonOfMud.Core;
using SonOfMud.Enemy.States;

namespace SonOfMud.Enemy;

/// <summary>
/// 敌人美术图按命名自动载入并按状态播放（规则同主角的 PlayerAnimator）。
/// - 图放在 ArtDirectory（assets/art/enemy）下，文件名 spr_enemy_&lt;Kind&gt;_&lt;动作&gt;.png；动作名由当前状态的 AnimName 给出。
/// - 横排图按正方形切帧（帧数 = 宽 ÷ 高，单张图就是 1 帧），每帧缩放到 Data.ArtCanvas 显示，帧底边中点对齐脚底（敌人原点）。
///   源图标准尺寸 = ArtCanvas × 4（普通敌人 512，守坑人 1024），不是标准尺寸照样显示，但给警告。
/// - 敌人的图画**朝左**（主角相反，画朝右）：Facing &gt; 0（朝右）时水平翻转。
/// - 不循环的动作拉伸到状态的 AnimDuration（读实际 FPS 和各帧相对时长，见 SpriteStrip.SpeedScaleFor）。
/// - 当前动作没有图：Sprite 隐藏，敌人本体照旧画色块（以及攻击红框、蝎尾预警）；有图：本体什么都不画。
/// - 状态颜色（前摇偏黄、守坑人僵直等）染在 Sprite 的 Modulate 上；守坑人变身闪白用一个小着色器。
/// - 显示节点：优先用敌人场景里名为 Sprite 的 AnimatedSprite2D，没有才新建一个。
///   Sprite 上已经手动填了 SpriteFrames 就原样使用（动画名用动作名），不再按命名生成。
/// </summary>
[GlobalClass]
public partial class EnemyAnimator : Node
{
    public const string DefaultArtDirectory = "res://assets/art/enemy";
    public const string SpriteName = "Sprite";
    public const float BaseFps = 12f;
    /// <summary>源图像素 ÷ 游戏像素（和主角一样）：源图标准帧边长 = ArtCanvas × 它。</summary>
    public const float SourceScale = 4f;

    /// <summary>会去找美术图的全部动作 → 是否循环（普通敌人 9 个 + 守坑人 10 个；守坑人「已击败」时节点隐藏，不需要图）。</summary>
    public static readonly IReadOnlyDictionary<string, bool> Actions = new Dictionary<string, bool>
    {
        ["idle"] = true, ["patrol"] = true, ["chase"] = true,
        ["attack_windup"] = false, ["attack_active"] = false, ["attack_recover"] = false,
        ["hurt"] = false, ["flee"] = true, ["dead"] = false,
        ["dormant"] = true, ["transform"] = false, ["approach"] = true,
        ["sweep_windup"] = false, ["sweep_strike"] = false, ["sweep_recover"] = false,
        ["tail_aim"] = true, ["tail_strike"] = false, ["tail_recover"] = false,
        ["staggered"] = true,
    };

    private const string FlashShaderCode = @"shader_type canvas_item;
uniform float flash = 0.0;
void fragment() {
	COLOR.rgb = mix(COLOR.rgb, vec3(1.0), flash);
}";
    private static Shader _flashShader;
    /// <summary>已经警告过尺寸不对的图（同一张图只警告一次，不然每个摆放的敌人都报一遍）。</summary>
    private static readonly HashSet<string> SizeWarned = new();
    /// <summary>警告过尺寸不对的图（测试用）。</summary>
    public static IReadOnlyCollection<string> WarnedSizes => SizeWarned;

    public AnimatedSprite2D Sprite { get; private set; }
    public string ArtDirectory { get; private set; } = DefaultArtDirectory;
    /// <summary>Sprite 用的是手动填的 SpriteFrames（不是按命名生成的）。</summary>
    public bool Manual { get; private set; }
    /// <summary>按命名生成的那一份（手动时为 null）。</summary>
    public SpriteFrames Generated { get; private set; }
    public string CurrentAnimation { get; private set; } = "";
    /// <summary>当前动作在播美术图（不是色块）。</summary>
    public bool CurrentIsArt { get; private set; }
    public float CurrentSpeedScale => Sprite?.SpeedScale ?? 1f;
    /// <summary>变身闪白的着色器材质（Sprite 原本没有材质时才装上）。</summary>
    public ShaderMaterial FlashMaterial { get; private set; }

    private EnemyBody _body;

    /// <summary>给敌人挂上动画组件：复用场景里的 Sprite，没有就新建；然后载入美术图。</summary>
    public static EnemyAnimator Attach(EnemyBody body)
    {
        var animator = new EnemyAnimator { Name = "Animator" };
        body.AddChild(animator);
        animator.Bind(body);
        return animator;
    }

    public void Bind(EnemyBody body)
    {
        _body = body;
        Sprite = body.GetNodeOrNull<AnimatedSprite2D>(SpriteName);
        if (Sprite == null)
        {
            Sprite = new AnimatedSprite2D { Name = SpriteName };
            body.AddChild(Sprite);
        }
        Manual = Sprite.SpriteFrames != null;
        if (Sprite.Material == null)
        {
            FlashMaterial = new ShaderMaterial { Shader = FlashShader() };
            Sprite.Material = FlashMaterial;
        }
        if (!Manual) Rebuild();
        body.Machine?.Connect(StateMachine.SignalName.StateChanged, new Callable(this, MethodName.OnStateChanged));
        Refresh();
    }

    /// <summary>换一个美术文件夹重新载入（测试用）。手动填了 SpriteFrames 的不受影响。</summary>
    public void UseArtDirectory(string directory)
    {
        ArtDirectory = directory;
        if (Manual) return;
        Rebuild();
        Refresh();
    }

    private void Rebuild()
    {
        Generated = BuildFrames(_body?.Data?.Kind.ToString() ?? "", ArtDirectory, _body?.Data?.ArtCanvas ?? 128f, Actions.Keys);
        Sprite.SpriteFrames = Generated;
    }

    private void OnStateChanged(StringName from, StringName to) => Refresh();

    public override void _Process(double delta) => UpdateLook();

    /// <summary>按当前状态选动作：有图就播（缩放、脚底对齐、拉伸到动作时长），没图就隐藏 Sprite 让本体画色块。</summary>
    public void Refresh()
    {
        if (_body == null || !IsInstanceValid(_body) || Sprite == null || !IsInstanceValid(Sprite)) return;
        string name = (_body.Machine?.Current as EnemyState)?.AnimName ?? "";
        SpriteFrames frames = Sprite.SpriteFrames;
        CurrentAnimation = name;
        CurrentIsArt = name != "" && frames != null && frames.HasAnimation(name) && frames.GetFrameCount(name) > 0;
        Sprite.Visible = CurrentIsArt;
        if (CurrentIsArt)
        {
            Sprite.Play(name);
            Sprite.SetFrameAndProgress(0, 0f);
            float seconds = (_body.Machine.Current as EnemyState)?.AnimDuration ?? 0f;
            Sprite.SpeedScale = seconds > 0f ? SpriteStrip.SpeedScaleFor(frames, name, seconds) : 1f;
            Layout(Sprite, FrameHeight(frames, name), _body.Data?.ArtCanvas ?? 128f);
        }
        else
        {
            Sprite.Stop();
        }
        UpdateLook();
        _body.QueueRedraw();
    }

    /// <summary>每帧：朝向翻转、状态颜色、闪白。</summary>
    private void UpdateLook()
    {
        if (_body == null || !IsInstanceValid(_body) || Sprite == null || !IsInstanceValid(Sprite)) return;
        Sprite.FlipH = FlipFor(_body.Facing);
        Sprite.Modulate = _body.StateTint(Colors.White);
        FlashMaterial?.SetShaderParameter("flash", _body.FlashAmount);
    }

    /// <summary>闪白着色器所有敌人共用一份；静态字段持有的 Godot 对象要在退出前放掉（见 ShutdownHooks）。</summary>
    private static Shader FlashShader()
    {
        if (_flashShader != null) return _flashShader;
        _flashShader = new Shader { Code = FlashShaderCode };
        ShutdownHooks.Register(ReleaseShared);
        return _flashShader;
    }

    private static void ReleaseShared() => _flashShader = null;

    // ---------- 共用（编辑器里的 EnemySprite 也用） ----------

    /// <summary>敌人的图画朝左，所以朝右（facing &gt; 0）时要水平翻转。</summary>
    public static bool FlipFor(float facing) => facing > 0f;

    /// <summary>源图一帧的高度（像素），用来算缩放；取这个动作第 1 帧的贴图高度。</summary>
    public static int FrameHeight(SpriteFrames frames, string animation)
    {
        if (frames == null || !frames.HasAnimation(animation) || frames.GetFrameCount(animation) == 0) return 0;
        Texture2D texture = frames.GetFrameTexture(animation, 0);
        if (texture is AtlasTexture atlas) return (int)atlas.Region.Size.Y;
        return texture?.GetHeight() ?? 0;
    }

    /// <summary>缩放到 canvas、帧底边中点放在原点（脚底）。</summary>
    public static void Layout(AnimatedSprite2D sprite, int frameHeight, float canvas)
    {
        if (frameHeight <= 0) return;
        float scale = canvas / frameHeight;
        sprite.Centered = true;
        sprite.Scale = new Vector2(scale, scale);
        sprite.Offset = new Vector2(0f, -frameHeight * 0.5f);
        sprite.Position = Vector2.Zero;
    }

    /// <summary>按命名为 kind 生成 SpriteFrames：actions 里有图的动作才加；没有任何图时返回 null。</summary>
    public static SpriteFrames BuildFrames(string kind, string directory, float canvas, IEnumerable<string> actions)
    {
        if (string.IsNullOrEmpty(kind)) return null;
        var frames = new SpriteFrames();
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        int standard = Mathf.RoundToInt(canvas * SourceScale);
        bool any = false;
        foreach (string action in actions)
        {
            Texture2D art = SpriteStrip.Load(directory, $"spr_enemy_{kind}_{action}.png");
            if (art == null) continue;
            bool loop = Actions.TryGetValue(action, out bool l) && l;
            if (!SpriteStrip.AddStrip(frames, action, art, loop, BaseFps, "敌人美术图")) continue;
            any = true;
            if (art.GetHeight() != standard && SizeWarned.Add(art.ResourcePath))
                GD.PushWarning($"敌人美术图 {art.ResourcePath}：每帧高 {art.GetHeight()}，标准是 {standard}×{standard}（照样缩放到 {canvas} 显示）。");
        }
        return any ? frames : null;
    }
}
