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

    /// <summary>按命名把最远景 / 中景 / 近景 / 物件导入成场景里的普通节点；已经导入过的只补缺的、只换图，不动缩放和位置。可以 Ctrl+Z 撤销。</summary>
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
            BackgroundImporter.Record(this, plan, new EditorSink(undo));
            undo.CommitAction();
            _checkLeft = 0;
            return;
        }
#endif
        BackgroundImporter.Apply(this, plan);
    }

#if TOOLS
    /// <summary>把导入操作交给编辑器的撤销管理器（Ctrl+Z / Ctrl+Y）。</summary>
    private sealed class EditorSink : BackgroundImporter.IUndoSink
    {
        private readonly EditorUndoRedoManager _undo;
        public EditorSink(EditorUndoRedoManager undo) => _undo = undo;
        public void DoMethod(GodotObject target, StringName method, params Variant[] args) => _undo.AddDoMethod(target, method, args);
        public void UndoMethod(GodotObject target, StringName method, params Variant[] args) => _undo.AddUndoMethod(target, method, args);
        public void DoProperty(GodotObject target, StringName property, Variant value) => _undo.AddDoProperty(target, property, value);
        public void UndoProperty(GodotObject target, StringName property, Variant value) => _undo.AddUndoProperty(target, property, value);
        public void DoReference(GodotObject target) => _undo.AddDoReference(target);
    }
#endif

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
