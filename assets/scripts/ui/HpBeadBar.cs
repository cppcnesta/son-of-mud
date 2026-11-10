using System.Collections.Generic;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 泥珠血条（D10）。收到 PlayerHealthChanged(hp, max)：珠子数 = ⌈max / 20⌉，第 i 颗装 [20i, 20i+20) 这一段血，
/// 按比例填充（130 / 140 = 6 颗满 + 1 颗半）。最大血量不是 20 的倍数时，最后一颗的容量是余数，满血时它也是满的。
/// </summary>
public partial class HpBeadBar : HBoxContainer
{
    [Export] public UiSettings Settings { get; set; }
    [Export] public Texture2D BeadFull { get; set; }
    [Export] public Texture2D BeadEmpty { get; set; }
    [Export] public Texture2D BeadBreak { get; set; }

    public int Health { get; private set; }
    public int MaxHealth { get; private set; }
    public int BeadCount => _beads.Count;
    public HpBead GetBead(int index) => _beads[index];

    private readonly List<HpBead> _beads = new();
    private bool _hasValue;

    public static int BeadCountFor(int maxHealth, int beadHp) =>
        maxHealth <= 0 || beadHp <= 0 ? 0 : (maxHealth + beadHp - 1) / beadHp;

    public override void _EnterTree()
    {
        Settings ??= UiSettings.LoadDefault();
        if (Events.Instance != null) Events.Instance.PlayerHealthChanged += OnPlayerHealthChanged;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.PlayerHealthChanged -= OnPlayerHealthChanged;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", Settings.BeadSpacing);
    }

    private void OnPlayerHealthChanged(int current, int max) => Apply(current, max);

    /// <summary>按血量和最大血量更新珠子。最大血量变了就重建（不播动画），否则液面渐变、掉空的珠子碎裂。</summary>
    public void Apply(int health, int maxHealth)
    {
        maxHealth = Mathf.Max(0, maxHealth);
        health = Mathf.Clamp(health, 0, maxHealth);
        int beadHp = Mathf.Max(1, Settings.BeadHp);
        int count = BeadCountFor(maxHealth, beadHp);
        bool rebuild = count != _beads.Count || maxHealth != MaxHealth;
        bool animate = _hasValue && !rebuild;
        if (rebuild) Rebuild(count, maxHealth, beadHp);
        for (int i = 0; i < _beads.Count; i++)
            _beads[i].SetAmount(health - i * beadHp, animate, Settings.BeadDrainSeconds, Settings.BeadBreakSeconds);
        Health = health;
        MaxHealth = maxHealth;
        _hasValue = true;
    }

    private void Rebuild(int count, int maxHealth, int beadHp)
    {
        foreach (HpBead bead in _beads)
        {
            RemoveChild(bead);
            bead.QueueFree();
        }
        _beads.Clear();
        for (int i = 0; i < count; i++)
        {
            var bead = new HpBead { Name = $"Bead{i}" };
            int capacity = Mathf.Min(beadHp, maxHealth - i * beadHp);
            bead.Setup(capacity, 0, BeadFull, BeadEmpty, BeadBreak, Settings.BeadBreakFrames, Settings.BeadSize);
            AddChild(bead);
            _beads.Add(bead);
        }
    }
}
