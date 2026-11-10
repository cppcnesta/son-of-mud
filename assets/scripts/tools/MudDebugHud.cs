using Godot;
using System.Collections.Generic;
using SonOfMud.Core;
using SonOfMud.Player;

namespace SonOfMud.Tools;

[GlobalClass]
public partial class MudDebugHud : CanvasLayer
{
    public MudWhiteboxRoom Room { get; set; }
    public Label Status { get; private set; }
    public Label Message { get; private set; }
    public Label Prompt { get; private set; }
    public Label Controls { get; private set; }
    private float _lifetime;
    private static readonly Dictionary<string, string> Names = new()
    {
        ["block"] = "格挡", ["form_switch"] = "形态切换", ["double_jump"] = "二段跳", ["wall_climb"] = "攀墙",
        ["heavy_slam"] = "重压下砸", ["dive"] = "潜游", ["high_jump_upgrade"] = "高跳升级"
    };
    public override void _Ready()
    {
        Layer = 10;
        AddChild(new ColorRect { Color = new Color(0.035f, 0.055f, 0.075f, 0.92f), Size = new Vector2(1152, 178), MouseFilter = Control.MouseFilterEnum.Ignore });
        Status = MakeLabel(new Vector2(22, 12), new Vector2(1100, 135), 17);
        Controls = MakeLabel(new Vector2(22, 145), new Vector2(1100, 28), 16);
        Controls.Text = "A/D移动　空格/W跳跃　Q形态　Shift闪避　J攻击　K格挡　S+J空中下砸　E交互　R重试　Esc菜单";
        Message = MakeLabel(new Vector2(26, 518), new Vector2(1100, 95), 20);
        Prompt = MakeLabel(new Vector2(26, 614), new Vector2(1100, 30), 17);
        Message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Message.AddThemeColorOverride("font_color", new Color("ffe2a0"));
    }
    public Label MakeLabel(Vector2 at, Vector2 dimensions, int fontSize)
    {
        var label = new Label { Position = at, Size = dimensions, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(label);
        return label;
    }
    public void Say(string text, float duration = 7f) { Message.Text = text; _lifetime = duration; }
    public override void _Process(double delta)
    {
        if (Room == null || !GodotObject.IsInstanceValid(Room.Player)) return;
        _lifetime -= (float)delta;
        if (_lifetime <= 0f) Message.Text = "";
        var player = Room.Player;
        var progress = MudProgress.Instance;
        var unlocked = new List<string>();
        foreach (var entry in Names) if (progress.HasAbility(entry.Key)) unlocked.Add(entry.Value);
        var recoveryName = new[] { "无", "闪避", "攻击" }[(int)player.Recovery.RecoverySource];
        var blockWindow = player.Action.State == MudActionState.Kind.Block ? player.Action.Remaining : 0f;
        var districts = (progress.Flag("district_C04") ? 1 : 0) + (progress.Flag("district_C05") ? 1 : 0) + (progress.Flag("district_C06") ? 1 : 0);
        Status.Text = $"{Room.RoomId}　{Room.RoomTitle}　｜　{player.Config().DisplayName}形态　泥跳{progress.MudModule + 1}　{player.Action.Label()}　生命{Mathf.CeilToInt(player.Health)}\n" +
            $"闪避CD {player.DodgeCooldown:F2}s　格挡CD {player.Guard.CooldownRemaining:F2}s　攻击间隔 {player.Combat.AttackIntervalRemaining:F2}s　格挡窗口 {blockWindow:F2}s\n" +
            $"收势：{recoveryName} {player.Recovery.Remaining:F2}s　速度{Mathf.RoundToInt(player.PenaltyMultiplier() * 100f)}%　格挡增伤：{(player.Combat.BonusReady ? "就绪" : "无")}　能力：{string.Join("、", unlocked)}\n" +
            $"街区 {districts}/3　{(progress.TestMode ? "独立训练" : "旅程已记录")}　｜　F2训练场　B调整跳跃　F6训练能力　F8重新开始";
        Prompt.Text = Room.CurrentPrompt;
    }
}
