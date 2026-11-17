using Godot;
using SonOfMud.Core;

namespace SonOfMud.Combat;

/// <summary>一种攻击的配置（玩家各攻击、下砸，敌人各技能，各一个 .tres）。</summary>
[GlobalClass]
public partial class HitSpec : KeptResource
{
    [Export] public float Damage { get; set; } = 10f;
    /// <summary>重击：只吃重击的物体（脆弱地面等）只认这种攻击。</summary>
    [Export] public bool Heavy { get; set; }
    [Export] public bool Blockable { get; set; } = true;
    /// <summary>没挡住时的眩晕时长，0 表示普通受伤。</summary>
    [Export] public float StunSeconds { get; set; }
    [Export] public float KnockbackForce { get; set; }
    /// <summary>被格挡时攻击方进入反制僵直（蝎尾）。</summary>
    [Export] public bool CounterOnBlock { get; set; }
    [Export] public string[] Tags { get; set; } = System.Array.Empty<string>();
    /// <summary>
    /// 能不能打断敌人出招（战斗策划表 G5）：false 时打中正在出招（前摇 / 判定 / 后摇）的普通敌人只扣血、闪白、击退，
    /// 不进受击反应，招照常出完。水的攻击是 false；泥的攻击、下砸和敌人的攻击是 true。
    /// </summary>
    [Export] public bool InterruptsActions { get; set; } = true;
    /// <summary>打击感（顿帧、震屏、闪白、击退、粒子、音效）；不填就没有。</summary>
    [Export] public HitFeel Feel { get; set; }
}
