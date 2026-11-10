using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [改名 角色 新名字]：之后这个角色的名牌显示新名字，比如终 Boss 之后 [改名 ？？？ 穆尔图库尔]。
/// 写进变量 name.&lt;角色显示名&gt;（字符串），随存档保存。
/// </summary>
public class RenameCommand : VNCommand
{
    public const string VarPrefix = "name.";

    public override string Name => "改名";
    public override int MinArgs => 2;
    public override string Usage => "[改名 角色 新名字]";

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        ctx.Vars.Set(VarPrefix + ctx.CanonicalName(args[0]), args[1]);
        done();
    }
}
