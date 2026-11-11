using Godot;

namespace SonOfMud.Enemy;

/// <summary>
/// 掉落表：掉什么、概率多少（Q9：朝圣者小泥偶 30%，跪拜泥人 100%；工人石头）。
/// 敌人死亡时 Roll 一次，掉落通过 Events.ItemDropped 交给 World 生成拾取物。
/// </summary>
[GlobalClass]
public partial class DropTable : Resource
{
    /// <summary>物品编号，也是进度变量 count.&lt;编号&gt; 和拾取台词标签的名字。</summary>
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
