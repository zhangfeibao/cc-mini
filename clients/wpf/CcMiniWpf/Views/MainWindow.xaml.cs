using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CcMiniWpf.ViewModels;

namespace CcMiniWpf.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Messages.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(() => ChatScroller.ScrollToEnd());

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.IsProcessing) && !ViewModel.IsProcessing)
                Dispatcher.BeginInvoke(() => InputBox.Focus());
        };

        await ViewModel.InitializeAsync();
        InputBox.Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.Cleanup();
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ViewModel.HandleKeyDown(e);
    }

    private void CommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel.ApplySelectedCommand();
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }
}
