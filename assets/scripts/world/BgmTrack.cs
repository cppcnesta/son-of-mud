using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>一个地图（区域）对应的一首 BGM。</summary>
[GlobalClass]
public partial class BgmTrack : KeptResource
{
    /// <summary>区域：S1、S2、S3、S4……</summary>
    [Export] public string Region { get; set; } = "";
    [Export] public AudioStream Stream { get; set; }
}
