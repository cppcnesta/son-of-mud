using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 泥肝数量（放在泥珠下面）：图标（先用色块占位）+「×N」。数量是 0 时整个隐藏。
/// 只读进度变量 count.泥肝（Core.ItemCounts），不引用玩家：
/// - 收到 VariableChanged 且是这个变量 → 立即更新（拾取、投掷都会发）；
/// - 新游戏 / 读档 / 重开时进度是整份替换的，不会逐个发 VariableChanged，所以在 RoomEntered、GameStartFinished、
///   PlayerSpawned 时也重新读一次。
/// </summary>
public partial class LiverCounter : HBoxContainer
{
    [Export] public StringName ItemId { get; set; } = "泥肝";
    [Export] public Control Icon { get; set; }
    [Export] public Label CountLabel { get; set; }

    public long Count { get; private set; }
    public string Text => CountLabel?.Text ?? "";

    public override void _EnterTree()
    {
        if (Events.Instance is not { } events) return;
        events.VariableChanged += OnVariableChanged;
        events.RoomEntered += OnRoomEntered;
        events.GameStartFinished += OnGameStartFinished;
        events.PlayerSpawned += OnPlayerSpawned;
    }

    public override void _ExitTree()
    {
        if (Events.Instance is not { } events) return;
        events.VariableChanged -= OnVariableChanged;
        events.RoomEntered -= OnRoomEntered;
        events.GameStartFinished -= OnGameStartFinished;
        events.PlayerSpawned -= OnPlayerSpawned;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Refresh();
    }

    private void OnVariableChanged(StringName key, Variant value)
    {
        if (ItemCounts.IsKeyOf(key, ItemId)) Refresh();
    }

    private void OnRoomEntered(string roomId) => Refresh();
    private void OnGameStartFinished(bool success, string error) => Refresh();
    private void OnPlayerSpawned(Node2D player) => Refresh();

    /// <summary>按当前进度更新数量和显隐。</summary>
    public void Refresh()
    {
        Count = ItemCounts.Get(ItemId);
        if (CountLabel != null) CountLabel.Text = $"×{Count}";
        Visible = Count > 0;
    }
}
