using System.Collections.Generic;
using Godot;

namespace SonOfMud.Combat;

/// <summary>
/// 攻击去重记录（规格第 5 节）：按 (AttackId, 实体实例 ID) 判断，同一实体的多个 HurtBox 共用一份。
/// 每个实体一份，挂在实体根节点的 meta 上，实体释放时一起释放。
/// 只保留最近 Capacity 条，防止长局无限增长。
/// </summary>
public partial class AttackLedger : RefCounted
{
    public const int Capacity = 64;
    private static readonly StringName MetaKey = "_attack_ledger";

    private readonly HashSet<(string, ulong)> _seen = new();
    private readonly Queue<(string, ulong)> _order = new();

    public int Count => _seen.Count;

    /// <summary>取实体根节点上的共享记录，没有就新建。</summary>
    public static AttackLedger For(Node entity)
    {
        if (entity == null) return new AttackLedger();
        if (entity.HasMeta(MetaKey) && entity.GetMeta(MetaKey).AsGodotObject() is AttackLedger existing) return existing;
        var ledger = new AttackLedger();
        entity.SetMeta(MetaKey, ledger);
        return ledger;
    }

    /// <summary>第一次见到这次攻击返回 true 并记录；重复或 ID 为空返回 false。</summary>
    public bool Accept(string attackId, ulong entityId)
    {
        if (string.IsNullOrEmpty(attackId)) return false;
        var key = (attackId, entityId);
        if (!_seen.Add(key)) return false;
        _order.Enqueue(key);
        while (_order.Count > Capacity) _seen.Remove(_order.Dequeue());
        return true;
    }

    public void Clear()
    {
        _seen.Clear();
        _order.Clear();
    }
}
