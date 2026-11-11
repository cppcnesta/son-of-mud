using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 房间清单里的一个出口。
/// Edge：Side + Offset 指出通道所在的格子（Offset 是沿这一边的第几格，左右两边按行数、上下两边按列数，从 0 开始）。
/// Door：Side / Offset 只决定门在房间里的大致位置，不写进 MetSys 通道。
/// Requirement：只有变量（Flags）时生成 GateBarrier 实体门；含能力（Abilities）的门靠地形高度挡住，生成器不放实体门。
/// </summary>
[GlobalClass]
public partial class ExitDef : Resource
{
    [Export] public ExitSide Side { get; set; } = ExitSide.Right;
    [Export] public int Offset { get; set; }
    [Export] public string Target { get; set; } = "";
    [Export] public ExitKind Kind { get; set; } = ExitKind.Edge;
    [Export] public GateRequirement Requirement { get; set; }

    /// <summary>从这个出口到达目标房间时，在目标房间里使用的出生点（目标房间里名为 From_本房间 的 SpawnPoint）。</summary>
    public static StringName ArrivalSpawn(string fromRoomId) => "From_" + fromRoomId;

    public bool HasBarrier => Requirement != null && Requirement.Flags.Count > 0 && Requirement.Abilities.Count == 0;
}
