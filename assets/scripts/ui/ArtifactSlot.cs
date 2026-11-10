using Godot;

namespace SonOfMud.UI;

/// <summary>
/// 器物环上的一个槽：Index 对应 ArtifactsChanged 掩码的第几位。
/// 两张图是场景里的两个子节点 Worn / Removed（TextureRect），这里只切换显示哪一个（C# 不引用贴图资源）。
/// </summary>
[GlobalClass]
public partial class ArtifactSlot : Control
{
    [Export(PropertyHint.Range, "0,6")] public int Index { get; set; }

    public bool IsWorn { get; private set; } = true;
    /// <summary>画面上确实是“戴着”的图在显示、“已放下”的图隐藏（或反过来）。</summary>
    public bool ShowsWorn => _worn != null && _removed != null && _worn.Visible && !_removed.Visible;
    public bool ShowsRemoved => _worn != null && _removed != null && !_worn.Visible && _removed.Visible;

    private CanvasItem _worn;
    private CanvasItem _removed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _worn = GetNodeOrNull<CanvasItem>("Worn");
        _removed = GetNodeOrNull<CanvasItem>("Removed");
        if (_worn == null || _removed == null) GD.PushError($"ArtifactSlot {Name}：缺少子节点 Worn / Removed。");
        SetWorn(IsWorn);
    }

    public void SetWorn(bool worn)
    {
        IsWorn = worn;
        if (_worn != null) _worn.Visible = worn;
        if (_removed != null) _removed.Visible = !worn;
    }
}
