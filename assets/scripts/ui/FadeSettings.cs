using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>黑幕淡入淡出的可调数值，放在 assets/data/ui/fade_settings.tres。</summary>
[GlobalClass]
public partial class FadeSettings : KeptResource
{
    [Export] public float FadeOutSeconds { get; set; } = 0.25f;
    [Export] public float FadeInSeconds { get; set; } = 0.25f;
    [Export] public Color Color { get; set; } = Colors.Black;
}
