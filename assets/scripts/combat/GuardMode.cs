namespace SonOfMud.Combat;

/// <summary>HurtBox 当前的格挡方式。Single：挡住一次就结束；Hold：按住期间可以挡多段。</summary>
public enum GuardMode
{
    None,
    Single,
    Hold,
}
