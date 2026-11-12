using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class TradesViewModel : ObservableObject
{
    private static readonly (uint AppId, string Name)[] TradeApps =
    [
        (730, "Counter-Strike 2"),
        (570, "Dota 2"),
        (440, "Team Fortress 2"),
        (252490, "Rust"),
        (753, "Steam")
    ];

    private readonly TradeService _trades;
    private readonly InventoryService _inventory;
    private readonly ItemIconCache _icons;
    private readonly FriendsViewModel _friends;
    private bool _loaded;
    private int _busy;

    public ObservableCollection<TradeOfferItem> Offers { get; } = [];

    public ObservableCollection<FriendItem> Friends => _friends.Friends;

    public ObservableCollection<AppChoice> Apps { get; } = [];

    public ObservableCollection<InventoryItem> OwnItems { get; } = [];

    public ObservableCollection<InventoryItem> TheirItems { get; } = [];

    [ObservableProperty]
    private bool _isIncoming;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    [ObservableProperty]
    private FriendItem? _selectedFriend;

    [ObservableProperty]
    private AppChoice? _ownApp;

    [ObservableProperty]
    private AppChoice? _theirApp;

    [ObservableProperty]
    private string _offerMessage = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private bool _isLoadingItems;

    [ObservableProperty]
    private string _theirStatusText = string.Empty;

    public bool HasOffers => Offers.Count > 0;

    public bool IsEmpty => !IsBusy && !HasOffers;

    public bool CanSendOffer => !IsSending && SelectedFriend != null
        && (OwnItems.Any(i => i.IsSelected) || TheirItems.Any(i => i.IsSelected));

    public string SelectionSummary
    {
        get
        {
            var give = OwnItems.Count(i => i.IsSelected);
            var receive = TheirItems.Count(i => i.IsSelected);
            return $"Отдаёте: {give} · Получаете: {receive}";
        }
    }

    public string EmptyText => IsIncoming
        ? "Входящих предложений нет. Обмены от друзей появятся здесь."
        : "Отправленных предложений нет. Создайте предложение кнопкой выше.";

    public TradesViewModel(TradeService trades, InventoryService inventory, ItemIconCache icons, FriendsViewModel friends)
    {
        _trades = trades;
        _inventory = inventory;
        _icons = icons;
        _friends = friends;

        _isIncoming = true;

        foreach (var app in TradeApps)
            Apps.Add(new AppChoice(app.AppId, app.Name));

        _ownApp = Apps[0];
        _theirApp = Apps[0];

        Offers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasOffers));
            OnPropertyChanged(nameof(IsEmpty));
        };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsBusy))
                OnPropertyChanged(nameof(IsEmpty));
        };
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;

        _loaded = true;
        await RefreshAsync();
    }

    public void SuppressAutoLoad() => _loaded = true;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        IsBusy = true;

        try
        {
            var result = await _trades.GetActiveAsync(cancellationToken);
            var direction = IsIncoming ? TradeDirection.Incoming : TradeDirection.Sent;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Offers.Clear();

                foreach (var snapshot in result.Offers.Where(o => o.Direction == direction))
                {
                    var item = new TradeOfferItem(snapshot, _icons, result.Descriptions);
                    item.SetPartner(_friends.Friends.FirstOrDefault(f => f.SteamId == item.PartnerId));
                    Offers.Add(item);
                }

                StatusText = $"Активных обменов: {result.Offers.Count}";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Не удалось загрузить обмены");
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Сессия недоступна для загрузки обменов");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    [RelayCommand]
    private async Task AcceptAsync(TradeOfferItem? offer)
    {
        if (offer == null || offer.IsBusy)
            return;

        offer.IsBusy = true;

        try
        {
            var result = await _trades.AcceptAsync(offer.OfferId, offer.PartnerId);

            if (result.NeedsMobileConfirmation)
            {
                ActionMessage = $"Обмен {offer.OfferId} принят, подтвердите его в мобильном приложении Steam.";
                await RefreshAsync();
            }
            else if (result.Accepted)
            {
                ActionMessage = $"Обмен {offer.OfferId} принят.";
                await RefreshAsync();
            }
            else
            {
                ActionMessage = $"Не удалось принять обмен {offer.OfferId}.";
            }
        }
        catch (HttpRequestException)
        {
            ActionMessage = "Steam отклонил запрос. Проверьте соединение и попробуйте снова.";
        }
        finally
        {
            offer.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeclineAsync(TradeOfferItem? offer)
    {
        if (offer == null || offer.IsBusy)
            return;

        offer.IsBusy = true;

        try
        {
            await _trades.DeclineAsync(offer.OfferId);
            ActionMessage = $"Обмен {offer.OfferId} отклонён.";
            await RefreshAsync();
        }
        catch (HttpRequestException)
        {
            ActionMessage = "Steam отклонил запрос. Проверьте соединение и попробуйте снова.";
        }
        finally
        {
            offer.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelAsync(TradeOfferItem? offer)
    {
        if (offer == null || offer.IsBusy)
            return;

        offer.IsBusy = true;

        try
        {
            await _trades.CancelAsync(offer.OfferId);
            ActionMessage = $"Предложение {offer.OfferId} отменено.";
            await RefreshAsync();
        }
        catch (HttpRequestException)
        {
            ActionMessage = "Steam отклонил запрос. Проверьте соединение и попробуйте снова.";
        }
        finally
        {
            offer.IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartCreate()
    {
        IsCreating = true;
        OfferMessage = string.Empty;
        TheirStatusText = string.Empty;
        _ = LoadOwnItemsAsync();
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreating = false;
        ClearSelection();
    }

    [RelayCommand]
    private async Task SendOfferAsync(CancellationToken cancellationToken = default)
    {
        var friend = SelectedFriend;

        if (friend == null || IsSending)
        {
            ActionMessage = "Выберите друга для обмена";
            return;
        }

        var give = OwnItems.Where(i => i.IsSelected)
            .Select(i => new TradeAssetSnapshot(i.AppId, i.ContextId, i.Snapshot.ClassId, i.Snapshot.InstanceId, long.Parse(i.AssetId, CultureInfo.InvariantCulture)))
            .ToList();
        var receive = TheirItems.Where(i => i.IsSelected)
            .Select(i => new TradeAssetSnapshot(i.AppId, i.ContextId, i.Snapshot.ClassId, i.Snapshot.InstanceId, long.Parse(i.AssetId, CultureInfo.InvariantCulture)))
            .ToList();

        if (give.Count == 0 && receive.Count == 0)
        {
            TheirStatusText = "Выберите хотя бы один предмет с любой стороны";
            return;
        }

        IsSending = true;
        TheirStatusText = "Отправка предложения…";

        try
        {
            var result = await _trades.CreateAsync(friend.SteamId, OfferMessage, give, receive, cancellationToken);

            if (result.Created)
            {
                TheirStatusText = string.Empty;
                ActionMessage = result.NeedsMobileConfirmation
                    ? $"Предложение {result.OfferId} отправлено, подтвердите его в мобильном приложении Steam."
                    : $"Предложение {result.OfferId} отправлено.";
                IsCreating = false;
                ClearSelection();
                await RefreshAsync(cancellationToken);
            }
            else
            {
                TheirStatusText = result.Error;
            }
        }
        catch (HttpRequestException)
        {
            TheirStatusText = "Steam отклонил запрос. Проверьте соединение и попробуйте снова.";
        }
        catch (InvalidOperationException)
        {
            TheirStatusText = "Сессия недоступна для отправки предложения";
        }
        finally
        {
            IsSending = false;
            OnPropertyChanged(nameof(CanSendOffer));
        }
    }

    private async Task LoadOwnItemsAsync(CancellationToken cancellationToken = default)
    {
        var app = OwnApp?.AppId ?? 0;

        if (app == 0)
            return;

        IsLoadingItems = true;
        OnPropertyChanged(nameof(CanSendOffer));

        try
        {
            var result = await _inventory.GetOwnAsync(app, InventoryService.ResolveContext(app), cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearSelection();
                OwnItems.Clear();

                foreach (var snapshot in result.Items)
                    AddOwnTradeItem(new InventoryItem(snapshot));

                if (OwnItems.Count == 0)
                    TheirStatusText = "Ваш инвентарь для выбранной игры пуст или недоступен";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => TheirStatusText = "Не удалось загрузить ваш инвентарь");
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => TheirStatusText = "Сессия недоступна для загрузки инвентаря");
        }
        finally
        {
            IsLoadingItems = false;
        }
    }

    private async Task LoadTheirItemsAsync(CancellationToken cancellationToken = default)
    {
        var friend = SelectedFriend;
        var app = TheirApp?.AppId ?? 0;

        TheirItems.Clear();
        OnPropertyChanged(nameof(CanSendOffer));

        if (friend == null || app == 0)
        {
            TheirStatusText = friend == null ? "Выберите друга, чтобы загрузить его инвентарь" : string.Empty;
            return;
        }

        TheirStatusText = "Загрузка инвентаря друга…";

        try
        {
            var result = await _inventory.GetPartnerAsync(friend.SteamId, app, InventoryService.ResolveContext(app), cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearSelection();
                TheirItems.Clear();

                foreach (var snapshot in result.Items)
                    AddTheirTradeItem(new InventoryItem(snapshot));

                TheirStatusText = TheirItems.Count == 0
                    ? "Инвентарь друга пуст или закрыт настройками приватности"
                    : $"Предметов у друга: {result.Items.Count}";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => TheirStatusText = "Не удалось загрузить инвентарь друга");
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => TheirStatusText = "Сессия недоступна для загрузки инвентаря друга");
        }
    }

    public void AddOwnTradeItem(InventoryItem item)
    {
        item.PropertyChanged += OnTradeItemChanged;
        OwnItems.Add(item);
        _ = LoadIconAsync(item, item.Snapshot.IconUrl);
    }

    public void AddTheirTradeItem(InventoryItem item)
    {
        item.PropertyChanged += OnTradeItemChanged;
        TheirItems.Add(item);
        _ = LoadIconAsync(item, item.Snapshot.IconUrl);
    }

    private void ClearSelection()
    {
        foreach (var item in OwnItems)
            item.IsSelected = false;

        foreach (var item in TheirItems)
            item.IsSelected = false;

        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanSendOffer));
    }

    private void OnTradeItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InventoryItem.IsSelected))
        {
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(CanSendOffer));
        }
    }

    private async Task LoadIconAsync(InventoryItem item, string? iconUrl)
    {
        var bitmap = await _icons.LoadAsync(iconUrl);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetIcon(bitmap));
    }

    partial void OnIsIncomingChanged(bool value) => _ = RefreshAsync();

    partial void OnSelectedFriendChanged(FriendItem? value) => _ = LoadTheirItemsAsync();

    partial void OnOwnAppChanged(AppChoice? value)
    {
        if (IsCreating)
            _ = LoadOwnItemsAsync();
    }

    partial void OnTheirAppChanged(AppChoice? value)
    {
        if (IsCreating && SelectedFriend != null)
            _ = LoadTheirItemsAsync();
    }
}
