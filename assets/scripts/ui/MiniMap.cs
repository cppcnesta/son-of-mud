using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// HUD 右上角的小地图：大地图的缩略图（同一份 map_pieces.tscn 缩小显示），只显示去过的碎片和「你在这里」。
/// 进房间（RoomEntered，带房间编号）时刷新；新游戏 / 读档 / 复活都会发 RoomEntered。
/// </summary>
public partial class MiniMap : Control
{
    [Export] public MapPieceLayer Layer { get; set; }

    /// <summary>最近一次进入的房间。</summary>
    public string CurrentRoomId { get; private set; } = "";
    public MapPiece HerePiece => Layer?.HerePiece;
    public MapPiece PieceOf(string roomId) => Layer?.PieceOf(roomId);

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.RoomEntered += OnRoomEntered;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.RoomEntered -= OnRoomEntered;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Refresh();
    }

    private void OnRoomEntered(string roomId)
    {
        CurrentRoomId = roomId ?? "";
        Refresh();
    }

    public void Refresh() => Layer?.Refresh(CurrentRoomId, "");
}
