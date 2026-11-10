using SonOfMud.Narrative.Data;

namespace SonOfMud.Narrative.Runtime;

/// <summary>显示台词的地方：对话框（DialogBox）或字幕条（SubtitleBar）。“说”“接着说”“等点击”只和它打交道。</summary>
public interface ILineSurface
{
    /// <summary>字还没打完。</summary>
    bool IsTyping { get; }
    /// <summary>玩家在一段台词还没出完时按了推进键：这一段剩下的句子直接出现，不再停顿。</summary>
    bool Rushing { get; }
    /// <summary>开始新的一段：清空，显示名牌（name 为空表示旁白，不显示名牌）、立绘和第一句。</summary>
    void ShowLine(string name, string text, CharacterData character, string expression);
    /// <summary>现在框里是不是这个人的一段话，可以接着往下加。</summary>
    bool CanAppend(string name);
    void AppendLine(string text);
    /// <summary>下一句前面空一行。</summary>
    void BreakParagraph();
    void Rush();
    void StopRush();
    void CompleteTyping();
    void Hide();
}
