using Godot;
using SonOfMud.Core;

namespace SonOfMud.Player;

/// <summary>
/// 玩家的交互组件（D14），是 player_v2.tscn 的子节点。检测交互层（Layers.Interact）上重叠的区域，
/// 从实现了 IInteractable 且 CanInteract 为 true 的对象里挑出离玩家最近的一个：
/// - 最近的对象换了、或它的提示文字变了 → 发 Events.InteractPromptChanged（空字符串表示附近没有）；
/// - 按下交互键（E）时调用它的 Interact(玩家)。输入锁期间读不到按键；
///   攻击、闪避、格挡、受伤、死亡等状态（PlayerState.AllowsInteract 为 false）不触发。
/// 过门、存档点、NPC、泥板都走这一个入口。
/// </summary>
[GlobalClass]
public partial class PlayerInteractor : Area2D
{
    public PlayerCharacter Player { get; private set; }
    /// <summary>当前最近的可交互对象；没有时为 null。</summary>
    public IInteractable Current { get; private set; }
    /// <summary>最近一次通过 InteractPromptChanged 发出的提示文字。</summary>
    public string CurrentPrompt { get; private set; } = "";

    public override void _Ready()
    {
        Player = GetParent() as PlayerCharacter;
        if (Player == null) GD.PushError("PlayerInteractor 必须是 PlayerCharacter 的子节点。");
        CollisionLayer = 0;
        CollisionMask = Layers.Interact;
        Monitoring = true;
        Monitorable = false;
    }

    public override void _ExitTree() => SetCurrent(null);

    public override void _PhysicsProcess(double delta)
    {
        SetCurrent(FindNearest());
        if (Current == null || Player == null) return;
        if (!Player.JustPressed(PlayerActions.Interact)) return;
        if (Player.State is not { AllowsInteract: true }) return;
        Current.Interact(Player);
    }

    /// <summary>重叠的交互区域里，CanInteract 为 true 且离玩家最近的一个；距离相同时取实例 ID 小的，保证结果稳定。</summary>
    public IInteractable FindNearest()
    {
        if (Player == null || !Monitoring) return null;
        IInteractable best = null;
        float bestDistance = float.MaxValue;
        ulong bestId = ulong.MaxValue;
        Vector2 from = Player.GlobalPosition;
        foreach (Area2D area in GetOverlappingAreas())
        {
            if (area is not IInteractable candidate || !IsInstanceValid(area) || !candidate.CanInteract(Player)) continue;
            float distance = from.DistanceSquaredTo(area.GlobalPosition);
            ulong id = area.GetInstanceId();
            if (distance < bestDistance || (Mathf.IsEqualApprox(distance, bestDistance) && id < bestId))
            {
                best = candidate;
                bestDistance = distance;
                bestId = id;
            }
        }
        return best;
    }

    private void SetCurrent(IInteractable target)
    {
        string prompt = target?.Prompt ?? "";
        bool changed = !ReferenceEquals(target, Current) || prompt != CurrentPrompt;
        Current = target;
        CurrentPrompt = prompt;
        if (changed) Events.Instance?.EmitSignal(Events.SignalName.InteractPromptChanged, prompt);
    }
}
