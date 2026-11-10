using System.Collections.Generic;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 七件器物环：7 个 ArtifactSlot 在场景里摆成环。收到 ArtifactsChanged(mask) 时，第 i 位为 1 的槽显示“戴着”，
/// 为 0 的显示“已放下”。只做显示，不挂能力（D6）。
/// </summary>
public partial class ArtifactRing : Control
{
    public const int SlotCount = 7;
    public const int FullMask = (1 << SlotCount) - 1;

    [Export] public UiSettings Settings { get; set; }

    public int Mask { get; private set; } = FullMask;
    public IReadOnlyList<ArtifactSlot> Slots => _slots;

    private readonly List<ArtifactSlot> _slots = new();

    public override void _EnterTree()
    {
        Settings ??= UiSettings.LoadDefault();
        if (Events.Instance != null) Events.Instance.ArtifactsChanged += Apply;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.ArtifactsChanged -= Apply;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _slots.Clear();
        foreach (Node child in GetChildren())
            if (child is ArtifactSlot slot) _slots.Add(slot);
        _slots.Sort((a, b) => a.Index.CompareTo(b.Index));
        if (_slots.Count != SlotCount) GD.PushError($"ArtifactRing：应当有 {SlotCount} 个槽，实际 {_slots.Count} 个。");
        Apply(Settings.InitialArtifactMask);
    }

    public bool IsWorn(int index) => (Mask & (1 << index)) != 0;

    public void Apply(int mask)
    {
        Mask = mask & FullMask;
        foreach (ArtifactSlot slot in _slots) slot.SetWorn(IsWorn(slot.Index));
    }
}
