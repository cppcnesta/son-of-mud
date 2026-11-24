using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 按地图放 BGM（main_game.tscn 里的 Music 节点）。收到 RoomEntered 时看房间在哪个地图（<see cref="BgmTable"/> 里查曲子）：
/// - 还是同一个地图（同一首曲子）：不动，接着循环，换房间不会从头放；
/// - 换了地图：正在放的淡出，新地图的曲子淡入（两条播放器交叉淡入淡出）；新地图没有曲子就只淡出，变安静。
/// 暂停（菜单、地图、收集页、剧情）时照常放（ProcessMode = Always）。
/// </summary>
[GlobalClass]
public partial class BgmPlayer : Node
{
    [Export] public BgmTable Table { get; set; }

    /// <summary>现在（或正在淡入的）曲子所属的区域；没有音乐时是空字符串。</summary>
    public string CurrentRegion { get; private set; } = "";
    /// <summary>现在（或正在淡入的）曲子；没有音乐时是 null。</summary>
    public AudioStream CurrentStream { get; private set; }
    /// <summary>从头开始放过几次曲子（测试用：同一个地图里换房间不会增加）。</summary>
    public int StartCount { get; private set; }

    private AudioStreamPlayer _a, _b, _active;
    private Tween _tween;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Table ??= GD.Load<BgmTable>(BgmTable.DefaultPath);
        _a = MakePlayer("A");
        _b = MakePlayer("B");
    }

    private AudioStreamPlayer MakePlayer(string name)
    {
        var player = GetNodeOrNull<AudioStreamPlayer>(name) ?? new AudioStreamPlayer { Name = name };
        player.ProcessMode = ProcessModeEnum.Always;
        if (player.GetParent() == null) AddChild(player);
        return player;
    }

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.RoomEntered += OnRoomEntered;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.RoomEntered -= OnRoomEntered;
    }

    private void OnRoomEntered(string roomId) => PlayRegion(RegionOf(roomId));

    /// <summary>房间编号下划线前面那段（S1_03 → S1）。</summary>
    public static string RegionOf(string roomId)
    {
        int at = roomId?.IndexOf('_') ?? -1;
        return at > 0 ? roomId[..at] : roomId ?? "";
    }

    /// <summary>切到这个区域的曲子；和现在放的是同一首就什么都不做。</summary>
    public void PlayRegion(string region)
    {
        AudioStream stream = Table?.StreamFor(region);
        if (stream == CurrentStream && (stream == null || _active is { Playing: true }))
        {
            CurrentRegion = stream == null ? "" : region;
            return;
        }
        CurrentRegion = stream == null ? "" : region;
        CurrentStream = stream;
        float fade = Mathf.Max(Table?.FadeSeconds ?? 0f, 0.01f);
        float volume = Table?.VolumeDb ?? 0f;
        AudioStreamPlayer old = _active;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        if (old != null && old.Playing)
            _tween.TweenProperty(old, "volume_db", -60f, fade);
        if (stream != null)
        {
            AudioStreamPlayer next = old == _a ? _b : _a;
            next.Stream = stream;
            next.VolumeDb = -60f;
            next.Play();
            StartCount++;
            _tween.TweenProperty(next, "volume_db", volume, fade);
            _active = next;
        }
        else _active = null;
        if (old != null)
            _tween.Chain().TweenCallback(Callable.From(() => { if (old != _active) old.Stop(); }));
    }

    /// <summary>正在放（没有在淡出）的那条播放器上的曲子。</summary>
    public bool IsPlaying(AudioStream stream) => _active != null && _active.Playing && _active.Stream == stream;
}
