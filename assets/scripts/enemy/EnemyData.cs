using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Enemy;

/// <summary>
/// 一种敌人的全部数值（assets/data/enemy/*.tres）。普通敌人没有接触伤害（Q8）：碰到身体不掉血，只有攻击判定伤人。
/// </summary>
[GlobalClass]
public partial class EnemyData : KeptResource
{
    [ExportGroup("基本")]
    /// <summary>种类编号，EnemyKilled 事件带的就是它。</summary>
    [Export] public StringName Kind { get; set; } = "";
    /// <summary>屏幕上显示的名字（「祈祷泥人」「塔庙泥卫」等）。</summary>
    [Export] public string DisplayName { get; set; } = "";
    [Export] public int MaxHealth { get; set; } = 30;
    [Export] public EnemyBehaviour Behaviour { get; set; } = EnemyBehaviour.Passive;
    [Export] public float MoveSpeed { get; set; } = 120f;
    [Export] public float Gravity { get; set; } = 1600f;
    /// <summary>身体碰撞框尺寸（脚底中心为原点）。</summary>
    [Export] public Vector2 BodySize { get; set; } = new(48f, 64f);
    [Export] public Color Tint { get; set; } = new("8a6a4a");
    /// <summary>
    /// 美术图一帧在游戏里显示的边长（像素）。标准：普通敌人 128（源图每帧 512×512），守坑人 256（源图每帧 1024×1024），
    /// 和主角同一个比例（源图 4 像素 = 游戏里 1 像素）。源图不是标准尺寸也照样缩放到这个大小，但会给警告。
    /// </summary>
    [Export] public float ArtCanvas { get; set; } = 128f;

    [ExportGroup("感知与巡逻")]
    /// <summary>水平方向看得见玩家的距离。</summary>
    [Export] public float VisionRange { get; set; } = 320f;
    /// <summary>竖直方向的容差：玩家脚底和自己脚底的高度差超过它就当作看不见（站在别的平台上）。</summary>
    [Export] public float VisionHeight { get; set; } = 128f;
    /// <summary>没看见玩家时，在出生点左右来回走动的距离；0 表示原地不动。</summary>
    [Export] public float PatrolDistance { get; set; }

    [ExportGroup("攻击")]
    [Export] public HitSpec AttackSpec { get; set; }
    /// <summary>水平距离小于等于它时开始前摇。</summary>
    [Export] public float AttackRange { get; set; } = 96f;
    [Export] public float AttackWindup { get; set; } = 0.5f;
    [Export] public float AttackActive { get; set; } = 0.15f;
    [Export] public float AttackRecover { get; set; } = 0.6f;
    /// <summary>冲锋型：判定期间的冲刺速度（方向在前摇开始时锁定）。</summary>
    [Export] public float ChargeSpeed { get; set; } = 600f;

    [ExportGroup("远程")]
    /// <summary>远程型：投掷物的飞行参数（重力、半径、寿命）；伤害仍是 AttackSpec。</summary>
    [Export] public ProjectileSpec ThrowSpec { get; set; }
    /// <summary>远程型：抛物线的水平速度（像素 / 秒），飞行时间 = 水平距离 / 它。</summary>
    [Export] public float ThrowSpeed { get; set; } = 420f;
    /// <summary>远程型：出手点相对脚底的位置（按朝右摆，朝左时左右翻转）。</summary>
    [Export] public Vector2 ThrowOrigin { get; set; } = new(20f, -56f);

    [ExportGroup("受击")]
    /// <summary>被打（没死）后的硬直时长。</summary>
    [Export] public float HurtStun { get; set; } = 0.3f;
    /// <summary>受到没打死它的攻击后逃跑的概率；被动型没逃跑就起身反击，攻击型没逃跑就进受击硬直。</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float FleeChance { get; set; }
    [Export] public float FleeSpeed { get; set; } = 220f;
    [Export] public float FleeDuration { get; set; } = 1.5f;

    [ExportGroup("掉落")]
    [Export] public DropTable Drops { get; set; }
}
