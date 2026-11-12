using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class TradesViewModel : ObservableObject
{
    private readonly TradeService _trades;
    private readonly ItemIconCache _icons;
    private readonly FriendsViewModel _friends;
    private bool _loaded;
    private int _busy;

    public ObservableCollection<TradeOfferItem> Offers { get; } = [];

    [ObservableProperty]
    private bool _isIncoming;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _actionMessage = string.Empty;

    public bool HasOffers => Offers.Count > 0;

    public bool IsEmpty => !IsBusy && !HasOffers;

    public string EmptyText => IsIncoming
        ? "Входящих предложений нет. Обмены от друзей появятся здесь."
        : "Отправленных предложений нет. Предложите другу обмен через профиль в Steam.";

    public TradesViewModel(TradeService trades, ItemIconCache icons, FriendsViewModel friends)
    {
        _trades = trades;
        _icons = icons;
        _friends = friends;

        _isIncoming = true;

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

    partial void OnIsIncomingChanged(bool value) => _ = RefreshAsync();
}
