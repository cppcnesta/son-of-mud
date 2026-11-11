using System.Collections.Generic;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>动作类别：决定从段上的哪些位置出发。</summary>
public enum ActionKind
{
    /// <summary>跳（含攀墙、蹬墙跳）：从段上每个采样点出发。</summary>
    Jump,
    /// <summary>走下边缘：只从段内一点出发、朝没有墙的一侧走。</summary>
    WalkOff,
}

public sealed class MoveAction
{
    public string Name;
    public int Dir;
    public ActionKind Kind;
    public InputScript Script;

    public override string ToString() => Script.ToString();
}

/// <summary>
/// 每个能站的位置要枚举的动作（计划 5.1）：原地跳、跑跳（按住不同时长）、先竖直跳再给方向、跳起攀墙、
/// 走下边缘（继续按住 / 松手 / 土狼跳 / 贴墙攀 / 回头攀）、攀墙中蹬墙跳（离墙 / 继续攀）。左右各一套。
/// </summary>
public static class ActionSet
{
    private static List<MoveAction> _default;

    public static IReadOnlyList<MoveAction> Default => _default ??= Build();

    private static InputStep F(int frames, int axis, bool up = false, bool jump = false) => InputStep.For(frames, axis, up, jump);
    private static InputStep U(Until until, int axis, bool up = false, bool jump = false) => InputStep.Till(until, axis, up, jump);

    private static MoveAction A(string name, int dir, ActionKind kind, params InputStep[] steps) =>
        new() { Name = name, Dir = dir, Kind = kind, Script = new InputScript(name, steps) };

    private static List<MoveAction> Build()
    {
        var list = new List<MoveAction> { A("原地跳", 0, ActionKind.Jump, F(1, 0, jump: true)) };
        foreach (int d in new[] { 1, -1 })
        {
            string s = d > 0 ? "右" : "左";
            list.Add(A($"跑跳{s}", d, ActionKind.Jump, F(1, d), U(Until.Grounded, d, jump: true)));
            foreach (int h in new[] { 6, 12, 20, 30 })
                list.Add(A($"跑跳{s}按{h}帧", d, ActionKind.Jump, F(1, d), F(h, d, jump: true)));
            foreach (int delay in new[] { 10, 20, 30, 40 })
                list.Add(A($"原地跳{delay}帧后{s}", d, ActionKind.Jump, F(delay, 0, jump: true), U(Until.Grounded, d)));
            list.Add(A($"跳起攀{s}", d, ActionKind.Jump, F(1, d), U(Until.Grounded, d, up: true, jump: true)));
            foreach (int delay in new[] { 15, 30 })
                list.Add(A($"原地跳{delay}帧后攀{s}", d, ActionKind.Jump, F(delay, 0, jump: true), U(Until.Grounded, d, up: true)));
            foreach (int k in new[] { 0, 30, 60 })
            {
                var steps = new List<InputStep> { F(1, d), U(Until.Climbing, d, up: true, jump: true) };
                if (k > 0) steps.Add(F(k, d, up: true));
                steps.Add(U(Until.Grounded, -d, jump: true));
                list.Add(A($"攀{s}墙{k}帧后蹬墙跳离开", d, ActionKind.Jump, steps.ToArray()));
            }
            list.Add(A($"攀{s}墙蹬墙跳后继续攀", d, ActionKind.Jump, F(1, d), U(Until.Climbing, d, up: true, jump: true), F(20, d, up: true), U(Until.Grounded, d, up: true, jump: true)));

            list.Add(A($"向{s}走下边缘", d, ActionKind.WalkOff, U(Until.Airborne, d), U(Until.Grounded, d)));
            list.Add(A($"向{s}走下边缘后松手", d, ActionKind.WalkOff, U(Until.Airborne, d)));
            list.Add(A($"向{s}走下边缘土狼跳", d, ActionKind.WalkOff, U(Until.Airborne, d), U(Until.Grounded, d, jump: true)));
            list.Add(A($"向{s}走下边缘贴墙攀", d, ActionKind.WalkOff, U(Until.Airborne, d), U(Until.Grounded, d, up: true)));
            list.Add(A($"向{s}走下边缘回头攀", d, ActionKind.WalkOff, U(Until.Airborne, d), U(Until.Grounded, -d, up: true)));
        }
        return list;
    }
}
