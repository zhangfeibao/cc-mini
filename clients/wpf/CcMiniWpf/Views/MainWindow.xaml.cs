using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CcMiniWpf.Models;
using CcMiniWpf.ViewModels;

namespace CcMiniWpf.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private bool _isSyncingScroll;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 左侧消息列表变化时，双侧滚动到底部
        ViewModel.Messages.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                ChatScroller.ScrollToEnd();
                ToolScroller.ScrollToEnd();
            });

        // 右侧工具列表变化时，滚动到底部
        ViewModel.AllToolCalls.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(() => ToolScroller.ScrollToEnd());

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

    // ── 同步滚动 ──

    private static bool IsAtBottom(ScrollViewer sv)
        => sv.VerticalOffset >= sv.ScrollableHeight - 20;

    private void ChatScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 内容增长且在底部时，只确保双方都在底部
        if (e.ExtentHeightChange > 0 && IsAtBottom(ChatScroller))
        {
            ToolScroller.ScrollToEnd();
            return;
        }

        if (_isSyncingScroll || ChatScroller.ScrollableHeight <= 0) return;

        _isSyncingScroll = true;
        try
        {
            double ratio = ChatScroller.VerticalOffset / ChatScroller.ScrollableHeight;
            ToolScroller.ScrollToVerticalOffset(ratio * ToolScroller.ScrollableHeight);
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }

    private void ToolScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || ToolScroller.ScrollableHeight <= 0) return;

        _isSyncingScroll = true;
        try
        {
            double ratio = ToolScroller.VerticalOffset / ToolScroller.ScrollableHeight;
            ChatScroller.ScrollToVerticalOffset(ratio * ChatScroller.ScrollableHeight);
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }

    // ── 点击定位 ──

    /// <summary>右侧工具卡片点击 -> 左侧滚动到对应消息。</summary>
    private void ToolCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolCallInfo toolCall
            && toolCall.OwnerMessage is ChatMessage msg)
        {
            var container = ChatItemsControl.ItemContainerGenerator
                .ContainerFromItem(msg) as FrameworkElement;
            if (container is not null)
            {
                container.BringIntoView();
                HighlightToolCall(toolCall);
            }
        }
    }

    /// <summary>左侧锚点标记点击 -> 右侧滚动到对应工具卡片。</summary>
    private void ToolAnchor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolCallInfo toolCall)
        {
            var container = ToolItemsControl.ItemContainerGenerator
                .ContainerFromItem(toolCall) as FrameworkElement;
            if (container is not null)
            {
                container.BringIntoView();
                HighlightToolCall(toolCall);
            }
        }
    }

    /// <summary>短暂高亮工具卡片（1.5秒）。</summary>
    private static void HighlightToolCall(ToolCallInfo toolCall)
    {
        toolCall.IsHighlighted = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            toolCall.IsHighlighted = false;
            timer.Stop();
        };
        timer.Start();
    }
}
