using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// 手动试玩竖切片（编辑器里打开 tests/world/slice_play.tscn 按 F6）：A/D 走、空格跳，从 S1_03 左侧出发往右走到 S1_04。
/// 用 TestMover 代替还没做的 Player。不参与自动测试。
/// </summary>
public partial class SlicePlay : Node
{
    [Export] public string StartRoom { get; set; } = "S1_03";
    [Export] public StringName StartSpawn { get; set; } = "From_S1_02";

    public override void _Ready() => Callable.From(Start).CallDeferred();

    private void Start()
    {
        GameSession.Reset();
        MetSysApi.ResetState();
        MetSysApi.SetSaveData();
        Node game = GD.Load<PackedScene>(WorldTestBase.GameScene).Instantiate();
        AddChild(game);
        game.AddChild(new TestMover { Name = "Mover", UseInput = true });
        game.GetNode<WorldGame>("WorldGame").LoadRoomImmediate(StartRoom, StartSpawn);
    }
}
