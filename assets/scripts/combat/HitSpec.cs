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
}
