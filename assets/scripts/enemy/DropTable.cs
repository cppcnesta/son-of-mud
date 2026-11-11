using Godot;

namespace SonOfMud.Enemy;

/// <summary>
/// 掉落表：掉什么、概率多少（祈祷泥人掉泥瓶：泥坑 30%、占卜之城 100%；虔诚泥人掉泥肝 30%）。
/// 敌人死亡时 Roll 一次，掉落通过 Events.ItemDropped 交给 World 生成拾取物。
/// </summary>
[GlobalClass]
public partial class DropTable : Resource
{
    /// <summary>物品编号（泥瓶、泥肝……），World 按它找拾取物的定义。</summary>
    [Export] public StringName ItemId { get; set; } = "";
    /// <summary>0–1，1 表示一定掉。</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Chance { get; set; } = 1f;

    /// <summary>用给定的随机数发生器掷一次（消耗一个 Randf）。没有物品时不掷，直接返回 false。</summary>
    public bool Roll(RandomNumberGenerator rng)
    {
        if (string.IsNullOrEmpty(ItemId.ToString()) || Chance <= 0f) return false;
        float value = rng?.Randf() ?? GD.Randf();
        return Chance >= 1f || value < Chance;
    }
}
