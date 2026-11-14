using Godot;

namespace SonOfMud.Core;

/// <summary>可按互动键交互的对象：存档点、门、NPC、泥板。actor 一般是玩家，但这里只当 Node2D 用。</summary>
public interface IInteractable
{
    /// <summary>提示文字前加这个记号表示“只显示文字、不显示按键”（锁着的门的提示）。</summary>
    public const string NoKeyMark = "​";

    string Prompt { get; }
    bool CanInteract(Node2D actor);
    void Interact(Node2D actor);

    /// <summary>提示里显示按键（“E 拾取泥板”）；锁着的门只显示文字。</summary>
    bool ShowsKey => true;
}
