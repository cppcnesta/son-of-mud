using System.Collections.Generic;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 大地图界面（按 M）。打开时 PauseService.Request(Map, true)，关闭时释放；ProcessMode = Always。开关的时机由 GameUi 决定（互斥规则在那里）。
/// 画面在 map_screen.tscn：Bg（半透明底，颜色在场景里调）、Border（边框）、Pieces（泥板碎片，map_pieces.tscn，见 <see cref="MapPieceLayer"/>）、
/// Legend（图例）。整张地图一屏放得下，不能平移。
/// 每次打开时刷新碎片：「你在这里」放在玩家所在房间的碎片上；「上次倒下的地方」放在上次倒下的房间的碎片上
/// （倒下时记进进度变量 <see cref="DeathRoomKey"/>，跟着存档走）。
/// </summary>
public partial class MapScreen : Control
{
    /// <summary>上次倒下的房间编号（进度变量，字符串）。</summary>
    public const string DeathRoomKey = "map.death_room";

    [Export] public MapPieceLayer Layer { get; set; }

    public bool IsOpen { get; private set; }
    /// <summary>最近一次打开时玩家所在的房间（找不到时是空字符串）。</summary>
    public string CurrentRoomId { get; private set; } = "";
    /// <summary>上次倒下的房间（没倒下过是空字符串）。</summary>
    public string DeathRoom => GameSession.Progress.GetVar(DeathRoomKey, "").AsString();

    public IReadOnlyList<MapPiece> Pieces => Layer?.Pieces ?? System.Array.Empty<MapPiece>();
    public MapPiece PieceOf(string roomId) => Layer?.PieceOf(roomId);
    public Control Here => Layer?.Here;
    public Control Death => Layer?.Death;
    public MapPiece HerePiece => Layer?.HerePiece;
    public MapPiece DeathPiece => Layer?.DeathPiece;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
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
        Layer?.Refresh(CurrentRoomId, DeathRoom);
    }

    private void OnPlayerDied()
    {
        string room = RoomAtPlayer();
        if (room != "") GameSession.Progress.SetVar(DeathRoomKey, room);
    }

    /// <summary>玩家现在所在的房间：按 MetSys 的当前格在房间表（RoomCatalog）里找。</summary>
    public static string RoomAtPlayer() =>
        MetSysApi.Available && MetSysApi.HasPlayerPosition ? RoomCatalog.RoomAt(MetSysApi.CurrentCoords) : "";
}
