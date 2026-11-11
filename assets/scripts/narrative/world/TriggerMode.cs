namespace SonOfMud.Narrative.World;

/// <summary>StoryTrigger 什么时候触发。</summary>
public enum TriggerMode
{
    /// <summary>玩家站在区域里按交互键（E）。</summary>
    Interact,
    /// <summary>玩家身体进入区域时自动触发。</summary>
    Enter,
}
