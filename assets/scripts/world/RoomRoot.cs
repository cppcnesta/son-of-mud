using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SonOfMud.World;

/// <summary>
/// 房间场景的根节点。结构：RoomInstance（MetSys，位置 0,0）、Geometry、Spawns、Objects，以及（导入后的）Background。
/// 编辑器里：检查器上有「按命名导入背景」按钮（见 <see cref="BackgroundImporter"/>）；背景会在镜头走到房间边上时露底的话，
/// 场景树里这个节点旁会出现警告图标（只提醒，不阻止）。游戏里这个节点不做任何背景相关的事。
/// 检查器里还有「背景视差预览」：按镜头在房间里的位置显示各层的视差效果，并画出这一刻屏幕能看到的范围；
/// 只改编辑器里的显示，保存场景前自动还原，预览开关和位置本身也不存进场景。
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

    private const string PreviewOnProperty = "preview_parallax";
    private const string PreviewXProperty = "preview_camera_x";
    private const string PreviewYProperty = "preview_camera_y";
    private bool _previewOn;
    private float _previewX, _previewY;
    /// <summary>预览改过位置的 Parallax2D 和它们原来的位置（还原用）。</summary>
    private readonly Dictionary<ulong, (Parallax2D node, Vector2 position)> _previewSaved = new();

    public override void _Ready()
    {
        SetProcess(Engine.IsEditorHint());
    }

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint()) return;
        if (_previewOn) ApplyPreview();
        _checkLeft -= delta;
        if (_checkLeft > 0) return;
        _checkLeft = 0.5;
        string[] now = BackgroundImporter.CoverageProblems(this, BackgroundSettings.LoadDefault(), checkNodeMoves: !_previewOn).ToArray();
        if (now.SequenceEqual(_warnings)) return;
        _warnings = now;
        UpdateConfigurationWarnings();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationEditorPreSave || what == NotificationExitTree) RestorePreview();
        else if (what == NotificationEditorPostSave && _previewOn) ApplyPreview();
    }

    // =================================================================
    // 背景视差预览（只在检查器里显示，不存进场景）
    // =================================================================

    public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList() => new()
    {
        new() { ["name"] = "背景视差预览（不存进场景）", ["type"] = (int)Variant.Type.Nil, ["usage"] = (int)PropertyUsageFlags.Group, ["hint_string"] = "preview_" },
        new() { ["name"] = PreviewOnProperty, ["type"] = (int)Variant.Type.Bool, ["usage"] = (int)PropertyUsageFlags.Editor },
        new() { ["name"] = PreviewXProperty, ["type"] = (int)Variant.Type.Float, ["usage"] = (int)PropertyUsageFlags.Editor, ["hint"] = (int)PropertyHint.Range, ["hint_string"] = "0,1,0.01" },
        new() { ["name"] = PreviewYProperty, ["type"] = (int)Variant.Type.Float, ["usage"] = (int)PropertyUsageFlags.Editor, ["hint"] = (int)PropertyHint.Range, ["hint_string"] = "0,1,0.01" },
    };

    public override Variant _Get(StringName property) => property.ToString() switch
    {
        PreviewOnProperty => _previewOn,
        PreviewXProperty => _previewX,
        PreviewYProperty => _previewY,
        _ => default,
    };

    public override bool _Set(StringName property, Variant value)
    {
        switch (property.ToString())
        {
            case PreviewOnProperty: SetPreview(value.AsBool(), _previewX, _previewY); return true;
            case PreviewXProperty: SetPreview(_previewOn, (float)value.AsDouble(), _previewY); return true;
            case PreviewYProperty: SetPreview(_previewOn, _previewX, (float)value.AsDouble()); return true;
            default: return false;
        }
    }

    public bool PreviewOn => _previewOn;

    /// <summary>打开 / 关闭预览。x、y：镜头在房间里的位置，0 = 房间左 / 上边，1 = 右 / 下边。关闭时各层回到原来的位置。</summary>
    public void SetPreview(bool on, float x, float y)
    {
        _previewX = Mathf.Clamp(x, 0f, 1f);
        _previewY = Mathf.Clamp(y, 0f, 1f);
        _previewOn = on;
        if (on) ApplyPreview();
        else RestorePreview();
        QueueRedraw();
    }

    /// <summary>预览时镜头画面左上角在房间里的坐标（镜头范围 = 房间范围）。</summary>
    public Vector2 PreviewCameraTopLeft()
    {
        if (!BackgroundImporter.TryRoomSize(RoomId, out Vector2 room)) return Vector2.Zero;
        Vector2 travel = (room - BackgroundImporter.ProjectScreenSize()).Max(Vector2.Zero);
        return travel * new Vector2(_previewX, _previewY);
    }

    private void ApplyPreview()
    {
        Vector2 c = PreviewCameraTopLeft();
        foreach (Parallax2D parallax in BackgroundImporter.ParallaxNodes(GetNodeOrNull(BackgroundImporter.NodeName)))
        {
            ulong id = parallax.GetInstanceId();
            if (!_previewSaved.ContainsKey(id)) _previewSaved[id] = (parallax, parallax.Position);
            parallax.Position = BackgroundImporter.RuntimePosition(parallax, c);
        }
    }

    private void RestorePreview()
    {
        foreach ((Parallax2D node, Vector2 position) in _previewSaved.Values)
            if (IsInstanceValid(node)) node.Position = position;
        _previewSaved.Clear();
    }

    public override void _Draw()
    {
        if (!_previewOn || !Engine.IsEditorHint()) return;
        DrawRect(new Rect2(PreviewCameraTopLeft(), BackgroundImporter.ProjectScreenSize()), new Color(1f, 0.75f, 0.2f), false, 6f);
    }

    // =================================================================
    // 旧版物件包一层视差节点（撤销管理器调用）
    // =================================================================

    /// <summary>把 item 换成 wrapper（放在 item 原来的位置），item 改名 Image 放进 wrapper；物件标识记在 wrapper 上。</summary>
    public void WrapToolItem(Node2D item, Parallax2D wrapper)
    {
        Node parent = item.GetParent();
        int index = item.GetIndex();
        parent.RemoveChild(item);
        item.Name = BackgroundImporter.ImageName;
        if (item.HasMeta(BackgroundImporter.ToolMeta)) item.RemoveMeta(BackgroundImporter.ToolMeta);
        wrapper.AddChild(item);
        parent.AddChild(wrapper);
        parent.MoveChild(wrapper, index);
        AssignOwner(wrapper);
    }

    /// <summary>WrapToolItem 的撤销：拆掉 wrapper，item 放回 wrapper 所在的位置，名字和原来的标识还原。</summary>
    public void UnwrapToolItem(Node2D item, Parallax2D wrapper, string oldMeta)
    {
        Node parent = wrapper.GetParent();
        int index = wrapper.GetIndex();
        parent.RemoveChild(wrapper);
        wrapper.RemoveChild(item);
        item.Name = wrapper.Name;
        if (!string.IsNullOrEmpty(oldMeta)) item.SetMeta(BackgroundImporter.ToolMeta, oldMeta);
        parent.AddChild(item);
        parent.MoveChild(item, index);
        AssignOwner(item);
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
        // 预览改过的位置先还原，导入时读到的才是场景里真正的值（导入完下一帧会重新预览）
        RestorePreview();
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
