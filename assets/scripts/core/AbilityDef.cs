using Godot;

namespace SonOfMud.Core;

/// <summary>一项能力的定义（格挡、二段跳、攀墙……），一项一个 .tres。</summary>
[GlobalClass]
public partial class AbilityDef : KeptResource
{
    [Export] public StringName Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public Texture2D Icon { get; set; }
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
}
