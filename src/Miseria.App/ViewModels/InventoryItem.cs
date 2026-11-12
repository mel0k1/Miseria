using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class InventoryItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private bool _hasIcon;

    [ObservableProperty]
    private bool _isSelected;

    public InventoryItemSnapshot Snapshot { get; }

    public string Name => Snapshot.Name;

    public string Type => Snapshot.Type;

    public bool HasType => Type.Length > 0;

    public string Description => Snapshot.Description;

    public bool HasDescription => Description.Length > 0;

    public bool Tradable => Snapshot.Tradable;

    public bool Marketable => Snapshot.Marketable;

    public string HashName => Snapshot.MarketHashName;

    public uint AppId => Snapshot.AppId;

    public uint ContextId => Snapshot.ContextId;

    public string AssetId => Snapshot.AssetId;

    public bool CanSell => Marketable && Tradable && HashName.Length > 0;

    public string MarketUrl => HashName.Length == 0
        ? string.Empty
        : $"https://steamcommunity.com/market/listings/{AppId}/{Uri.EscapeDataString(HashName)}";

    public string BadgeText => Marketable
        ? (Tradable ? "Можно продать и обменять" : "Только продажа")
        : (Tradable ? "Только обмен" : "Не продаётся");

    public InventoryItem(InventoryItemSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public void SetIcon(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Icon = bitmap;
        HasIcon = true;
    }
}
