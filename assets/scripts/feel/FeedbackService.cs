using System.Collections.Generic;
using Godot;
using SonOfMud.Camera;
using SonOfMud.Core;

namespace SonOfMud.Feel;

/// <summary>
/// 打击感服务（自动加载 Feedback）：全工程唯一调用 Juicee 插件的地方，负责顿帧、震屏、命中粒子和音效，并负责收尾。
/// 闪白和击退不在这里（受击方自己做）。
///
/// - 只听 Events：HitStopRequested、CameraShakeRequested、HitSparkRequested、SoundRequested。
/// - 顿帧：Juicee 把 Engine.TimeScale 设成 0，按真实时间恢复。正在顿帧时再来一个（同一刀打中几个敌人、连续命中），
///   从这次顿帧开始算，总时长取较长的那个，不累加：新的不比当前总时长长就忽略，长就把结束时间推到「开始 + 新的」。和暂停互不干扰：暂停只改 SceneTree.Paused（PauseService），顿帧只改 TimeScale。
/// - 震屏：Juicee 只晃镜头下的震动挂点（RoomCamera.ShakeRig），镜头读挂点、按 MaxShakeOffset 截断后写自己的 Offset。
/// - 收尾（取消责任在这里，不靠插件）：换房、主角死亡、重开、回主菜单、开新游戏、继续游戏，以及自己离开场景树时，
///   停掉所有进行中的效果、TimeScale 回到 1、震动挂点归零、命中粒子清掉。被停掉的旧效果之后不会再改任何东西。
/// - 没有 Juicee（插件没启用）时什么都不做，不报错。
/// 直接调用 /root/Juicee 和效果脚本，不用插件自带的 C# 包装（它用静态字段缓存节点，退出时有风险）。
/// </summary>
public partial class FeedbackService : Node
{
    public const string JuiceePath = "/root/Juicee";
    private const string EffectDir = "res://addons/juicee/effects/";
    private const string SparkPrefix = "_juicee_hit_spark_";

    public static FeedbackService Instance { get; private set; }

    /// <summary>当前顿帧的效果（Juicee 对象），没有时为 null。</summary>
    public GodotObject HitStopEffect { get; private set; }
    /// <summary>当前顿帧还剩多少秒（真实时间）。</summary>
    public double HitStopRemaining => _hitStopClock != null && _hitStopClock.TimeLeft > 0 ? _hitStopClock.TimeLeft : 0;
    public bool HitStopActive => HitStopRemaining > 0;
    /// <summary>真正开始（或延长）顿帧的次数（测试用；被忽略的请求不算）。</summary>
    public int HitStopStarts { get; private set; }
    /// <summary>收尾的次数（测试用）。</summary>
    public int Cancels { get; private set; }

    private SceneTreeTimer _hitStopClock;
    /// <summary>当前这次顿帧从开始算的总时长。</summary>
    private double _hitStopTotal;
    private readonly List<GodotObject> _shakes = new();
    private Node2D _sparkOrigin;
    private bool _subscribed;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _sparkOrigin = new Node2D { Name = "SparkOrigin" };
        AddChild(_sparkOrigin);
        Subscribe();
    }

    public override void _ExitTree()
    {
        if (Instance != this) return;
        CancelAll();
        Unsubscribe();
        Instance = null;
    }

    private void Subscribe()
    {
        Events events = Events.Instance;
        if (events == null || _subscribed) return;
        events.HitStopRequested += OnHitStopRequested;
        events.CameraShakeRequested += OnShakeRequested;
        events.HitSparkRequested += OnSparkRequested;
        events.SoundRequested += OnSoundRequested;
        events.RoomExiting += OnRoomExiting;
        events.PlayerDied += CancelAll;
        events.RestartRunRequested += CancelAll;
        events.QuitToMenuRequested += CancelAll;
        events.NewGameRequested += CancelAll;
        events.ContinueRequested += CancelAll;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        Events events = Events.Instance;
        if (events == null || !_subscribed) return;
        events.HitStopRequested -= OnHitStopRequested;
        events.CameraShakeRequested -= OnShakeRequested;
        events.HitSparkRequested -= OnSparkRequested;
        events.SoundRequested -= OnSoundRequested;
        events.RoomExiting -= OnRoomExiting;
        events.PlayerDied -= CancelAll;
        events.RestartRunRequested -= CancelAll;
        events.QuitToMenuRequested -= CancelAll;
        events.NewGameRequested -= CancelAll;
        events.ContinueRequested -= CancelAll;
        _subscribed = false;
    }

    private void OnRoomExiting(string roomId) => CancelAll();

    private Node Juicee => GetNodeOrNull(JuiceePath);

    /// <summary>新建一个 Juicee 效果（GDScript 资源），设好属性；插件不在时返回 null。</summary>
    private GodotObject NewEffect(string file, Godot.Collections.Dictionary properties)
    {
        if (Juicee == null || !ResourceLoader.Exists(EffectDir + file)) return null;
        var script = GD.Load<GDScript>(EffectDir + file);
        GodotObject effect = script.New().AsGodotObject();
        foreach (var (key, value) in properties) effect.Set(key.AsStringName(), value);
        return effect;
    }

    private static bool IsPlaying(GodotObject effect) => effect != null && IsInstanceValid(effect) && effect.Call("is_playing").AsBool();

    private static void Stop(GodotObject effect)
    {
        if (effect != null && IsInstanceValid(effect)) effect.Call("stop");
    }

    // ---------- 顿帧 ----------

    private void OnHitStopRequested(float seconds) => HitStop(seconds);

    /// <summary>顿帧 seconds 秒（真实时间）。正在顿帧时：从这次顿帧开始算总时长取较长的，不累加。</summary>
    public void HitStop(float seconds)
    {
        if (seconds <= 0f || !IsInsideTree()) return;
        double remaining = seconds;
        double total = seconds;
        if (HitStopActive)
        {
            if (seconds <= _hitStopTotal) return;
            double elapsed = _hitStopTotal - HitStopRemaining;
            remaining = seconds - elapsed;
        }
        GodotObject effect = NewEffect("hit_stop_effect.gd", new() { ["freeze_duration"] = (float)remaining, ["time_scale_during"] = 0f });
        if (effect == null) return;
        Stop(HitStopEffect); // 旧的停掉：它的计时器之后醒来也不会再改 TimeScale
        HitStopEffect = effect;
        HitStopStarts++;
        _hitStopTotal = total;
        _hitStopClock = GetTree().CreateTimer(remaining, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
        effect.Call("apply", this);
    }

    // ---------- 震屏 ----------

    private void OnShakeRequested(float strength, float duration) => Shake(strength, duration);

    /// <summary>晃当前镜头的震动挂点。多次叠加时由镜头按上限截断。</summary>
    public void Shake(float strength, float duration)
    {
        if (strength <= 0f || duration <= 0f || !IsInsideTree()) return;
        if (GetViewport()?.GetCamera2D() is not RoomCamera camera || camera.ShakeRig == null) return;
        float falloff = camera.Settings?.ShakeFalloff ?? 2f;
        GodotObject effect = NewEffect("shake_effect.gd", new()
        {
            ["intensity"] = strength,
            ["duration"] = duration,
            ["frequency"] = 30f,
            ["decay"] = falloff * 0.5f,
            ["roll_degrees"] = 0f,
            ["use_noise"] = true,
            ["target_path"] = new NodePath("."),
        });
        if (effect == null) return;
        _shakes.RemoveAll(e => !IsPlaying(e));
        _shakes.Add(effect);
        effect.Call("apply", camera.ShakeRig);
    }

    /// <summary>还有震屏在进行。</summary>
    public bool IsShaking()
    {
        _shakes.RemoveAll(e => !IsPlaying(e));
        return _shakes.Count > 0;
    }

    // ---------- 粒子、音效 ----------

    private void OnSparkRequested(Vector2 position, Vector2 direction, int amount, Color color, float speed)
    {
        if (amount <= 0 || !IsInsideTree() || GetTree().CurrentScene == null) return;
        GodotObject effect = NewEffect("hit_spark_effect.gd", new()
        {
            ["amount"] = amount,
            ["speed"] = speed,
            ["color"] = color,
            ["cone"] = 35f,
        });
        if (effect == null) return;
        _sparkOrigin.GlobalPosition = position;
        effect.Call("apply", _sparkOrigin, new Godot.Collections.Dictionary { ["hit_direction"] = direction == Vector2.Zero ? Vector2.Right : direction });
    }

    private void OnSoundRequested(Godot.Collections.Array streams)
    {
        var typed = new Godot.Collections.Array<AudioStream>();
        foreach (Variant v in streams)
            if (v.AsGodotObject() is AudioStream stream) typed.Add(stream);
        if (typed.Count == 0 || !IsInsideTree()) return;
        GodotObject effect = NewEffect("sound_effect.gd", new() { ["streams"] = Variant.From(typed), ["pitch_min"] = 0.92f, ["pitch_max"] = 1.08f });
        effect?.Call("apply", this);
    }

    /// <summary>命中粒子还在场上的个数（测试用）。</summary>
    public int ActiveSparks()
    {
        Node scene = IsInsideTree() ? GetTree().CurrentScene : null;
        if (scene == null) return 0;
        int count = 0;
        foreach (Node child in scene.GetChildren())
            if (child.Name.ToString().StartsWith(SparkPrefix) && child is CpuParticles2D p && p.Emitting && !p.IsQueuedForDeletion()) count++;
        return count;
    }

    // ---------- 收尾 ----------

    /// <summary>停掉所有进行中的效果：TimeScale 回到 1、震动挂点归零、命中粒子清掉。</summary>
    public void CancelAll()
    {
        Cancels++;
        Stop(HitStopEffect);
        HitStopEffect = null;
        _hitStopClock = null;
        foreach (GodotObject shake in _shakes) Stop(shake);
        _shakes.Clear();
        Engine.TimeScale = 1.0;
        if (IsInsideTree())
        {
            if (GetViewport()?.GetCamera2D() is RoomCamera camera && camera.ShakeRig != null) camera.ShakeRig.Position = Vector2.Zero;
            Node scene = GetTree().CurrentScene;
            if (scene != null)
                foreach (Node child in scene.GetChildren())
                    if (child.Name.ToString().StartsWith(SparkPrefix) && child is CpuParticles2D) child.QueueFree();
        }
    }
}
