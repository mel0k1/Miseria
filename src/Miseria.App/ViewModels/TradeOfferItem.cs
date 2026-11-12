using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class TradeItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private bool _hasIcon;

    public string Name { get; }

    public string Type { get; }

    public bool HasType => Type.Length > 0;

    public TradeItem(string name, string type)
    {
        Name = name;
        Type = type;
    }

    public void SetIcon(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Icon = bitmap;
        HasIcon = true;
    }
}

public partial class TradeOfferItem : ObservableObject
{
    private readonly ItemIconCache _icons;

    [ObservableProperty]
    private string _partnerName;

    [ObservableProperty]
    private Bitmap? _partnerAvatar;

    [ObservableProperty]
    private bool _hasPartnerAvatar;

    [ObservableProperty]
    private string _createdText;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ulong OfferId { get; }

    public ulong PartnerId { get; }

    public TradeDirection Direction { get; }

    public bool IsIncoming => Direction == TradeDirection.Incoming;

    public bool HasMessage { get; }

    public string Message { get; }

    public bool NeedsMobileConfirmation { get; }

    public ObservableCollection<TradeItem> Give { get; } = [];

    public ObservableCollection<TradeItem> Receive { get; } = [];

    public TradeOfferItem(TradeOfferSnapshot snapshot, ItemIconCache icons, IReadOnlyDictionary<(uint AppId, long ClassId, long InstanceId), TradeDescriptionSnapshot> descriptions)
    {
        OfferId = snapshot.OfferId;
        PartnerId = snapshot.PartnerId;
        Direction = snapshot.Direction;
        NeedsMobileConfirmation = snapshot.NeedsMobileConfirmation;
        HasMessage = !string.IsNullOrWhiteSpace(snapshot.Message);
        Message = snapshot.Message ?? string.Empty;
        PartnerName = $"Steam ID {PartnerId}";
        CreatedText = snapshot.CreatedAt.ToLocalTime().ToString("d MMM H:mm", CultureInfo.GetCultureInfo("ru-RU"));
        _icons = icons;

        foreach (var asset in snapshot.ToGive)
            AddItem(Give, asset, descriptions);

        foreach (var asset in snapshot.ToReceive)
            AddItem(Receive, asset, descriptions);
    }

    public void SetPartner(FriendItem? friend)
    {
        if (friend == null)
            return;

        PartnerName = friend.Name;
        PartnerAvatar = friend.Avatar;
        HasPartnerAvatar = friend.HasAvatar;
    }

    private void AddItem(ObservableCollection<TradeItem> collection, TradeAssetSnapshot asset, IReadOnlyDictionary<(uint AppId, long ClassId, long InstanceId), TradeDescriptionSnapshot> descriptions)
    {
        descriptions.TryGetValue((asset.AppId, asset.ClassId, asset.InstanceId), out var description);
        var item = new TradeItem(description?.Name ?? "Предмет", description?.Type ?? string.Empty);
        collection.Add(item);
        _ = LoadIconAsync(item, description?.IconUrl);
    }

    private async Task LoadIconAsync(TradeItem item, string? iconUrl)
    {
        var bitmap = await _icons.LoadAsync(iconUrl);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetIcon(bitmap));
    }
}
