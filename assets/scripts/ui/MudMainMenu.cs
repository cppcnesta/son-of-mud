using Godot;
using System;

public partial class MudMainMenu : Control
{
    public override void _Ready()
    {
        var background = new ColorRect { Color = new Color("17252d") };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        AddLabel("泥之子", new Vector2(100, 80), 42);
        AddLabel("白盒测试版本", new Vector2(100, 210), 21);
        AddButton("开始旅程", 310, StartNew);
        AddButton("继续旅程", 375, Resume);
        AddButton("训练场", 440, Lab);
        AddLabel("A/D移动　空格/W跳跃　Q切换　Shift闪避　J攻击　K格挡　E交互\nF2训练场　B调整泥跳跃　F8重新开始", new Vector2(100, 540), 18);
    }
    private void AddLabel(string text, Vector2 at, int fontSize)
    {
        var label = new Label { Text = text, Position = at };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(label);
    }
    private void AddButton(string text, float y, Action callback)
    {
        var button = new Button { Text = text, Position = new Vector2(100, y), Size = new Vector2(600, 50) };
        button.AddThemeFontSizeOverride("font_size", 23);
        button.Pressed += callback;
        AddChild(button);
    }
    public void StartNew() { MudProgress.Instance.NewGame(); MudRoomTravel.Instance.EnterRoom("M01", "Start", false); }
    public void Resume()
    {
        var progress = MudProgress.Instance;
        progress.LeaveTest();
        var room = progress.LastRoom;
        if (!ResourceLoader.Exists(MudRoomTravel.Instance.RoomPath(room))) room = "M01";
        MudRoomTravel.Instance.EnterRoom(room, room != "M01" ? progress.LastSpawn : "Start", false);
    }
    public void Lab() { MudProgress.Instance.EnterTest(); MudRoomTravel.Instance.EnterRoom("TEST", "Start", false); }
}
