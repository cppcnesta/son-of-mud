using Godot;

namespace SonOfMud.World;

/// <summary>出生点。每个出口一个（From_来源房间），另加 Default；有存档点的房间还有一个 Save。</summary>
[GlobalClass]
public partial class SpawnPoint : Marker2D
{
    public static readonly StringName DefaultId = "Default";
    public static readonly StringName SaveId = "Save";

    [Export] public StringName SpawnId { get; set; } = "Default";
}
