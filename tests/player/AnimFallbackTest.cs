using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Narrative.World;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.World;

/// <summary>
/// 计划（泥形态美术、碰撞框、替补表、收集品图）PR A 的验收：
/// ① 碰撞框和图对上：主角身体 48×120、受击框 40×120；各敌人身体 / 受击框和数据一致；守坑人停步距离能横扫、也能放蝎尾，横扫判定盖住停步位置；
/// ② 替补表：攀墙 = 上升、下砸下落 = 停在下劈最后一帧、下砸落地 = 起跳蓄力播一遍、水形态缺图用泥形态的图；
///    泥收势先停在刚结束那一下攻击的最后一帧，0.25 秒后按走没走播待机 / 跑；
/// ③ 物件图：祈祷者雕像、泥板（按地图换残泥板，命运石板单独）、地上的泥瓶 / 泥肝、界面泥肝图标都换成图，高度固定，占位色块藏起来。
/// 用仓库里的正式美术图（assets/art/player、collectible、item）。
/// </summary>
public partial class AnimFallbackTest : LabSuiteBase
{
    private const string LabPath = "res://assets/scenes/tests/player_lab.tscn";
    private static readonly float FloorY = 600f - PlayerBody.HalfH;

    protected override string ReportName => "anim_fallback_results";

    protected override async Task RunChecks()
    {
        BodyChecks();
        await LoadLab(LabPath);
        await FallbackChecks();
        await RecoverChecks();
        await ObjectArtChecks();
    }

    // ---------- ① 碰撞框 ----------

    private void BodyChecks()
    {
        Node player = GD.Load<PackedScene>(PlayerBody.ScenePath).Instantiate();
        Vector2 body = ((RectangleShape2D)player.GetNode<CollisionShape2D>("CollisionShape2D").Shape).Size;
        Vector2 hurt = ((RectangleShape2D)player.GetNode<CollisionShape2D>("HurtBox/CollisionShape2D").Shape).Size;
        float feet = BodyFeet.FeetOffset((Node2D)player);
        player.Free();
        Check(body == new Vector2(48, 120) && hurt == new Vector2(40, 120) && Mathf.IsEqualApprox(feet, 60f),
            $"主角：身体 {body}、受击框 {hurt}（分别改），脚底在原点下方 {feet}");

        foreach (string kind in new[] { "prayer", "devout", "mason", "temple_guard" })
        {
            Node2D enemy = GD.Load<PackedScene>($"res://assets/scenes/enemy/{kind}.tscn").Instantiate<Node2D>();
            var data = (EnemyData)enemy.Get("Data").AsGodotObject();
            var bodyShape = enemy.GetNode<CollisionShape2D>("CollisionShape2D");
            var hurtShape = enemy.GetNode<CollisionShape2D>("HurtBox/CollisionShape2D");
            Vector2 b = ((RectangleShape2D)bodyShape.Shape).Size, h = ((RectangleShape2D)hurtShape.Shape).Size;
            Check(b == data.BodySize && h == data.BodySize && Mathf.IsEqualApprox(bodyShape.Position.Y, -b.Y / 2f) && Mathf.IsEqualApprox(hurtShape.Position.Y, -h.Y / 2f),
                $"{kind}：身体、受击框都是 {data.BodySize}，脚底在原点");
            enemy.Free();
        }

        Node2D keeperNode = GD.Load<PackedScene>("res://assets/scenes/enemy/keeper.tscn").Instantiate<Node2D>();
        var keeper = (SonOfMud.Enemy.Bosses.Keeper.KeeperData)keeperNode.Get("Data").AsGodotObject();
        var kBody = keeperNode.GetNode<CollisionShape2D>("CollisionShape2D");
        var kHurt = keeperNode.GetNode<CollisionShape2D>("HurtBox/CollisionShape2D");
        var kHit = keeperNode.GetNode<CollisionShape2D>("HitBox/CollisionShape2D");
        Vector2 hitSize = ((RectangleShape2D)kHit.Shape).Size;
        float hitNear = kHit.Position.X - hitSize.X / 2f, hitFar = kHit.Position.X + hitSize.X / 2f;
        Check(((RectangleShape2D)kBody.Shape).Size.Y <= 80f && ((RectangleShape2D)kHurt.Shape).Size == new Vector2(600, 400) && keeper.ArtFacesRight,
            "守坑人：走路用的身体只是贴地一块（不卡芦苇平台），受击框 600×400，图画朝右");
        Check(keeper.StopDistance <= keeper.SweepRange && keeper.StopDistance < keeper.TailTriggerRange
              && keeper.StopDistance >= hitNear && keeper.StopDistance <= hitFar && keeper.SweepRange <= hitFar,
            $"守坑人距离：停步 {keeper.StopDistance} ≤ 横扫 {keeper.SweepRange} < 蝎尾 {keeper.TailTriggerRange}；横扫判定 x∈[{hitNear},{hitFar}] 盖住停步位置和横扫距离");
        keeperNode.Free();
    }

    // ---------- ② 替补表 ----------

    private async Task FallbackChecks()
    {
        await Reset(new Vector2(900, FloorY), 1);
        PlayerAnimator anim = Player.Animator;
        SpriteFrames f = anim.Sprite.SpriteFrames;
        bool repo = anim.IsArt("mud_rise") && anim.IsArt("mud_attack_down") && anim.IsArt("mud_jump_windup") && anim.IsArt("mud_attack");
        Check(repo, "仓库里有泥形态的上升、下劈、起跳蓄力、攻击图（替补表要用到）");
        if (!repo) return;
        Check(anim.FallbackOf("mud_wall_climb") == "mud_rise" && f.GetFrameCount("mud_wall_climb") == f.GetFrameCount("mud_rise") && f.GetAnimationLoop("mud_wall_climb")
              && f.GetFrameTexture("mud_wall_climb", 0) == f.GetFrameTexture("mud_rise", 0),
            "攀墙没有专门的图：用上升的图循环播");
        int downLast = f.GetFrameCount("mud_attack_down") - 1;
        Check(anim.FallbackOf("mud_slam_fall") == "mud_attack_down" && f.GetFrameCount("mud_slam_fall") == 1
              && f.GetFrameTexture("mud_slam_fall", 0) == f.GetFrameTexture("mud_attack_down", downLast),
            "下砸下落：停在下劈最后一帧");
        Check(anim.FallbackOf("mud_slam_land") == "mud_jump_windup" && !f.GetAnimationLoop("mud_slam_land")
              && f.GetFrameCount("mud_slam_land") == f.GetFrameCount("mud_jump_windup"),
            "下砸落地：起跳蓄力播一遍（不循环）");
        Check(anim.FallbackOf("mud_idle") == "" && anim.FallbackOf("mud_run") == "", "有专门图的动作（待机、跑）不替补");
        Check(anim.IsArt("water_attack") && SameImage(f.GetFrameTexture("water_attack", 0), f.GetFrameTexture("mud_attack", 0))
              && anim.IsArt("water_block") && SameImage(f.GetFrameTexture("water_block", 0), f.GetFrameTexture("mud_block", 0)),
            "水形态没有自己的攻击、格挡图：先用泥形态的图");
    }

    /// <summary>两帧来自同一张图的同一格（每个形态各切一份，帧对象不同）。</summary>
    private static bool SameImage(Texture2D a, Texture2D b) =>
        a is AtlasTexture x && b is AtlasTexture y ? x.Atlas == y.Atlas && x.Region == y.Region : a == b;

    private async Task RecoverChecks()
    {
        await Reset(new Vector2(900, FloorY), 1);
        PlayerAnimator anim = Player.Animator;
        AnimatedSprite2D sprite = anim.Sprite;
        await Tap(PlayerActions.Attack);
        for (int i = 0; i < 60 && Player.State is not AttackRecoveryState; i++) await Frames(1);
        await Frames(1);
        int last = sprite.SpriteFrames.GetFrameCount("mud_attack") - 1;
        Check(Player.State is AttackRecoveryState && anim.RecoverFallbackActive && sprite.Animation == "mud_attack" && sprite.Frame == last && !sprite.IsPlaying(),
            $"泥收势（没有专门的图）：先停在泥攻击的最后一帧（{sprite.Animation} 第 {sprite.Frame} 帧，在播 {sprite.IsPlaying()}）");
        await Frames(Mathf.CeilToInt(anim.RecoverHoldSeconds * 60f) + 2);
        Check(Player.State is AttackRecoveryState && sprite.Animation == "mud_idle", $"0.25 秒后站着不动：播待机（{sprite.Animation}）");
        Press(PlayerActions.Right);
        await Frames(6);
        Check(Player.State is AttackRecoveryState && sprite.Animation == "mud_run", $"收势里往右走：播跑（{sprite.Animation}）");
        Release(PlayerActions.Right);
        await Frames(100);
        Check(!anim.RecoverFallbackActive, "收势结束后不再替补");
    }

    // ---------- ③ 物件图 ----------

    private static float ShownHeight(Sprite2D s) => s.Texture.GetHeight() * s.Scale.Y;

    private async Task ObjectArtChecks()
    {
        Node room = GD.Load<PackedScene>("res://assets/scenes/world/s1/S1_06.tscn").Instantiate();
        AddChild(room);
        await Frames(1);
        var statue = room.GetNode<Node2D>("Objects/Story_SevenNights");
        var art = statue.GetNodeOrNull<Sprite2D>(ArtSprite.NodeName);
        Check(art != null && Mathf.IsEqualApprox(ShownHeight(art), 120f) && !statue.GetNode<CanvasItem>("Placeholder").Visible
              && art.Texture.ResourcePath.Contains("spr_collectible_prayer_statue"),
            $"祈祷者雕像「七夜」：显示雕像图，高 {(art == null ? 0 : ShownHeight(art)):F0}（和主角一样高），占位色块藏起来");
        room.QueueFree();

        Node room2 = GD.Load<PackedScene>("res://assets/scenes/world/s1/S1_02.tscn").Instantiate();
        AddChild(room2);
        await Frames(1);
        var tablet = room2.GetNode<Node2D>("Objects/Tablet_nameless_day");
        var tArt = tablet.GetNodeOrNull<Sprite2D>(ArtSprite.NodeName);
        Check(tArt != null && Mathf.IsEqualApprox(ShownHeight(tArt), TabletPickup.ArtHeight) && !tablet.GetNode<CanvasItem>("Placeholder").Visible
              && tArt.Texture.ResourcePath.Contains("spr_collectible_tablet_pit"),
            "泥板（S1）：显示残泥板·泥坑的图，高 48，占位色块藏起来");
        room2.QueueFree();
        Check(TabletPickup.ArtName("destiny", "S2_04") == "spr_collectible_tablet_of_destiny" && TabletPickup.ArtName("smile", "S2_02") == "spr_collectible_tablet_temple"
              && TabletPickup.ArtName("curse", "S3_06") == "spr_collectible_tablet_divination" && TabletPickup.ArtName("gate_1", "S4_02") == "spr_collectible_tablet_summit",
            "泥板按地图换图：S2 塔庙、S3 占卜、S4 塔顶，命运石板用自己的图");

        var bottle = GD.Load<PickupItem>("res://assets/data/world/items/mud_bottle.tres");
        var liver = GD.Load<PickupItem>("res://assets/data/world/items/mud_liver.tres");
        Check(bottle.Art != null && bottle.Art.ResourcePath.Contains("spr_item_mud_bottle") && liver.Art != null && liver.Art.ResourcePath.Contains("spr_item_mud_liver")
              && Pickup.Size == new Vector2(40, 40),
            "地上的泥瓶、泥肝用图显示（40 高），拾取范围 40×40");

        Node hud = GD.Load<PackedScene>("res://assets/scenes/ui/hud.tscn").Instantiate();
        AddChild(hud);
        await Frames(1);
        var counter = hud.FindChildren("LiverCounter", "", true, false).OfType<SonOfMud.UI.LiverCounter>().FirstOrDefault();
        var iconArt = counter?.Icon?.GetNodeOrNull<TextureRect>("Art");
        Check(iconArt != null && iconArt.Texture.ResourcePath.Contains("spr_item_mud_liver"), "界面泥肝数量图标：换成泥肝图");
        hud.QueueFree();
        await Frames(2);
    }
}
