using System;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Views;

/// <summary>
/// 一次动画 CG 的播放（[CG id 秒数] 遇到带动画场景的 CG 时由 CgCommand 创建，登记成 VNContext.ActiveSegment）。
/// Begin：在 CG 层上放出这个 CG 的动画场景并从头播，总时间轴播完时调用一次 onFinished；场景缺东西就抛错（由 CgCommand 当作播放失败收尾）。
/// Cancel：停掉总时间轴、马上释放自己放出的 CG 实例，之后不再回调。只动自己的实例，不碰 CG 层上别的东西。
/// </summary>
public sealed class CgPlayback : IGameplaySegment
{
    private readonly CgLayer _layer;
    private readonly CgData _data;
    private readonly float _fade;
    private bool _cancelled;

    public CgPlayback(CgLayer layer, CgData data, float fade)
    {
        _layer = layer;
        _data = data;
        _fade = fade;
    }

    public string Type => "cg";
    /// <summary>放出来的 CG 实例（Begin 之后才有）。</summary>
    public AnimatedCg Instance { get; private set; }
    public bool Cancelled => _cancelled;

    /// <summary>总时长（秒），Begin 之后才知道。</summary>
    public double Duration => Instance?.Duration ?? 0;

    public void Begin(string arg, Action<string> onFinished)
    {
        if (_layer == null) throw new InvalidOperationException("没有 CG 层，放不了动画 CG " + _data?.Id);
        Instance = _layer.ShowAnimated(_data, _fade) ?? throw new InvalidOperationException($"CG {_data?.Id} 的动画场景放不出来（根节点要挂 AnimatedCg）");
        string error = Instance.Begin(() =>
        {
            if (_cancelled) return;
            onFinished?.Invoke("done");
        });
        if (error != null) throw new InvalidOperationException(error);
    }

    public void Cancel()
    {
        if (_cancelled) return;
        _cancelled = true;
        if (Instance == null) return;
        Instance.StopPlayback();
        _layer?.RemoveAnimated(Instance);
    }
}
