using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnityFModel.Models;
using UnityFModel.ViewModels;

namespace UnityFModel;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // AvalonEdit's Text is not a dependency property, so we push it from the view model manually.
        JsonEditor.SyntaxHighlighting = JsonHighlighting.Definition;
        JsonEditor.Options.HighlightCurrentLine = true;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
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
