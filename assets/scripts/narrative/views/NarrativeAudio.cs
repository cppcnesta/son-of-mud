using System.Collections.Generic;
using Godot;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Views;

/// <summary>剧情音效：assets/audio/sfx/sfx_narrative_&lt;名字&gt;.wav 或 .ogg。没交付的音效每个名字只警告一次。</summary>
[GlobalClass]
public partial class NarrativeAudio : Node
{
    [Export] public NarrativeSettings Settings { get; set; }

    public string LastPlayed { get; private set; } = "";

    private AudioStreamPlayer _player;
    private readonly HashSet<string> _missing = new();

    public override void _Ready()
    {
        Settings ??= new NarrativeSettings();
        _player = new AudioStreamPlayer { Name = "Sfx" };
        AddChild(_player);
    }

    public void PlaySfx(string name)
    {
        AudioStream stream = null;
        foreach (string extension in new[] { ".wav", ".ogg" })
        {
            string path = $"{Settings.SfxDirectory}/sfx_narrative_{name}{extension}";
            if (!ResourceLoader.Exists(path)) continue;
            stream = GD.Load<AudioStream>(path);
            break;
        }
        if (stream == null)
        {
            if (_missing.Add(name)) NarrativeLog.Warn($"[剧情] 音效 sfx_narrative_{name} 还没有交付，已跳过");
            return;
        }
        LastPlayed = name;
        _player.Stream = stream;
        _player.Play();
    }
}
