namespace SonOfMud.Narrative.Runtime;

/// <summary>执行器把台词送到哪里：对话框（暂停游戏、等推进键）或字幕条（不暂停、自动消失）。</summary>
public enum RunnerMode
{
    Dialog,
    Subtitle,
}
