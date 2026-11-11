using System;
using System.Linq;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间场景的根节点。结构：RoomInstance（MetSys，位置 0,0）、Geometry、Spawns、Objects。
/// 进场时自动生成背景（<see cref="RoomBackground"/>，内部子节点，不存进场景文件）；编辑器里打开房间场景也会生成，用来预览。
/// 房间里手动放了名为 Background 的节点时，不再自动生成。
/// </summary>
[Tool]
[GlobalClass]
public partial class RoomRoot : Node2D
{
    [Export] public string RoomId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    /// <summary>自动生成的背景；房间里手动放了别的 Background 节点、或还没生成时为 null。</summary>
    public RoomBackground Background => GetNodeOrNull<RoomBackground>(RoomBackground.NodeName);

    /// <summary>MetSys 编辑器相邻房间预览的标记（RoomInstance.gd 里 set_meta(&amp;"fake_map", true)）。</summary>
    public static readonly StringName MetSysPreviewMeta = "fake_map";

    public override void _Ready()
    {
        // MetSys 在编辑器里会把相邻房间实例化成半透明预览（带 fake_map 标记），预览不生成背景，免得盖住正在编辑的房间
        if (string.IsNullOrEmpty(RoomId) || HasMeta(MetSysPreviewMeta) || GetNodeOrNull(RoomBackground.NodeName) != null) return;
        var background = new RoomBackground { Name = RoomBackground.NodeName };
        AddChild(background, false, InternalMode.Front);
        background.Build(RoomId);
        if (Engine.IsEditorHint()) UpdateConfigurationWarnings();
    }

    /// <summary>编辑器场景树里的警告图标：背景图不够大被放大 / 平铺、房间清单里找不到这个房间。</summary>
    public override string[] _GetConfigurationWarnings() => Background?.Warnings.ToArray() ?? Array.Empty<string>();

    /// <summary>按 SpawnId 找出生点；找不到时退回 Default，再找不到返回 null。</summary>
    public SpawnPoint FindSpawn(StringName spawnId)
    {
        SpawnPoint exact = Search(this, spawnId);
        if (exact != null) return exact;
        SpawnPoint fallback = Search(this, SpawnPoint.DefaultId);
        GD.PushWarning($"房间 {RoomId} 没有出生点 {spawnId}，改用 Default。");
        return fallback;
    }

    private static SpawnPoint Search(Node node, StringName spawnId)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is SpawnPoint spawn && spawn.SpawnId == spawnId) return spawn;
            SpawnPoint found = Search(child, spawnId);
            if (found != null) return found;
        }
        return null;
    }
}
