using Godot;

namespace SonOfMud.World;

/// <summary>
/// 一种拾取物的定义（assets/data/world/items/*.tres）。DropSpawner 按物品编号找到它，交给生成的 Pickup。
/// - HealAmount &gt; 0：回血道具（泥瓶）。碰到立刻给玩家回血；玩家满血时不捡，留在地上。
/// - Counted：拾取时 count.&lt;编号&gt; 加 1；MaxCount &gt; 0 时数到上限就不捡，留在地上（泥肝最多 5 个）。
/// - PlaysLine：第一次拾取时请求 pickups.orph 里同名标签的拾取台词（泥瓶、泥肝暂时没有）。
/// </summary>
[GlobalClass]
public partial class PickupItem : Resource
{
    [Export] public StringName ItemId { get; set; } = "";
    [Export] public int HealAmount { get; set; }
    [Export] public bool Counted { get; set; } = true;
    /// <summary>计数上限；0 表示不限。</summary>
    [Export] public int MaxCount { get; set; }
    [Export] public bool PlaysLine { get; set; }
    [Export] public Color Tint { get; set; } = new(0.72f, 0.55f, 0.36f);
}
