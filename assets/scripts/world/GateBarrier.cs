using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 条件门：通道上的阻挡物。条件满足（GameSession.Progress.Meets）就打开：去掉碰撞并隐藏。
/// 收到 VariableChanged / AbilityUnlocked 时立即重新判断。能力门（攀墙）靠地形挡，不用这个类。
/// </summary>
[GlobalClass]
public partial class GateBarrier : StaticBody2D
{
    [Export] public GateRequirement Requirement { get; set; }

    public bool IsOpen { get; private set; }

    public string LockedText => Requirement?.LockedText ?? "";

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.VariableChanged += OnVariableChanged;
        Events.Instance.AbilityUnlocked += OnAbilityUnlocked;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.VariableChanged -= OnVariableChanged;
        Events.Instance.AbilityUnlocked -= OnAbilityUnlocked;
    }

    public override void _Ready() => Refresh();

    private void OnVariableChanged(StringName key, Variant value) => Refresh();
    private void OnAbilityUnlocked(StringName abilityId) => Refresh();

    public void Refresh()
    {
        IsOpen = GameSession.Progress.Meets(Requirement, out _);
        CollisionLayer = IsOpen ? 0u : Layers.World;
        CollisionMask = 0;
        Visible = !IsOpen;
    }
}
