using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class FriendsView : UserControl
{
    private FriendsViewModel? _viewModel;

    public FriendsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.Messages.CollectionChanged -= OnMessagesChanged;

        _viewModel = DataContext as FriendsViewModel;

        if (_viewModel != null)
            _viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => ChatScroll.ScrollToEnd());

    private void OnFriendTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid { DataContext: FriendItem item } && DataContext is FriendsViewModel vm)
            vm.OpenChat(item);
    }

    private void OnChatKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FriendsViewModel vm)
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
