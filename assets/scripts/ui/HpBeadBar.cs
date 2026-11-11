using System.Collections.Generic;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 泥珠血条（D10）。收到 PlayerHealthChanged(hp, max)：
/// 珠子数 = ⌊max / 基准⌋（至少 1 颗；基准是 UiSettings.BeadHp，默认 20），每颗容量 = max / 珠子数，
/// 第 i 颗装 [i×容量, (i+1)×容量) 这一段血，按比例填充。上限 70 → 3 颗、每颗约 23.3；上限 140 → 7 颗、每颗 20。
/// 满血时每颗都满，掉血时从最后一颗往前空。珠子都是复制场景里的模板 Template 得到的，C# 不引用贴图资源。
/// </summary>
public partial class HpBeadBar : HBoxContainer
{
    /// <summary>可调数值；不导出，统一用 UiSettings.Shared（原因见那里）。测试可以替换。</summary>
    public UiSettings Settings { get; set; }
    /// <summary>场景里摆好的模板珠子（隐藏）：贴图、填充方向、碎裂子节点都在它上面设置，新珠子复制它。</summary>
    [Export] public HpBead Template { get; set; }

    public int Health { get; private set; }
    public int MaxHealth { get; private set; }
    public int BeadCount => _beads.Count;
    public HpBead GetBead(int index) => _beads[index];

    private readonly List<HpBead> _beads = new();
    private bool _hasValue;

    /// <summary>珠子数 = ⌊max / beadHp⌋，至少 1 颗；max ≤ 0 时 0 颗。</summary>
    public static int BeadCountFor(int maxHealth, int beadHp) =>
        maxHealth <= 0 ? 0 : Mathf.Max(1, maxHealth / Mathf.Max(1, beadHp));

    /// <summary>每颗珠子的容量 = max / 珠子数（可以不是整数）。</summary>
    public static float BeadCapacityFor(int maxHealth, int count) =>
        count <= 0 ? 0f : (float)maxHealth / count;

    /// <summary>
    /// 第 index 颗珠子装的血量 = clamp(hp − index×容量, 0, 容量)。先按整数算 clamp(hp×count − index×max, 0, max) 再除以 count，
    /// 满血时和容量的算法完全一样（不会因为浮点误差差一点点不满），掉到下一颗的边界时也正好是 0。
    /// </summary>
    public static float BeadAmountFor(int health, int maxHealth, int count, int index)
    {
        if (count <= 0 || maxHealth <= 0) return 0f;
        long units = (long)health * count - (long)index * maxHealth;
        return Mathf.Clamp(units, 0, maxHealth) / (float)count;
    }

    public override void _EnterTree()
    {
        Settings ??= UiSettings.Shared;
        if (Events.Instance != null) Events.Instance.PlayerHealthChanged += OnPlayerHealthChanged;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.PlayerHealthChanged -= OnPlayerHealthChanged;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        if (Template != null) Template.Visible = false;
        else GD.PushError("HpBeadBar：没有设置模板珠子 Template。");
        AddThemeConstantOverride("separation", Settings.BeadSpacing);
    }

    private void OnPlayerHealthChanged(int current, int max) => Apply(current, max);

    /// <summary>按血量和最大血量更新珠子。最大血量变了就重建（不播动画），否则液面渐变、掉空的珠子碎裂。</summary>
    public void Apply(int health, int maxHealth)
    {
        maxHealth = Mathf.Max(0, maxHealth);
        health = Mathf.Clamp(health, 0, maxHealth);
        int count = BeadCountFor(maxHealth, Settings.BeadHp);
        bool rebuild = count != _beads.Count || maxHealth != MaxHealth;
        bool animate = _hasValue && !rebuild;
        if (rebuild) Rebuild(count, maxHealth);
        for (int i = 0; i < _beads.Count; i++)
            _beads[i].SetAmount(BeadAmountFor(health, maxHealth, count, i), animate, Settings.BeadDrainSeconds, Settings.BeadBreakSeconds);
        Health = health;
        MaxHealth = maxHealth;
        _hasValue = true;
    }

    private void Rebuild(int count, int maxHealth)
    {
        float capacity = BeadCapacityFor(maxHealth, count);
        foreach (HpBead bead in _beads)
        {
            RemoveChild(bead);
            bead.QueueFree();
        }
        _beads.Clear();
        for (int i = 0; i < count; i++)
        {
            var bead = Template?.Duplicate() as HpBead ?? new HpBead();
            bead.Name = $"Bead{i}";
            bead.Visible = true;
            bead.Setup(capacity, Settings.BeadSize);
            AddChild(bead);
            _beads.Add(bead);
        }
    }
}
