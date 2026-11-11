using System.Collections.Generic;
using Godot;

namespace SonOfMud.Combat;

/// <summary>
/// 血量组件。伤害一律是整数（规格第 5 节）。
/// IncomingMultiplier 是受击方的易伤倍率（例如反制僵直 ×2），多个来源时取最大值。
/// </summary>
[GlobalClass]
public partial class Stats : Node
{
    [Signal] public delegate void HealthChangedEventHandler(int oldHealth, int newHealth);
    [Signal] public delegate void DepletedEventHandler();

    [Export] public int MaxHealth { get; set; } = 100;

    private int _health;
    private bool _initialized;
    private readonly Dictionary<StringName, float> _incoming = new();

    public int Health
    {
        get { EnsureInitialized(); return _health; }
    }

    public bool IsDepleted => Health <= 0;

    public float IncomingMultiplier
    {
        get
        {
            float best = 1f;
            foreach (float m in _incoming.Values) if (m > best) best = m;
            return best;
        }
    }

    public override void _Ready() => EnsureInitialized();

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        _health = MaxHealth;
    }

    public void SetIncomingMultiplier(StringName source, float multiplier)
    {
        if (multiplier <= 1f) _incoming.Remove(source);
        else _incoming[source] = multiplier;
    }

    /// <summary>最近一次 TakeDamage 是否把血量扣到了 0（在发信号之前确定）。</summary>
    public bool LastHitDepleted { get; private set; }

    /// <summary>扣血，返回实际扣掉的量。</summary>
    public int TakeDamage(int amount)
    {
        EnsureInitialized();
        if (amount <= 0 || _health <= 0) return 0;
        int old = _health;
        _health = Mathf.Max(0, _health - amount);
        // 先算好结果再发信号：信号的接收方即使改了血量，也不影响这次结算的返回值。
        int dealt = old - _health;
        LastHitDepleted = _health == 0;
        EmitSignal(SignalName.HealthChanged, old, _health);
        if (LastHitDepleted) EmitSignal(SignalName.Depleted);
        return dealt;
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        SetHealth(Health + amount);
    }

    /// <summary>直接设定血量（复活回满血、测试用）。</summary>
    public void SetHealth(int value)
    {
        EnsureInitialized();
        int old = _health;
        _health = Mathf.Clamp(value, 0, MaxHealth);
        if (old != _health) EmitSignal(SignalName.HealthChanged, old, _health);
    }

    public void Refill() => SetHealth(MaxHealth);
}
