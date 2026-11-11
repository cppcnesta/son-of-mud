using SonOfMud.Core;

namespace SonOfMud.Player.States;

/// <summary>输入锁（Core InputLock）生效期间：不能操作、不受击，只受重力。解锁 → 自由状态。</summary>
public partial class LockedState : PlayerState
{
    public override string AnimName => "idle";
    public override string DisplayName => "锁定";
    public override bool AcceptsHits => false;
    public override bool Lockable => false;

    public override void PhysicsUpdate(double delta)
    {
        P.StopHorizontal();
        P.ApplyGravity((float)delta);
        if (!InputLock.IsLocked) P.ToFreeState();
    }
}
