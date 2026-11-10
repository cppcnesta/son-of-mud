using System.Collections.Generic;
using Godot;

namespace SonOfMud.Combat;

/// <summary>
/// 攻击框。Area2D 只用来在编辑器里摆形状；激活期间每个物理帧用自身的形状做 IntersectShape 立即查询
/// （CollideWithAreas=true、CollideWithBodies=false，按 CollisionMask 过滤），找到 HurtBox 就结算。
/// 一次激活内，同一个实体只处理一次；跨 HitBox 的重复由受击方的 AttackLedger 判定。
/// 何时收招由攻击状态自己的计时器决定，HitBox 只负责「开着的时候打到谁」。
/// </summary>
[GlobalClass]
public partial class HitBox : Area2D
{
    [Signal] public delegate void HitLandedEventHandler(HurtBox target, Hit hit, int outcome);

    public bool Active { get; private set; }
    public Hit Current { get; private set; }

    private readonly HashSet<ulong> _processed = new();
    private readonly List<CollisionShape2D> _shapes = new();

    public override void _Ready()
    {
        Monitoring = false;
        Monitorable = false;
        foreach (Node child in GetChildren())
            if (child is CollisionShape2D shape) _shapes.Add(shape);
    }

    /// <summary>开启判定并立即查询一次（同一帧生效，沿用白盒 Strike 的做法）。</summary>
    public void Activate(Hit hit)
    {
        Current = hit;
        Active = true;
        _processed.Clear();
        Scan();
    }

    public void Deactivate()
    {
        Active = false;
        Current = null;
        _processed.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active) Scan();
    }

    /// <summary>用自身所有形状查询一次。返回本次新结算的目标数。</summary>
    public int Scan()
    {
        if (!Active || Current == null || !IsInsideTree()) return 0;
        int count = 0;
        PhysicsDirectSpaceState2D space = GetWorld2D().DirectSpaceState;
        foreach (CollisionShape2D shapeNode in _shapes)
        {
            if (shapeNode.Disabled || shapeNode.Shape == null) continue;
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = shapeNode.Shape,
                Transform = shapeNode.GlobalTransform,
                CollisionMask = CollisionMask,
                CollideWithAreas = true,
                CollideWithBodies = false,
            };
            foreach (var result in space.IntersectShape(query, 32))
            {
                if (!Active) return count;
                if (result["collider"].AsGodotObject() is not HurtBox target) continue;
                if (!_processed.Add(target.EntityId)) continue;
                HitOutcome outcome = target.Receive(Current);
                count++;
                EmitSignal(SignalName.HitLanded, target, Current, (int)outcome);
            }
        }
        return count;
    }
}
