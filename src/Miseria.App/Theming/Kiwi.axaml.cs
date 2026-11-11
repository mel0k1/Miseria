using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Miseria.App.Theming;

public partial class Kiwi : ResourceDictionary
{
    public Kiwi()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
