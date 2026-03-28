using Avalonia.Controls;
using ShokzMP3Sync.ViewModels;

namespace ShokzMP3Sync.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;
        vm.Initialize();

        Closed += (_, _) => vm.Cleanup();
    }
}
