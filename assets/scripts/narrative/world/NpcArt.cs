using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.World;

/// <summary>
/// NPC 的序列帧（挂在 NpcTalk 之类的 NPC 节点下面）：从 <see cref="ArtDirectory"/> 找 <c>spr_npc_&lt;ArtId&gt;_&lt;动作&gt;_&lt;列&gt;x&lt;行&gt;.png</c>，
/// 切成 idle（循环）、walk（循环）、show_liver（播一次）等动画；有图时藏起上一层的 Placeholder 色块，没有图时什么都不做。
/// 显示比例和主角、敌人一样（源图 4 像素 = 游戏 1 像素），脚踩在 NPC 节点的原点。美术图朝右画。
///
/// 同时是舞台效果 <c>[效果 &lt;ArtId&gt; 动作]</c>（剧本里用）：
/// - <c>出示泥肝</c>（show_liver）：转向主角，播一次出示动画，播完回到待机；不等它播完，台词照常往下走。
/// - <c>跟上</c>（follow）：转向主角，走到主角身边（隔 <see cref="FollowGap"/> 像素，最多走 <see cref="MaxFollow"/> 像素）再待机；走完台词才继续。
/// - <c>待机</c>（idle）：回到待机；<c>看向</c>（face）：转向主角。
/// </summary>
[GlobalClass]
public partial class NpcArt : Node2D, IStageEffect, IBlockingStageEffect
{
	public const string ArtDirectory = "res://assets/art/npc";
	public const string Idle = "idle";
	public const string Walk = "walk";
	public const string ShowLiver = "show_liver";
	public const float BaseFps = 12f;

	/// <summary>动画名 → 是否循环。</summary>
	private static readonly (string name, bool loop)[] Actions = { (Idle, true), (Walk, true), (ShowLiver, false) };

	/// <summary>同一个 NPC 的帧只切一次（进出房间不重复切图）。</summary>
	private static readonly Dictionary<string, SpriteFrames> FramesCache = new();
	private static readonly Dictionary<string, Dictionary<string, float>> GroundCache = new();

	/// <summary>图名里的 NPC 名，也是舞台效果的名字（<c>[效果 igi 出示泥肝]</c>）。</summary>
	[Export] public string ArtId { get; set; } = "";
	/// <summary>一开始朝左（美术图朝右画，朝左时水平翻转）。</summary>
	[Export] public bool FaceLeft { get; set; }
	/// <summary>走路速度（像素 / 秒）。</summary>
	[Export] public float WalkSpeed { get; set; } = 90f;
	/// <summary>「跟上」时停在离主角多远的地方。</summary>
	[Export] public float FollowGap { get; set; } = 48f;
	/// <summary>「跟上」最多走多远（房间里不跨台阶）。</summary>
	[Export] public float MaxFollow { get; set; } = 200f;

	public string Id => ArtId;
	public AnimatedSprite2D Sprite { get; private set; }
	public bool Walking => _walkTarget.HasValue;
	public string Current => Sprite?.Animation.ToString() ?? "";
	public bool FacingLeft => Sprite?.FlipH ?? FaceLeft;

	private float? _walkTarget;
	private Action _walkDone;
	private Dictionary<string, float> _ground;

	public override void _EnterTree()
	{
		if (!string.IsNullOrEmpty(ArtId)) VNRegistry.Register((IStageEffect)this);
	}

	public override void _ExitTree()
	{
		if (!string.IsNullOrEmpty(ArtId)) VNRegistry.Unregister((IStageEffect)this);
		FinishWalk();
	}

	public override void _Ready()
	{
		SpriteFrames frames = FramesFor(ArtId, out _ground);
		if (frames == null || !frames.HasAnimation(Idle)) return;
		if (GetParent()?.GetNodeOrNull<CanvasItem>("Placeholder") is { } placeholder) placeholder.Visible = false;
		Sprite = new AnimatedSprite2D { Name = "Sprite", SpriteFrames = frames, FlipH = FaceLeft };
		Sprite.AnimationFinished += OnAnimationFinished;
		AddChild(Sprite);
		PlayAnim(Idle);
	}

	/// <summary>切好 spr_npc_&lt;id&gt;_* 的动画；一张都没有返回 null。ground：每段动画最低不透明像素的行（脚底）。</summary>
	public static SpriteFrames FramesFor(string id, out Dictionary<string, float> ground)
	{
		ground = null;
		if (string.IsNullOrEmpty(id)) return null;
		if (FramesCache.TryGetValue(id, out SpriteFrames cached))
		{
			ground = GroundCache[id];
			return cached;
		}
		var frames = new SpriteFrames();
		frames.RemoveAnimation("default");
		ground = new Dictionary<string, float>();
		foreach (var (name, loop) in Actions)
		{
			Texture2D art = SpriteStrip.Find(ArtDirectory, $"spr_npc_{id}_{name}", "NPC 美术图", out int cols, out int rows);
			if (art == null || cols <= 0 || rows <= 0) continue;
			if (!SpriteStrip.AddEvenGrid(frames, name, art, cols, rows, loop, BaseFps)) continue;
			ground[name] = EnemyAnimator.ContentBottom(frames, name);
		}
		if (frames.GetAnimationNames().Length == 0) return null;
		FramesCache[id] = frames;
		GroundCache[id] = ground;
		return frames;
	}

	private void PlayAnim(string name)
	{
		if (Sprite == null || !Sprite.SpriteFrames.HasAnimation(name)) name = Idle;
		if (Sprite == null || !Sprite.SpriteFrames.HasAnimation(name)) return;
		EnemyAnimator.Layout(Sprite, SpriteStrip.FrameHeight(Sprite.SpriteFrames, name), 1f, _ground != null && _ground.TryGetValue(name, out float g) ? g : 0f);
		Sprite.Play(name);
	}

	private void OnAnimationFinished()
	{
		if (Current == ShowLiver) PlayAnim(Walking ? Walk : Idle);
	}

	private Node2D Player => GetTree()?.GetFirstNodeInGroup("player") as Node2D;
	/// <summary>被移动的是 NPC 节点本身（互动区域跟着走）。</summary>
	private Node2D Body => GetParent() as Node2D ?? this;

	/// <summary>转向主角（主角不在场时不动）。</summary>
	public void FacePlayer()
	{
		if (Sprite == null || Player is not { } player) return;
		float dx = player.GlobalPosition.X - Body.GlobalPosition.X;
		if (Mathf.Abs(dx) > 1f) Sprite.FlipH = dx < 0f;
	}

	public void Play(string[] args) => Play(args, null);

	public void Play(string[] args, Action done)
	{
		string action = args != null && args.Length > 0 ? args[0] : "";
		switch (action)
		{
			case "出示泥肝":
			case ShowLiver:
				FacePlayer();
				PlayAnim(ShowLiver);
				break;
			case "跟上":
			case "follow":
				if (StartFollow(done)) return;
				break;
			case "看向":
			case "face":
				FacePlayer();
				break;
			case "待机":
			case Idle:
				PlayAnim(Idle);
				break;
			default:
				NarrativeLog.Warn($"[剧情] 舞台效果「{ArtId}」不认识动作「{action}」（可用：出示泥肝、跟上、看向、待机）。");
				break;
		}
		done?.Invoke();
	}

	/// <summary>开始往主角那边走；已经够近（或没有图 / 没有主角）返回 false。</summary>
	private bool StartFollow(Action done)
	{
		FacePlayer();
		if (Sprite == null || Player is not { } player) return false;
		float from = Body.GlobalPosition.X, dx = player.GlobalPosition.X - from;
		float distance = Mathf.Min(Mathf.Abs(dx) - FollowGap, MaxFollow);
		if (distance <= 1f) return false;
		FinishWalk();
		_walkTarget = from + Mathf.Sign(dx) * distance;
		_walkDone = done;
		PlayAnim(Walk);
		return true;
	}

	public override void _Process(double delta)
	{
		if (_walkTarget is not float target) return;
		Vector2 pos = Body.GlobalPosition;
		float step = WalkSpeed * (float)delta;
		if (Mathf.Abs(target - pos.X) <= step)
		{
			Body.GlobalPosition = new Vector2(target, pos.Y);
			FinishWalk();
			FacePlayer();
			PlayAnim(Idle);
			return;
		}
		Body.GlobalPosition = new Vector2(pos.X + Mathf.Sign(target - pos.X) * step, pos.Y);
	}

	private void FinishWalk()
	{
		_walkTarget = null;
		Action done = _walkDone;
		_walkDone = null;
		done?.Invoke();
	}
}
