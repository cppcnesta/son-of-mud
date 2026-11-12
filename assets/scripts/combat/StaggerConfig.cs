using Godot;
using SonOfMud.Core;

namespace SonOfMud.Combat;

/// <summary>反制僵直的参数：持续时间、易伤倍率、每受一次伤减少的剩余时间（boss.md）。</summary>
[GlobalClass]
public partial class StaggerConfig : KeptResource
{
    [Export] public float Duration { get; set; } = 5f;
    [Export] public float DamageMultiplier { get; set; } = 2f;
    [Export] public float ReducePerHit { get; set; } = 1f;
}
