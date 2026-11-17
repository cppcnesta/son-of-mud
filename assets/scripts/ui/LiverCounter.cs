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
        ShowIconArt();
        Refresh();
    }

    /// <summary>图标换成泥肝图：在占位色块里铺一张图（保持比例），色块变透明；没有图时照旧是色块。</summary>
    private void ShowIconArt()
    {
        Texture2D art = ArtSprite.Load(ArtSprite.ItemDirectory, "spr_item_mud_liver");
        if (art == null || Icon == null || Icon.GetNodeOrNull("Art") != null) return;
        if (Icon is ColorRect rect) rect.Color = new Color(rect.Color, 0f);
        var texture = new TextureRect
        {
            Name = "Art",
            Texture = art,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        texture.SetAnchorsPreset(LayoutPreset.FullRect);
        Icon.AddChild(texture);
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
