using CommunityToolkit.Mvvm.ComponentModel;

namespace Miseria.App.ViewModels;

public class ChatBubble
{
    public ChatBubble(string text, DateTimeOffset timestamp, bool isMine)
    {
        Text = text;
        Timestamp = timestamp;
        IsMine = isMine;
    }

    public string Text { get; }

    public DateTimeOffset Timestamp { get; }

    public bool IsMine { get; }

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");
}
