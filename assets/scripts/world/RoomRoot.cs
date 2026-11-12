using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间场景的根节点。结构：RoomInstance（MetSys，位置 0,0）、Geometry、Spawns、Objects，以及（导入后的）Background。
/// 编辑器里：检查器上有「按命名导入背景」按钮（见 <see cref="BackgroundImporter"/>）；背景会在镜头走到房间边上时露底的话，
/// 场景树里这个节点旁会出现警告图标（只提醒，不阻止）。游戏里这个节点不做任何背景相关的事。
/// </summary>
[Tool]
[GlobalClass]
public partial class RoomRoot : Node2D
{
    [Export] public string RoomId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    /// <summary>按命名把最远景 / 中景导入成场景里的普通节点；已经导入过的只换图，不动缩放和位置。可以 Ctrl+Z 撤销。</summary>
    [ExportToolButton("按命名导入背景")]
    public Callable ImportBackgroundButton => Callable.From(ImportBackgroundInEditor);

    private string[] _warnings = Array.Empty<string>();
    private double _checkLeft;

    public override void _Ready()
    {
        SetProcess(Engine.IsEditorHint());
    }

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint()) return;
        _checkLeft -= delta;
        if (_checkLeft > 0) return;
        _checkLeft = 0.5;
        string[] now = BackgroundImporter.CoverageProblems(this, BackgroundSettings.LoadDefault()).ToArray();
        if (now.SequenceEqual(_warnings)) return;
        _warnings = now;
        UpdateConfigurationWarnings();
    }

    public override string[] _GetConfigurationWarnings() => _warnings;

    /// <summary>把 node 及其所有子节点的 owner 设成这个房间，保存场景时才会写进去（编辑器撤销管理器也会调用）。</summary>
    public void AssignOwner(Node node)
    {
        node.Owner = this;
        foreach (Node child in node.GetChildren()) AssignOwner(child);
    }

    private void ImportBackgroundInEditor()
    {
        BackgroundImporter.ImportPlan plan = BackgroundImporter.PlanImport(this, BackgroundSettings.LoadDefault());
        foreach (string message in plan.Messages) GD.Print("[背景] " + message);
        if (plan.Empty) return;
#if TOOLS
        if (Engine.IsEditorHint())
        {
            EditorUndoRedoManager undo = EditorInterface.Singleton.GetEditorUndoRedo();
            undo.CreateAction($"导入背景 {RoomId}", UndoRedo.MergeMode.Disable, this);
            if (plan.NewBackground != null)
            {
                undo.AddDoMethod(this, Node.MethodName.AddChild, plan.NewBackground);
                undo.AddDoMethod(this, Node.MethodName.MoveChild, plan.NewBackground, 0);
                undo.AddDoMethod(this, MethodName.AssignOwner, plan.NewBackground);
                undo.AddDoReference(plan.NewBackground);
                undo.AddUndoMethod(this, Node.MethodName.RemoveChild, plan.NewBackground);
            }
            foreach (Parallax2D layer in plan.NewLayers)
            {
                undo.AddDoMethod(plan.ExistingBackground, Node.MethodName.AddChild, layer);
                undo.AddDoMethod(this, MethodName.AssignOwner, layer);
                undo.AddDoReference(layer);
                undo.AddUndoMethod(plan.ExistingBackground, Node.MethodName.RemoveChild, layer);
            }
            foreach ((Sprite2D image, Texture2D texture) in plan.Retextures)
            {
                undo.AddDoProperty(image, Sprite2D.PropertyName.Texture, texture);
                undo.AddUndoProperty(image, Sprite2D.PropertyName.Texture, image.Texture);
            }
            undo.CommitAction();
            _checkLeft = 0;
            return;
        }
#endif
        BackgroundImporter.Apply(this, plan);
    }

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
