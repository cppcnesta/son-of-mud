using Godot;

namespace SonOfMud.UI;

/// <summary>器物环上的一个槽：Index 对应 ArtifactsChanged 掩码的第几位，戴着 / 已放下各一张图。</summary>
[GlobalClass]
public partial class ArtifactSlot : TextureRect
{
    [Export(PropertyHint.Range, "0,6")] public int Index { get; set; }
    [Export] public Texture2D Worn { get; set; }
    [Export] public Texture2D Removed { get; set; }

    public bool IsWorn { get; private set; } = true;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Texture = IsWorn ? Worn : Removed;
    }

    public void SetWorn(bool worn)
    {
        IsWorn = worn;
        Texture = worn ? Worn : Removed;
    }
}
