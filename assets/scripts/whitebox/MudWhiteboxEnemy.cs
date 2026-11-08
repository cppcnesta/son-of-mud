using Godot;
using GDictionary = Godot.Collections.Dictionary;

[GlobalClass]
public partial class MudWhiteboxEnemy : StaticBody2D, IMudDamageable
{
    [Signal] public delegate void DefeatedEventHandler();
    [Export] public string DisplayName { get; set; } = "持锤泥人";
    [Export] public float MaxHealth { get; set; } = 60f;
    [Export] public float Damage { get; set; } = 12f;
    [Export] public float TelegraphTime { get; set; } = 0.8f;
    [Export] public float AttackTime { get; set; } = 0.18f;
    [Export] public float RecoveryTime { get; set; } = 1.15f;
    [Export] public float AttackRange { get; set; } = 135f;
    [Export] public float VulnerabilityMultiplier { get; set; } = 2f;
    [Export] public string BossKind { get; set; } = "";
    [Export] public bool Passive { get; set; }
    public float Health { get; set; }
    public int Phase { get; set; }
    public float Clock { get; set; } = 0.6f;
    public string AttackId { get; set; } = "";
    public int Sequence { get; private set; }
    public int Stage { get; private set; } = 1;
    public float LockedX { get; private set; }
    private readonly MudAttackLedger _ledger = new();
    public Label Label { get; private set; }
    private Vector2 _bodySize = new(46, 64);
    private static readonly string[] PhaseNames = { "", "蓄势", "出手", "失衡", "" };

    public override void _Ready()
    {
        AddToGroup("damageables");
        CollisionLayer = 4;
        CollisionMask = 0;
        Health = MaxHealth;
        if (BossKind.Length > 0 && BossKind != "dummy") _bodySize = new Vector2(100, 144);
        AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = _bodySize } });
        Label = new Label
        {
            Position = new Vector2(-80, -_bodySize.Y * 0.5f - 70f), Size = new Vector2(160, 70),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Label.AddThemeFontSizeOverride("font_size", 16);
        AddChild(Label);
    }
    public bool ReceiveHit(float amount, string attackId, GDictionary info)
    {
        if (Phase == 4 || !_ledger.Accept(attackId)) return false;
        Health -= amount * (Phase == 3 ? VulnerabilityMultiplier : 1f);
        if (Label != null) Label.Text = $"{DisplayName}\n{Mathf.Max(0, Mathf.CeilToInt(Health))}/{(int)MaxHealth}";
        if (Health <= 0f)
        {
            if (BossKind == "final" && Stage == 1)
            { Stage = 2; DisplayName = "伊姆杜"; Health = MaxHealth; Phase = 3; Clock = 2f; }
            else { Phase = 4; EmitSignal(SignalName.Defeated); QueueFree(); }
        }
        QueueRedraw();
        return true;
    }
    public override void _PhysicsProcess(double delta)
    {
        if (GetTree().GetFirstNodeInGroup("player") is not MudPlayer player || Phase == 4) return;
        Label.Text = $"{DisplayName}\n{Mathf.CeilToInt(Health)}/{(int)MaxHealth}  {PhaseNames[Phase]}";
        if (Passive) return;
        Clock -= (float)delta;
        if (Phase == 0 && Mathf.Abs(player.GlobalPosition.X - GlobalPosition.X) > 600f) return;
        if (Phase == 2)
        {
            var center = BossKind == "dragon" ? LockedX : GlobalPosition.X;
            if (Mathf.Abs(player.GlobalPosition.X - center) < AttackRange && Mathf.Abs(player.GlobalPosition.Y - GlobalPosition.Y) < 115f)
            {
                var result = player.ReceiveAttack(Damage, AttackId, GlobalPosition);
                if (result == "blocked" && BossKind == "keeper") { Phase = 3; Clock = player.Rules.BossStaggerDuration; }
            }
        }
        if (Clock <= 0f)
        {
            switch (Phase)
            {
                case 0:
                    Phase = 1; Clock = TelegraphTime; LockedX = player.GlobalPosition.X;
                    AttackId = $"enemy_{GetInstanceId()}_{++Sequence}"; break;
                case 1: Phase = 2; Clock = AttackTime; break;
                case 2:
                    Phase = 0; Clock = RecoveryTime;
                    if (BossKind == "dragon" && (player.GlobalPosition.Y < GlobalPosition.Y - 100f || Mathf.Abs(player.GlobalPosition.X - LockedX) > 180f))
                    { Phase = 3; Clock = 2f; }
                    break;
                case 3: Phase = 0; Clock = RecoveryTime; break;
            }
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        var color = new Color(Phase switch { 1 => "ffd471", 2 => "f45050", 3 => "a5d0ce", _ => "b15a55" });
        DrawRect(new Rect2(-_bodySize * 0.5f, _bodySize), color);
        if (Phase == 1 || Phase == 2)
        {
            var center = BossKind == "dragon" ? LockedX - GlobalPosition.X : 0f;
            DrawRect(new Rect2(center - AttackRange, -100, AttackRange * 2f, 200), new Color(1f, 0.3f, 0.2f, 0.18f));
        }
    }
}
