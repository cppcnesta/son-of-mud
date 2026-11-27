using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.World;

/// <summary>
/// 新手引导区：玩家身体走进来时只记一笔 tutorial.due.&lt;Label&gt; = true（「这条提示该播了」），不直接播。
/// 什么时候真正显示由剧情层的 TutorialHints 决定（等没有剧情、字幕条空着再播），所以开场、拾取剧情、别的字幕都不会把提示吞掉。
/// 区域大小和位置在编辑器里调（子节点 Shape）。没有外观，不挡路（只检测玩家身体层，自己不在任何碰撞层上）。
/// </summary>
[GlobalClass]
public partial class TutorialZone : Area2D
{
    /// <summary>tutorial.orph 里的标签，例如「移动」「攻击」。</summary>
    [Export] public string Label { get; set; } = "";

    private ulong _readyFrame;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = Layers.PlayerBody;
        Monitoring = true;
        Monitorable = false;
        BodyEntered += OnBodyEntered;
        _readyFrame = Engine.GetPhysicsFrames();
    }

    private void OnBodyEntered(Node2D body)
    {
        // 和 StoryTrigger 一样：房间刚放进场景的头几个物理帧，玩家可能还在传送前的位置，等两帧再确认。
        if (Engine.GetPhysicsFrames() - _readyFrame <= 2)
        {
            _ = ConfirmLater(body);
            return;
        }
        TutorialHints.MarkDue(Label);
    }

    private async System.Threading.Tasks.Task ConfirmLater(Node2D body)
    {
        for (int i = 0; i < 2; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (!IsInstanceValid(this) || !IsInsideTree()) return;
        }
        if (IsInstanceValid(body) && OverlapsBody(body)) TutorialHints.MarkDue(Label);
    }
}
