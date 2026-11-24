using Godot;
using SonOfMud.Core;

namespace SonOfMud.Enemy.States;

/// <summary>
/// 死亡（终态）：判定已关、不再受击。
/// 有 death 图时（小怪碎裂成一堆泥，spr_enemy_&lt;种类&gt;_death_4x4.png）：0.9 秒播完碎裂，停在最后一帧（泥堆），
/// 停 HoldSeconds 秒后用 SinkSeconds 秒沉进地里（往下挪、变透明），沉完隐藏。没有图时原地留下红泥色块（占位），同样到时消失。
/// </summary>
public partial class DeadState : EnemyState
{
    /// <summary>碎裂动画的时长（秒）。</summary>
    public const float CrumbleSeconds = 0.9f;
    /// <summary>泥堆停留多久再开始沉（秒）。</summary>
    public const float HoldSeconds = 3f;
    /// <summary>沉进地里用多久（秒）。</summary>
    public const float SinkSeconds = 1f;
    /// <summary>沉下去的深度（游戏像素）。</summary>
    public const float SinkDepth = 18f;

    /// <summary>死了多久（游戏时间）。</summary>
    public float Elapsed { get; private set; }
    /// <summary>已经沉完、隐藏了。</summary>
    public bool Gone { get; private set; }

    public override string DisplayName => "死亡";
    public override string AnimName => "dead";
    public override float AnimDuration => CrumbleSeconds;

    public override void Enter(StateMsg msg)
    {
        Elapsed = 0f;
        Gone = false;
        E.Stop();
        E.CloseAttacks();
    }

    public override void PhysicsUpdate(double delta)
    {
        E.Stop();
        E.ApplyGravity((float)delta);
        if (Gone) return;
        Elapsed += (float)delta;
        float sink = Mathf.Clamp((Elapsed - CrumbleSeconds - HoldSeconds) / SinkSeconds, 0f, 1f);
        if (sink <= 0f) return;
        // 沉进地里：图往下挪、慢慢变透明（整个身体一起，占位色块也一样）
        AnimatedSprite2D sprite = E.Animator?.Sprite;
        if (sprite != null) sprite.Position = new Vector2(0f, SinkDepth * sink);
        E.Modulate = new Color(1f, 1f, 1f, 1f - sink);
        if (sink >= 1f)
        {
            Gone = true;
            E.Visible = false;
        }
    }
}
