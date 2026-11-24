using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 泥板碎片地图（map_pieces.tscn），大地图和 HUD 小地图共用：每块碎片是一个 <see cref="MapPiece"/>，
/// 另有 Here（你在这里）、Death（上次倒下的地方）两个标记。
/// <see cref="Refresh"/>：去过的房间对应的碎片才显示（玩家现在所在的房间也算；其余看 MetSys 里这个房间有没有已探索的格，
/// 房间占哪些格从 RoomCatalog 查）；碎片上还有没读的泥板时显示泥板图标；两个标记放到对应房间的碎片中心。
/// ShowIcons=false 时（小地图）不显示存档 / 泥板图标和 Death；MarkerScale 放大标记（小地图整体缩小后标记仍看得清）。
/// </summary>
[GlobalClass]
public partial class MapPieceLayer : Control
{
    [Export] public Control Here { get; set; }
    [Export] public Control Death { get; set; }
    [Export] public bool ShowIcons { get; set; } = true;
    [Export] public float MarkerScale { get; set; } = 1f;

    public IReadOnlyList<MapPiece> Pieces => _pieces;
    public MapPiece HerePiece { get; private set; }
    public MapPiece DeathPiece { get; private set; }

    private readonly List<MapPiece> _pieces = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _pieces.Clear();
        _pieces.AddRange(GetChildren().OfType<MapPiece>());
        foreach (Control marker in new[] { Here, Death })
            if (marker != null) marker.Scale = Vector2.One * MarkerScale;
    }

    public MapPiece PieceOf(string roomId) => _pieces.FirstOrDefault(p => p.Has(roomId));

    public void Refresh(string currentRoom, string deathRoom)
    {
        ProgressStore progress = GameSession.Progress;
        foreach (MapPiece piece in _pieces)
        {
            piece.Visible = piece.Rooms.Any(r => r == currentRoom || IsVisited(r));
            if (piece.GetNodeOrNull<CanvasItem>("Save") is { } save) save.Visible = ShowIcons;
            if (piece.GetNodeOrNull<CanvasItem>("Tablet") is { } tablet)
                tablet.Visible = ShowIcons && piece.TabletIds.Any(id => !progress.Flag("tablet." + id));
        }
        HerePiece = Place(Here, PieceOf(currentRoom), Vector2.Zero, pinBottom: true);
        DeathPiece = ShowIcons ? Place(Death, PieceOf(deathRoom), new Vector2(10f, 4f), pinBottom: false) : Place(Death, null, Vector2.Zero, false);
    }

    /// <summary>去过：MetSys 里这个房间有任何一格已探索（只是“已知”的不算）。</summary>
    public static bool IsVisited(string roomId)
    {
        if (string.IsNullOrEmpty(roomId) || !MetSysApi.Available || !MetSysApi.HasSaveData) return false;
        return RoomCatalog.CellsOf(roomId).Any(cell => MetSysApi.IsCellDiscovered(cell, false));
    }

    /// <summary>把标记放到碎片中心；pinBottom 时标记的下尖对准中心（「你在这里」的图钉）。碎片没显示时标记也不显示。</summary>
    private static MapPiece Place(Control marker, MapPiece piece, Vector2 offset, bool pinBottom)
    {
        if (piece is not { Visible: true }) piece = null;
        if (marker == null) return piece;
        marker.Visible = piece != null;
        if (piece == null) return null;
        Vector2 size = marker.Size * marker.Scale;
        Vector2 anchor = pinBottom ? new Vector2(size.X * 0.5f, size.Y) : size * 0.5f;
        marker.Position = piece.Center + offset - anchor;
        return piece;
    }
}
