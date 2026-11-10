using Godot;

namespace SonOfMud.Player;

[GlobalClass]
public partial class MudPrototypeRules : Resource
{
    [ExportGroup("正式进度（测试场解锁独立）")]
    [Export(PropertyHint.Enum, "水,泥")] public int InitialForm { get; set; }
    [Export] public string[] InitialAbilities { get; set; } = new[] { "block", "form_switch" };
    [Export] public bool TestFastSwitch { get; set; } = true;

    [ExportGroup("尚未定案的动作规则")]
    [Export] public float SwitchCooldown { get; set; } = 0.15f;
    [Export] public bool AllowAirSwitch { get; set; } = true;
    [Export] public bool AllowActionCancel { get; set; }
    [Export] public bool OmnidirectionalBlock { get; set; } = true;
    [Export] public float DodgeInvulnerability { get; set; }
    [Export] public float HurtDuration { get; set; } = 0.18f;

    [ExportGroup("泥跳跃：互斥原型模组")]
    [Export(PropertyHint.Enum, "模组1：重跳,模组2：长按")] public int MudJumpModule { get; set; }
    [Export] public float UpgradedHoldHeight { get; set; } = 224f;
    [Export] public float LandingRadius { get; set; } = 150f;
    [Export] public float LandingDamage { get; set; } = 30f;
    [Export] public float SlamSpeed { get; set; } = 1000f;
    [Export] public float SlamDamage { get; set; } = 44f;

    [ExportGroup("白盒战斗")]
    [Export] public float PlayerHealth { get; set; } = 120f;
    [Export] public float BossStaggerDuration { get; set; } = 1.6f;
    [Export] public float BossVulnerability { get; set; } = 2f;
}
