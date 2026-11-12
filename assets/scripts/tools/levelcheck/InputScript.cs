using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SonOfMud.Tools.LevelCheck;

/// <summary>按住一段输入直到某个条件成立。</summary>
public enum Until
{
    None,
    /// <summary>回到地面自由状态（Idle / Run）。</summary>
    Grounded,
    /// <summary>进入攀墙。</summary>
    Climbing,
    /// <summary>离地（IsOnFloor 为假）。</summary>
    Airborne,
}

/// <summary>
/// 脚本的一步：Frames 帧（Until 为 None 时）或一直到条件成立（Until 不为 None 时，最多 MaxFrames 帧）。
/// Jump 表示这一步的第一帧按下跳（之后松开）。
/// </summary>
public sealed class InputStep
{
    public int Frames = 1;
    public int Axis;
    public bool Up;
    public bool Jump;
    public Until Until;
    public int MaxFrames = 600;

    public static InputStep For(int frames, int axis, bool up = false, bool jump = false) =>
        new() { Frames = frames, Axis = axis, Up = up, Jump = jump };

    public static InputStep Till(Until until, int axis, bool up = false, bool jump = false, int maxFrames = 600) =>
        new() { Until = until, Axis = axis, Up = up, Jump = jump, MaxFrames = maxFrames };

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Jump) sb.Append("跳+");
        sb.Append(Axis switch { < 0 => "左", > 0 => "右", _ => "无方向" });
        if (Up) sb.Append("+上");
        sb.Append(Until switch
        {
            Until.None => $" {Frames}帧",
            Until.Grounded => " 到落地",
            Until.Climbing => " 到攀上墙",
            _ => " 到离地",
        });
        return sb.ToString();
    }
}

/// <summary>一个动作的输入脚本。所有步骤结束后：松开所有键，等到落地站稳（Grounded）。</summary>
public sealed class InputScript
{
    public string Name { get; }
    public IReadOnlyList<InputStep> Steps { get; }
    public int SettleMaxFrames { get; init; } = 600;

    public InputScript(string name, params InputStep[] steps)
    {
        Name = name;
        Steps = steps;
        for (int i = 1; i < steps.Length; i++)
            if (steps[i].Jump && steps[i - 1].Jump && steps[i - 1].Until == Until.None && steps[i - 1].Frames == 1)
                throw new ArgumentException($"{name}：连续两帧按跳，第二次按不出“刚按下”");
    }

    public override string ToString() => Name + "［" + string.Join("，", Steps.Select(s => s.ToString())) + "］";
}

/// <summary>
/// 按脚本逐帧给出输入。模拟器和真实按键机器人共用同一个运行器，保证两边对条件的反应时机一样：
/// 每帧调用 Next(obs)，obs 是“做决定时能看到的最新状态”。真实游戏里测试协程在物理帧开头恢复，
/// 看到的是上一帧结束时的状态，而这时按下的键要到下一帧才生效，所以第 j 帧的输入只能根据第 j-1-Delay 帧之后的状态决定
/// （Delay 由校准测试的延迟探针实测，见 ObservationDelay）。为避免用到这一步开始之前的旧状态，
/// 带条件的步骤至少运行 1 + Delay 帧之后才检查条件。
/// </summary>
public sealed class ScriptRunner
{
    /// <summary>观察延迟（帧）。校准测试里的探针会实测并断言等于这个值。</summary>
    public const int ObservationDelay = 1;

    private readonly InputScript _script;
    private readonly int _delay;
    private int _step;
    private int _frameInStep;

    public bool Done { get; private set; }
    public bool Failed { get; private set; }
    public int StepIndex => _step;

    public ScriptRunner(InputScript script, int delay = ObservationDelay)
    {
        _script = script;
        _delay = delay;
    }

    private bool Fresh => _frameInStep >= 1 + _delay;

    private static bool Met(Until until, SimObservation obs) => until switch
    {
        Until.Grounded => obs.Grounded,
        Until.Climbing => obs.Climbing,
        Until.Airborne => obs.Airborne,
        _ => false,
    };

    /// <summary>返回这一帧的输入；返回后 Done 为真表示脚本已经结束（这一帧不用再推进）。</summary>
    public SimInput Next(SimObservation obs)
    {
        if (Done) return default;
        while (true)
        {
            if (_step < _script.Steps.Count)
            {
                InputStep s = _script.Steps[_step];
                bool finished = s.Until == Until.None
                    ? _frameInStep >= s.Frames
                    : Fresh && Met(s.Until, obs);
                if (finished)
                {
                    _step++;
                    _frameInStep = 0;
                    continue;
                }
                if (s.Until != Until.None && _frameInStep >= s.MaxFrames)
                {
                    Done = Failed = true;
                    return default;
                }
                var input = new SimInput(s.Axis, s.Up, s.Jump && _frameInStep == 0);
                _frameInStep++;
                return input;
            }
            // 收尾：松开所有键，等站稳
            if (Fresh && obs.Grounded)
            {
                Done = true;
                return default;
            }
            if (_frameInStep >= _script.SettleMaxFrames)
            {
                Done = Failed = true;
                return default;
            }
            _frameInStep++;
            return default;
        }
    }
}
