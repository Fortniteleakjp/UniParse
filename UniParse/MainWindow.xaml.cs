using System;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using UniParse.Controls;
using UniParse.Models;
using UniParse.ViewModels;

namespace UniParse;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly PPtrLinkElementGenerator _pptrLinks = new();
    private readonly ToolTip _jsonToolTip = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        TrySetIcon();

        // AvalonEdit's Text is not a dependency property, so we push it from the view model manually.
        JsonEditor.SyntaxHighlighting = JsonHighlighting.Definition;
        JsonEditor.Options.HighlightCurrentLine = true;
        StyleJsonEditor();

        // Make Unity asset references (PPtr) in the JSON clickable -> jump to that asset.
        JsonEditor.TextArea.TextView.ElementGenerators.Add(_pptrLinks);
        _pptrLinks.Navigate += (fileId, pathId) => _viewModel.NavigateToPPtr(fileId, pathId);
        JsonEditor.MouseHover += OnJsonMouseHover;
        JsonEditor.MouseHoverStopped += OnJsonMouseHoverStopped;

        // Scroll the selected tree node into view when selection changes (incl. via reference navigation).
        Tree.AddHandler(TreeViewItem.SelectedEvent, new RoutedEventHandler(OnTreeItemSelected));
        _viewModel.NavigateToNodeRequested += OnNavigateToNode;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
    }

    /// <summary>Applies dark-theme-friendly selection / current-line / line-number colours to the JSON editor.</summary>
    private void StyleJsonEditor()
    {
        JsonEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x4C, 0x8D, 0xFF));
        JsonEditor.TextArea.SelectionBorder = null;
        JsonEditor.TextArea.SelectionForeground = null;
        JsonEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        JsonEditor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        JsonEditor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6F, 0x78));
    }

    private void OnTreeItemSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item)
            item.BringIntoView();
    }

    /// <summary>
    /// Expands the path to a node, selects it and scrolls it into view. Walks the container
    /// generators with forced layout so it works even when the branch was collapsed (its
    /// containers are realized on demand), and re-scrolls even if the node was already selected.
    /// </summary>
    private void OnNavigateToNode(AssetTreeNode node)
        => Dispatcher.BeginInvoke(new Action(() => RevealNode(node)), System.Windows.Threading.DispatcherPriority.Background);

    private void RevealNode(AssetTreeNode target)
    {
        // Build the path from the root down to the target.
        var path = new System.Collections.Generic.List<AssetTreeNode>();
        for (AssetTreeNode? n = target; n is not null; n = n.Parent)
            path.Insert(0, n);

        ItemsControl parent = Tree;
        parent.UpdateLayout(); // ensure root containers exist (e.g. just after a tree swap)

        for (int i = 0; i < path.Count; i++)
        {
            TreeViewItem? item = parent.ItemContainerGenerator.ContainerFromItem(path[i]) as TreeViewItem;
            if (item is null)
            {
                parent.UpdateLayout();
                item = parent.ItemContainerGenerator.ContainerFromItem(path[i]) as TreeViewItem;
                if (item is null)
                    return; // container could not be realized; give up quietly
            }

            if (i == path.Count - 1)
            {
                item.IsSelected = true;
                item.BringIntoView();
            }
            else
            {
                item.IsExpanded = true;
                item.UpdateLayout(); // realize this level's children before descending
                parent = item;
            }
        }
    }

    // ===== JSON reference (PPtr) hover tooltip =====
    private void OnJsonMouseHover(object sender, MouseEventArgs e)
    {
        TextViewPosition? position = JsonEditor.GetPositionFromPoint(e.GetPosition(JsonEditor));
        if (position is null)
            return;

        TextViewPosition pos = position.Value;
        DocumentLine line = JsonEditor.Document.GetLineByNumber(pos.Line);
        string text = JsonEditor.Document.GetText(line.Offset, line.Length);
        int column = pos.Column - 1; // Column is 1-based.

        foreach (Match m in PPtrLinkElementGenerator.PPtrPattern.Matches(text))
        {
            if (column < m.Index || column >= m.Index + m.Length)
                continue;
            if (!long.TryParse(m.Groups[2].Value, out long pathId) || pathId == 0)
                return;
            int fileId = int.TryParse(m.Groups[1].Value, out int f) ? f : 0;

            string? description = _viewModel.DescribePPtr(fileId, pathId);
            if (!string.IsNullOrEmpty(description))
            {
                _jsonToolTip.Content = description;
                _jsonToolTip.PlacementTarget = JsonEditor;
                _jsonToolTip.IsOpen = true;
                e.Handled = true;
            }
            return;
        }
    }

    private void OnJsonMouseHoverStopped(object sender, MouseEventArgs e) => _jsonToolTip.IsOpen = false;

    private void TrySetIcon()
    {
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/UniParse.png", UriKind.Absolute));
        }
        catch { /* the window icon is optional; never let it crash startup */ }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.JsonText))
        {
            JsonEditor.Text = _viewModel.JsonText ?? string.Empty;
        }
        else if (e.PropertyName == nameof(MainViewModel.VideoSource))
        {
            if (_viewModel.VideoSource is { } uri)
            {
                VideoPlayer.Source = uri;
                VideoPlayer.Position = TimeSpan.Zero;
                VideoPlayer.Play();
            }
            else
            {
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
            }
        }
    }

    private void OnVideoPlay(object sender, RoutedEventArgs e) => VideoPlayer.Play();

    private void OnVideoPause(object sender, RoutedEventArgs e) => VideoPlayer.Pause();

    private void OnVideoStop(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Stop();
        VideoPlayer.Position = TimeSpan.Zero;
    }

    private void OnVideoEnded(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Position = TimeSpan.Zero;
        VideoPlayer.Pause();
    }

    private void OnVideoFailed(object sender, ExceptionRoutedEventArgs e) => _viewModel.OnVideoPlaybackFailed();

    /// <summary>If a path was passed on the command line, open it automatically once the window is shown.</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
#if !DEBUG
        // Check for a newer release in the background (release builds only).
        _ = _viewModel.CheckForUpdatesAsync(silent: true);
#endif

        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            string path = args[1];
            if (File.Exists(path) || Directory.Exists(path))
                await _viewModel.OpenPathsAsync(new[] { path });
        }
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectedNode = e.NewValue as AssetTreeNode;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.ApplyFilterCommand.CanExecute(null))
            _viewModel.ApplyFilterCommand.Execute(null);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            await _viewModel.OpenPathsAsync(paths);
        }
    }
}
