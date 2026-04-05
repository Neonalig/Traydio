using Avalonia.Controls;
using Traydio.Common;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(MainWindowViewModel))]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
