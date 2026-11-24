using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 大地图界面（按 M）。打开时 PauseService.Request(Map, true)，关闭时释放；ProcessMode = Always。开关的时机由 GameUi 决定（互斥规则在那里）。
/// 画面在 map_screen.tscn：Bg（半透明底，颜色在场景里调）、Border（边框）、Pieces（泥板碎片，每块是一个 <see cref="MapPiece"/>）、
/// Markers（Here「你在这里」、Death「上次倒下的地方」）、Legend（图例）。整张地图一屏放得下，不能平移。
/// 每次打开时刷新：
/// - 去过的房间（MetSys 里这个房间有任何一格已探索；房间占哪些格从 RoomCatalog 查）对应的碎片才显示；碎片上还有没读的泥板时显示泥板图标。
/// - Here 放在玩家所在房间的碎片上；Death 放在上次倒下的房间的碎片上（倒下时记进进度变量 <see cref="DeathRoomKey"/>，跟着存档走）。
/// </summary>
public partial class MapScreen : Control
{
    /// <summary>上次倒下的房间编号（进度变量，字符串）。</summary>
    public const string DeathRoomKey = "map.death_room";

    [Export] public Control Here { get; set; }
    [Export] public Control Death { get; set; }

    public bool IsOpen { get; private set; }
    /// <summary>最近一次打开时玩家所在的房间（找不到时是空字符串）。</summary>
    public string CurrentRoomId { get; private set; } = "";
    public IReadOnlyList<MapPiece> Pieces => _pieces;

    private readonly List<MapPiece> _pieces = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        _pieces.Clear();
        if (GetNodeOrNull("Pieces") is { } parent) _pieces.AddRange(parent.GetChildren().OfType<MapPiece>());
    }

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.PlayerDied += OnPlayerDied;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.PlayerDied -= OnPlayerDied;
        // 场景被直接释放（没经过 GameBoot）时也不留下地图的暂停来源
        if (IsOpen) PauseService.Request(PauseService.Map, false);
        IsOpen = false;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        PauseService.Request(PauseService.Map, true);
        Visible = true;
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        PauseService.Request(PauseService.Map, false);
    }

    /// <summary>按当前进度刷新碎片、泥板图标和两个标记。</summary>
    public void Refresh()
    {
        CurrentRoomId = RoomAtPlayer();
        ProgressStore progress = GameSession.Progress;
        foreach (MapPiece piece in _pieces)
        {
            piece.Visible = piece.Rooms.Any(IsVisited);
            if (piece.GetNodeOrNull<CanvasItem>("Tablet") is { } tablet)
                tablet.Visible = piece.TabletIds.Any(id => !progress.Flag("tablet." + id));
        }
        HerePiece = Place(Here, PieceOf(CurrentRoomId), Vector2.Zero, pinBottom: true);
        DeathPiece = Place(Death, PieceOf(DeathRoom), new Vector2(10f, 4f), pinBottom: false);
    }

    /// <summary>「你在这里」/「上次倒下的地方」现在放在哪块碎片上（没显示时是 null）。</summary>
    public MapPiece HerePiece { get; private set; }
    public MapPiece DeathPiece { get; private set; }

    /// <summary>上次倒下的房间（没倒下过是空字符串）。</summary>
    public string DeathRoom => GameSession.Progress.GetVar(DeathRoomKey, "").AsString();

    public MapPiece PieceOf(string roomId) => _pieces.FirstOrDefault(p => p.Has(roomId));

    /// <summary>这块碎片现在是否显示（测试用）。</summary>
    public bool IsShown(string roomId) => PieceOf(roomId) is { Visible: true };

    /// <summary>去过：玩家现在所在的房间，或者 MetSys 里这个房间有任何一格已探索（只是“已知”的不算）。</summary>
    public bool IsVisited(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return false;
        if (roomId == CurrentRoomId) return true;
        if (!MetSysApi.Available || !MetSysApi.HasSaveData) return false;
        return RoomCatalog.CellsOf(roomId).Any(cell => MetSysApi.IsCellDiscovered(cell, false));
    }

    private void OnPlayerDied()
    {
        string room = RoomAtPlayer();
        if (room != "") GameSession.Progress.SetVar(DeathRoomKey, room);
    }

    /// <summary>玩家现在所在的房间：按 MetSys 的当前格在房间表（RoomCatalog）里找。</summary>
    private static string RoomAtPlayer() =>
        MetSysApi.Available && MetSysApi.HasPlayerPosition ? RoomCatalog.RoomAt(MetSysApi.CurrentCoords) : "";

    /// <summary>把标记放到碎片中心；pinBottom 时标记的下尖对准中心（「你在这里」的图钉）。碎片没显示时标记也不显示。</summary>
    private static MapPiece Place(Control marker, MapPiece piece, Vector2 offset, bool pinBottom)
    {
        if (piece is not { Visible: true }) piece = null;
        if (marker == null) return piece;
        marker.Visible = piece != null;
        if (piece == null) return null;
        Vector2 anchor = pinBottom ? new Vector2(marker.Size.X * 0.5f, marker.Size.Y) : marker.Size * 0.5f;
        marker.Position = piece.Center + offset - anchor;
        return piece;
    }
}
