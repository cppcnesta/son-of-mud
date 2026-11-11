using System;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [字幕]：写在标签下面第一行，表示这一段用字幕条播放（不暂停游戏、不锁输入、自动消失）。
/// 剧情层按标签后第一条指令决定用哪个执行器；执行时什么也不做。
/// </summary>
public class SubtitleModeCommand : VNCommand
{
    public const string CommandName = "字幕";

    public override string Name => CommandName;
    public override string Usage => "[字幕]（写在标签下面第一行）";

    public override void Execute(string[] args, VNContext ctx, Action done) => done();
}
