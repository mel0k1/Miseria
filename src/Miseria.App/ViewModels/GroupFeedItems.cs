namespace Miseria.App.ViewModels;

public sealed record AnnouncementItem(string Title, string Url, string DateText, string Text);

public sealed record DiscussionItem(string Title, string Url, string Author, string MetaText);
