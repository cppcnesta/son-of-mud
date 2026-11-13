using Godot;
using SonOfMud.Core;

namespace SonOfMud.Combat;

/// <summary>
/// 一种攻击的打击感（顿帧、震屏、闪白、击退、粒子、音效），挂在 HitSpec.Feel 上，随本次 Hit 一起传给受击方。
/// 只在真正打中（受伤 / 打死）时生效；被格挡、打在无敌期间都不触发。数值都在 .tres 里调。
/// </summary>
[GlobalClass]
public partial class HitFeel : KeptResource
{
    [ExportGroup("顿帧（秒，真实时间）")]
    [Export] public float HitStop { get; set; } = 0.05f;
    [Export] public float KillHitStop { get; set; } = 0.1f;

    [ExportGroup("震屏（像素 / 秒；镜头最多偏 CameraSettings.MaxShakeOffset）")]
    [Export] public float ShakeStrength { get; set; }
    [Export] public float KillShakeStrength { get; set; }
    [Export] public float ShakeDuration { get; set; } = 0.12f;

    [ExportGroup("闪白（秒）")]
    [Export] public float FlashSeconds { get; set; } = 0.08f;

    [ExportGroup("击退（敌人被推开）")]
    /// <summary>初速度（像素 / 秒），沿「攻击来源 → 敌人」的水平方向；0 表示不推。</summary>
    [Export] public float Knockback { get; set; }
    /// <summary>每秒减掉多少速度（像素 / 秒²）。</summary>
    [Export] public float KnockbackDecay { get; set; } = 1500f;

    [ExportGroup("粒子（命中点的泥点）")]
    [Export] public int SparkAmount { get; set; } = 8;
    [Export] public Color SparkColor { get; set; } = new(0.45f, 0.33f, 0.22f);
    [Export] public float SparkSpeed { get; set; } = 360f;

    [ExportGroup("音效（多个随机挑一个，音高随机浮动）")]
    [Export] public Godot.Collections.Array<AudioStream> SwingSounds { get; set; } = new();
    [Export] public Godot.Collections.Array<AudioStream> HitSounds { get; set; } = new();
    [Export] public Godot.Collections.Array<AudioStream> KillSounds { get; set; } = new();

    public float HitStopFor(bool killed) => killed ? KillHitStop : HitStop;
    public float ShakeFor(bool killed) => killed ? KillShakeStrength : ShakeStrength;
    /// <summary>打死时没配打死音效就用打中音效。</summary>
    public Godot.Collections.Array<AudioStream> SoundsFor(bool killed) => killed && KillSounds.Count > 0 ? KillSounds : HitSounds;
}
