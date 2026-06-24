using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Import.Logging;
using Microsoft.Win32;
using UniParse.Infrastructure;
using UniParse.Models;
using UniParse.Services;

namespace UniParse.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly UnityAssetService _service = new();
    private readonly StringBuilder _log = new();

    private byte[]? _audioWav;
    private SoundPlayer? _soundPlayer;
    private string? _videoTempPath;
    private string? _fontTempDir;

    private readonly UpdateService _updateService = new();
    private UpdateInfo? _pendingUpdate;

    public MainViewModel()
    {
        OpenFileCommand = new RelayCommand(async _ => await OpenFileAsync(), _ => !IsBusy);
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolderAsync(), _ => !IsBusy);
        CloseCommand = new RelayCommand(_ => CloseFile(), _ => IsLoaded && !IsBusy);
        SaveImageCommand = new RelayCommand(_ => SaveImage(), _ => Kind == PreviewKind.Image);
        SaveModelCommand = new RelayCommand(_ => SaveModel(), _ => Kind == PreviewKind.Model);
        SaveAudioCommand = new RelayCommand(_ => SaveAudio(), _ => Kind == PreviewKind.Audio);
        SaveVideoCommand = new RelayCommand(_ => SaveVideo(), _ => Kind == PreviewKind.Video);
        SaveFontCommand = new RelayCommand(_ => SaveFont(), _ => Kind == PreviewKind.Font);
        SaveJsonCommand = new RelayCommand(_ => SaveJson(), _ => !string.IsNullOrEmpty(JsonText));
        SaveTextCommand = new RelayCommand(_ => SaveText(), _ => HasTextContent);
        PlayAudioCommand = new RelayCommand(_ => PlayAudio(), _ => Kind == PreviewKind.Audio && _audioWav is not null);
        StopAudioCommand = new RelayCommand(_ => StopAudio(), _ => Kind == PreviewKind.Audio);
        ApplyFilterCommand = new RelayCommand(_ => ApplyFilter(), _ => IsLoaded);
        ClearFilterCommand = new RelayCommand(_ =>
        {
            SearchText = string.Empty;
            SelectedClassOption = ClassOptions.Count > 0 ? ClassOptions[0] : null;
            ApplyFilter();
        }, _ => IsLoaded);

        UpdateNowCommand = new RelayCommand(async _ => await UpdateNowAsync(), _ => UpdateAvailable && !IsBusy);
        OpenReleaseCommand = new RelayCommand(_ => OpenReleasePage());
        DismissUpdateCommand = new RelayCommand(_ => UpdateAvailable = false);
        CheckUpdateCommand = new RelayCommand(async _ => await CheckForUpdatesAsync(silent: false), _ => !IsBusy);

        Logger.Add(new DelegateLogger(OnAssetRipperLog));
    }

    // ===== Commands =====
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand SaveImageCommand { get; }
    public RelayCommand SaveModelCommand { get; }
    public RelayCommand SaveAudioCommand { get; }
    public RelayCommand SaveVideoCommand { get; }
    public RelayCommand SaveFontCommand { get; }
    public RelayCommand SaveJsonCommand { get; }
    public RelayCommand SaveTextCommand { get; }
    public RelayCommand PlayAudioCommand { get; }
    public RelayCommand StopAudioCommand { get; }
    public RelayCommand ApplyFilterCommand { get; }
    public RelayCommand ClearFilterCommand { get; }
    public RelayCommand UpdateNowCommand { get; }
    public RelayCommand OpenReleaseCommand { get; }
    public RelayCommand DismissUpdateCommand { get; }
    public RelayCommand CheckUpdateCommand { get; }

    // ===== Tree =====
    private ObservableCollection<AssetTreeNode> _rootNodes = new();
    public ObservableCollection<AssetTreeNode> RootNodes
    {
        get => _rootNodes;
        private set => SetField(ref _rootNodes, value);
    }

    // The complete tree, kept so filtering can swap in a small result tree and restore this.
    private ObservableCollection<AssetTreeNode> _fullRootNodes = new();

    private AssetTreeNode? _selectedNode;
    public AssetTreeNode? SelectedNode
    {
        get => _selectedNode;
        set { if (SetField(ref _selectedNode, value)) _ = SelectAssetAsync(value); }
    }

    // ===== Detail header =====
    private string _assetHeader = "アセットを選択してください";
    public string AssetHeader { get => _assetHeader; private set => SetField(ref _assetHeader, value); }

    private string _assetSubHeader = "";
    public string AssetSubHeader { get => _assetSubHeader; private set => SetField(ref _assetSubHeader, value); }

    // ===== Preview kind =====
    private PreviewKind _kind = PreviewKind.None;
    public PreviewKind Kind
    {
        get => _kind;
        private set
        {
            if (SetField(ref _kind, value))
            {
                OnPropertyChanged(nameof(ShowImage));
                OnPropertyChanged(nameof(ShowModel));
                OnPropertyChanged(nameof(ShowAudio));
                OnPropertyChanged(nameof(ShowVideo));
                OnPropertyChanged(nameof(ShowFont));
                OnPropertyChanged(nameof(ShowMessage));
                OnPropertyChanged(nameof(ShowInfoOverlay));
            }
        }
    }

    public bool ShowImage => Kind == PreviewKind.Image;
    public bool ShowModel => Kind == PreviewKind.Model;
    public bool ShowAudio => Kind == PreviewKind.Audio;
    public bool ShowVideo => Kind == PreviewKind.Video;
    public bool ShowFont => Kind == PreviewKind.Font;
    public bool ShowMessage => Kind == PreviewKind.None;
    public bool ShowInfoOverlay => (Kind == PreviewKind.Image || Kind == PreviewKind.Model) && !string.IsNullOrEmpty(PreviewInfo);

    // ===== Preview payloads =====
    private BitmapSource? _previewImage;
    public BitmapSource? PreviewImage { get => _previewImage; private set => SetField(ref _previewImage, value); }

    private MeshGeometry3D? _modelGeometry;
    public MeshGeometry3D? ModelGeometry { get => _modelGeometry; private set => SetField(ref _modelGeometry, value); }

    private string _audioInfo = "";
    public string AudioInfo { get => _audioInfo; private set => SetField(ref _audioInfo, value); }

    private Uri? _videoSource;
    public Uri? VideoSource { get => _videoSource; private set => SetField(ref _videoSource, value); }

    private string _videoInfo = "";
    public string VideoInfo { get => _videoInfo; private set => SetField(ref _videoInfo, value); }

    private string _videoError = "";
    public string VideoError
    {
        get => _videoError;
        private set { if (SetField(ref _videoError, value)) OnPropertyChanged(nameof(HasVideoError)); }
    }
    public bool HasVideoError => !string.IsNullOrEmpty(VideoError);

    private FontFamily? _previewFontFamily;
    public FontFamily? PreviewFontFamily { get => _previewFontFamily; private set => SetField(ref _previewFontFamily, value); }

    private string _fontInfo = "";
    public string FontInfo { get => _fontInfo; private set => SetField(ref _fontInfo, value); }

    private string _previewInfo = "";
    public string PreviewInfo
    {
        get => _previewInfo;
        private set { if (SetField(ref _previewInfo, value)) OnPropertyChanged(nameof(ShowInfoOverlay)); }
    }

    private string _previewMessage = "";
    public string PreviewMessage { get => _previewMessage; private set => SetField(ref _previewMessage, value); }

    // ===== Data tabs =====
    private string _jsonText = "";
    public string JsonText { get => _jsonText; private set => SetField(ref _jsonText, value); }

    private string _textContent = "";
    public string TextContent { get => _textContent; private set => SetField(ref _textContent, value); }

    private bool _hasTextContent;
    public bool HasTextContent { get => _hasTextContent; private set => SetField(ref _hasTextContent, value); }

    private string _textTabHeader = "テキスト";
    public string TextTabHeader { get => _textTabHeader; private set => SetField(ref _textTabHeader, value); }

    // ===== Status / busy =====
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(IsNotBusy)); }
    }
    public bool IsNotBusy => !IsBusy;

    private string _busyMessage = "読み込み中...";
    public string BusyMessage { get => _busyMessage; private set => SetField(ref _busyMessage, value); }

    private string _statusText = "ファイルまたはフォルダを開いてください。";
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

    private string _logText = "";
    public string LogText { get => _logText; private set => SetField(ref _logText, value); }

    private string _projectVersion = "-";
    public string ProjectVersion { get => _projectVersion; private set => SetField(ref _projectVersion, value); }

    private int _assetCount;
    public int AssetCount { get => _assetCount; private set => SetField(ref _assetCount, value); }

    private string _searchText = "";
    public string SearchText { get => _searchText; set => SetField(ref _searchText, value); }

    private ObservableCollection<ClassOption> _classOptions = new();
    public ObservableCollection<ClassOption> ClassOptions
    {
        get => _classOptions;
        private set => SetField(ref _classOptions, value);
    }

    private ClassOption? _selectedClassOption;
    public ClassOption? SelectedClassOption
    {
        get => _selectedClassOption;
        set { if (SetField(ref _selectedClassOption, value)) ApplyFilter(); }
    }

    public bool IsLoaded => _service.IsLoaded;

    // ===== Update =====
    private bool _updateAvailable;
    public bool UpdateAvailable { get => _updateAvailable; private set => SetField(ref _updateAvailable, value); }

    private string _updateBannerText = "";
    public string UpdateBannerText { get => _updateBannerText; private set => SetField(ref _updateBannerText, value); }

    public string AppVersion => "v" + UpdateService.CurrentVersion;

    public async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            UpdateInfo? info = await _updateService.CheckAsync();
            if (info is not null)
            {
                _pendingUpdate = info;
                UpdateBannerText = $"🆕 新しいバージョン {info.TagName} が利用可能です（現在 {AppVersion}）";
                UpdateAvailable = true;
            }
            else if (!silent)
            {
                StatusText = $"最新バージョンです（{AppVersion}）";
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                StatusText = "更新確認に失敗しました: " + ex.Message;
        }
    }

    private async Task UpdateNowAsync()
    {
        if (_pendingUpdate is null)
            return;
        if (string.IsNullOrEmpty(_pendingUpdate.ZipUrl))
        {
            OpenReleasePage();
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"{_pendingUpdate.TagName} をダウンロードして更新します。\nアプリは一旦終了し、更新後に自動で再起動します。続行しますか？",
            "アップデート", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "アップデートをダウンロード中...";
            string zip = await _updateService.DownloadAsync(
                _pendingUpdate,
                new Progress<double>(p => BusyMessage = $"ダウンロード中... {p * 100:F0}%"));
            BusyMessage = "更新を適用して再起動します...";
            _updateService.StartUpdaterAndExit(zip);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            IsBusy = false;
            MessageBox.Show("アップデートに失敗しました: " + ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenReleasePage()
    {
        try
        {
            string url = _pendingUpdate?.HtmlUrl is { Length: > 0 } u ? u : UpdateService.ReleasesPageUrl;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ===== Loading =====
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy || paths.Count == 0)
            return;

        IsBusy = true;
        BusyMessage = "Unity ファイルを読み込み中...";
        _log.Clear();
        LogText = "";
        StatusText = "読み込み中...";
        ResetDetail();
        RootNodes = new ObservableCollection<AssetTreeNode>();
        ClassOptions = new ObservableCollection<ClassOption>();
        _selectedClassOption = null;
        OnPropertyChanged(nameof(SelectedClassOption));

        try
        {
            await Task.Run(() => _service.Load(paths));

            ObservableCollection<AssetTreeNode> roots = await Task.Run(BuildTree);
            _fullRootNodes = roots;
            RootNodes = roots;
            foreach (AssetTreeNode root in roots)
                root.IsExpanded = true;

            ProjectVersion = _service.ProjectVersion;
            AssetCount = _service.GameData?.GameBundle.FetchAssetCollections().Sum(c => c.Count) ?? 0;

            List<ClassOption> classOptions = await Task.Run(BuildClassOptions);
            ClassOptions = new ObservableCollection<ClassOption>(classOptions);
            _selectedClassOption = ClassOptions.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedClassOption));

            StatusText = $"読み込み完了 — {AssetCount:N0} アセット / Unity {ProjectVersion}";
            OnPropertyChanged(nameof(IsLoaded));
            TryLog($"[OK] loaded {AssetCount} assets / Unity {ProjectVersion}");
        }
        catch (Exception ex)
        {
            StatusText = "読み込みに失敗しました: " + ex.Message;
            TryLog("[ERROR] " + ex);
            MessageBox.Show(ex.ToString(), "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenFileAsync()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Unity ファイルを開く",
            Filter = "Unity アセット (*.*)|*.*|AssetBundle (*.bundle;*.unity3d)|*.bundle;*.unity3d|シリアライズファイル (*.assets)|*.assets",
            Multiselect = true,
        };
        if (dialog.ShowDialog() == true)
            await OpenPathsAsync(dialog.FileNames);
    }

    private async Task OpenFolderAsync()
    {
        OpenFolderDialog dialog = new() { Title = "Unity データフォルダを開く" };
        if (dialog.ShowDialog() == true)
            await OpenPathsAsync(new[] { dialog.FolderName });
    }

    private void CloseFile()
    {
        _service.Reset();
        _fullRootNodes = new ObservableCollection<AssetTreeNode>();
        RootNodes = new ObservableCollection<AssetTreeNode>();
        ClassOptions = new ObservableCollection<ClassOption>();
        _selectedClassOption = null;
        OnPropertyChanged(nameof(SelectedClassOption));
        ResetDetail();
        ProjectVersion = "-";
        AssetCount = 0;
        StatusText = "ファイルを閉じました。";
        OnPropertyChanged(nameof(IsLoaded));
    }

    // ===== Tree building (worker thread) =====
    private ObservableCollection<AssetTreeNode> BuildTree()
    {
        ObservableCollection<AssetTreeNode> roots = new();
        GameBundle? bundle = _service.GameData?.GameBundle;
        if (bundle is not null)
            roots.Add(BuildBundle(bundle));
        return roots;
    }

    /// <summary>Builds the class (asset type) filter list with per-class counts.</summary>
    private List<ClassOption> BuildClassOptions()
    {
        Dictionary<string, int> counts = new();
        GameBundle? bundle = _service.GameData?.GameBundle;
        if (bundle is not null)
        {
            foreach (AssetCollection collection in bundle.FetchAssetCollections())
                foreach (IUnityObjectBase asset in collection)
                    counts[asset.ClassName] = counts.GetValueOrDefault(asset.ClassName) + 1;
        }

        List<ClassOption> options = new() { new ClassOption($"すべてのクラス ({counts.Values.Sum():N0})", null) };
        foreach (KeyValuePair<string, int> pair in counts.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            options.Add(new ClassOption($"{pair.Key} ({pair.Value:N0})", pair.Key));
        return options;
    }

    private static AssetTreeNode BuildBundle(Bundle bundle)
    {
        AssetTreeNode node = new(bundle.Name, AssetNodeKind.Bundle);
        foreach (Bundle child in bundle.Bundles)
            node.Children.Add(BuildBundle(child));
        foreach (AssetCollection collection in bundle.Collections)
            node.Children.Add(BuildCollection(collection));
        return node;
    }

    private static AssetTreeNode BuildCollection(AssetCollection collection)
    {
        AssetTreeNode node = new($"{collection.Name}  ({collection.Count})", AssetNodeKind.Collection);

        foreach (IGrouping<string, IUnityObjectBase> group in collection
                     .GroupBy(a => a.ClassName)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            AssetTreeNode groupNode = new($"{group.Key}  ({group.Count()})", AssetNodeKind.TypeGroup);
            foreach (IUnityObjectBase asset in group.OrderBy(a => a.GetBestName(), StringComparer.OrdinalIgnoreCase))
            {
                bool previewable = UnityAssetService.IsPreviewable(asset);
                string toolTip = $"{asset.ClassName}\nPathID: {asset.PathID}\nCollection: {collection.Name}";
                groupNode.Children.Add(new AssetTreeNode(asset.GetBestName(), AssetNodeKind.Asset, asset, toolTip, previewable));
            }
            node.Children.Add(groupNode);
        }
        return node;
    }

    // ===== Selection / preview (worker thread) =====
    private async Task SelectAssetAsync(AssetTreeNode? node)
    {
        ResetDetail();

        if (node is null)
            return;

        if (node.Asset is null)
        {
            AssetHeader = node.Header;
            AssetSubHeader = node.Kind.ToString();
            return;
        }

        IUnityObjectBase asset = node.Asset;
        AssetHeader = asset.GetBestName();
        AssetSubHeader = $"{asset.ClassName}   •   PathID {asset.PathID}   •   {asset.Collection.Name}";

        (PreviewResult preview, string json, string text, UnityAssetService.TextKind textKind) = await Task.Run(() =>
        {
            PreviewResult p;
            try { p = UnityAssetService.BuildPreview(asset); }
            catch (Exception ex) { p = new PreviewResult { Kind = PreviewKind.None, Message = "プレビュー生成中にエラー: " + ex.Message }; }

            string j;
            try { j = UnityAssetService.GetJson(asset); }
            catch (Exception ex) { j = "// JSON を生成できませんでした:\n// " + ex.Message; }

            UnityAssetService.TextKind tk;
            string t;
            try { (tk, t) = UnityAssetService.GetTextContent(asset); }
            catch (Exception ex) { tk = UnityAssetService.TextKind.Text; t = "// テキストを取得できませんでした:\n// " + ex.Message; }
            return (p, j, t, tk);
        });

        if (!ReferenceEquals(SelectedNode, node))
            return;

        JsonText = json;
        TextContent = text;
        HasTextContent = textKind != UnityAssetService.TextKind.None;
        TextTabHeader = textKind switch
        {
            UnityAssetService.TextKind.Shader => "シェーダー",
            UnityAssetService.TextKind.Animation => "アニメーション",
            _ => "テキスト",
        };
        ApplyPreview(preview);
    }

    private void ApplyPreview(PreviewResult preview)
    {
        PreviewInfo = preview.Info;
        PreviewMessage = preview.Message;

        switch (preview.Kind)
        {
            case PreviewKind.Image:
                PreviewImage = LoadBitmap(preview.Png!);
                Kind = PreviewKind.Image;
                break;
            case PreviewKind.Model:
                ModelGeometry = preview.Geometry;
                Kind = PreviewKind.Model;
                break;
            case PreviewKind.Audio:
                _audioWav = preview.Wav;
                AudioInfo = preview.Info;
                Kind = PreviewKind.Audio;
                break;
            case PreviewKind.Video:
                VideoInfo = preview.Info;
                VideoError = "";
                VideoSource = WriteVideoTemp(preview.Video!, preview.VideoExtension);
                Kind = PreviewKind.Video;
                break;
            case PreviewKind.Font:
                FontInfo = preview.Info;
                PreviewFontFamily = BuildFontFamily(preview.Font!, preview.FontExtension);
                Kind = PreviewKind.Font;
                break;
            default:
                Kind = PreviewKind.None;
                break;
        }
    }

    private void ResetDetail()
    {
        StopAudio();
        _audioWav = null;
        VideoSource = null;
        VideoInfo = "";
        VideoError = "";
        DeleteVideoTemp();
        PreviewFontFamily = null;
        FontInfo = "";
        DeleteFontTemp();
        PreviewImage = null;
        ModelGeometry = null;
        AudioInfo = "";
        PreviewInfo = "";
        PreviewMessage = "";
        Kind = PreviewKind.None;
        JsonText = "";
        TextContent = "";
        HasTextContent = false;
        TextTabHeader = "テキスト";
        AssetHeader = IsLoaded ? "アセットを選択してください" : "ファイルまたはフォルダを開いてください";
        AssetSubHeader = "";
    }

    private static BitmapSource LoadBitmap(byte[] png)
    {
        using MemoryStream stream = new(png);
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // ===== Audio playback =====
    private void PlayAudio()
    {
        if (_audioWav is null)
            return;
        try
        {
            StopAudio();
            _soundPlayer = new SoundPlayer(new MemoryStream(_audioWav));
            _soundPlayer.Play();
        }
        catch (Exception ex)
        {
            StatusText = "再生エラー: " + ex.Message;
        }
    }

    private void StopAudio()
    {
        try
        {
            _soundPlayer?.Stop();
            _soundPlayer?.Dispose();
        }
        catch { /* ignore */ }
        _soundPlayer = null;
    }

    // ===== Video =====
    private Uri? WriteVideoTemp(byte[] data, string extension)
    {
        try
        {
            DeleteVideoTemp();
            string path = Path.Combine(Path.GetTempPath(), $"ufm_video_{Guid.NewGuid():N}.{extension}");
            File.WriteAllBytes(path, data);
            _videoTempPath = path;
            return new Uri(path);
        }
        catch
        {
            return null;
        }
    }

    private void DeleteVideoTemp()
    {
        if (_videoTempPath is null)
            return;
        try { if (File.Exists(_videoTempPath)) File.Delete(_videoTempPath); }
        catch { /* still locked by the player – the OS cleans temp later */ }
        _videoTempPath = null;
    }

    /// <summary>Called by the view when the media element cannot decode the video.</summary>
    public void OnVideoPlaybackFailed()
        => VideoError = "この動画形式はこの環境で再生できません（コーデック未対応の可能性）。\n「🎬 動画を保存」で保存して外部プレーヤーで再生してください。";

    // ===== Font =====
    private FontFamily? BuildFontFamily(byte[] data, string extension)
    {
        try
        {
            DeleteFontTemp();
            string dir = Path.Combine(Path.GetTempPath(), "UniParse_fonts", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"font.{extension}");
            File.WriteAllBytes(path, data);
            _fontTempDir = dir;

            GlyphTypeface glyph = new(new Uri(path));
            string family = glyph.Win32FamilyNames.Values.FirstOrDefault()
                            ?? glyph.FamilyNames.Values.FirstOrDefault()
                            ?? string.Empty;
            if (string.IsNullOrEmpty(family))
                return null;

            return new FontFamily(new Uri(dir + Path.DirectorySeparatorChar), "./#" + family);
        }
        catch
        {
            return null; // fall back to the default font for the sample text
        }
    }

    private void DeleteFontTemp()
    {
        if (_fontTempDir is null)
            return;
        try { if (Directory.Exists(_fontTempDir)) Directory.Delete(_fontTempDir, true); }
        catch { /* font may still be loaded; the OS cleans temp later */ }
        _fontTempDir = null;
    }

    private void SaveFont()
    {
        if (SelectedNode?.Asset is not { } asset)
            return;
        (byte[]? data, string ext) = UnityAssetService.TryGetFont(asset);
        if (data is null)
            return;
        SaveFileDialog dialog = new()
        {
            Title = "フォントを保存",
            Filter = $"フォント (*.{ext})|*.{ext}|すべてのファイル (*.*)|*.*",
            FileName = SanitizeFileName(asset.GetBestName()) + "." + ext,
        };
        if (dialog.ShowDialog() == true)
            File.WriteAllBytes(dialog.FileName, data);
    }

    // ===== Saving =====
    private void SaveImage()
    {
        if (SelectedNode?.Asset is not { } asset)
            return;
        SaveFileDialog dialog = new()
        {
            Title = "画像を保存",
            Filter = "PNG 画像 (*.png)|*.png",
            FileName = SanitizeFileName(asset.GetBestName()) + ".png",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            byte[]? png = UnityAssetService.RenderPng(asset);
            if (png is not null)
                File.WriteAllBytes(dialog.FileName, png);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveModel()
    {
        if (SelectedNode?.Asset is not { } asset)
            return;
        SaveFileDialog dialog = new()
        {
            Title = "モデルを保存",
            Filter = "glTF Binary (*.glb)|*.glb",
            FileName = SanitizeFileName(asset.GetBestName()) + ".glb",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            byte[]? glb = UnityAssetService.TryGetGlb(asset);
            if (glb is not null)
                File.WriteAllBytes(dialog.FileName, glb);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveAudio()
    {
        if (SelectedNode?.Asset is not { } asset)
            return;
        (byte[]? data, string ext) = UnityAssetService.TryGetAudio(asset);
        if (data is null)
            return;
        SaveFileDialog dialog = new()
        {
            Title = "音声を保存",
            Filter = $"音声 (*.{ext})|*.{ext}|すべてのファイル (*.*)|*.*",
            FileName = SanitizeFileName(asset.GetBestName()) + "." + ext,
        };
        if (dialog.ShowDialog() == true)
            File.WriteAllBytes(dialog.FileName, data);
    }

    private void SaveVideo()
    {
        if (SelectedNode?.Asset is not { } asset)
            return;
        (byte[]? data, string ext) = UnityAssetService.TryGetVideo(asset);
        if (data is null)
            return;
        SaveFileDialog dialog = new()
        {
            Title = "動画を保存",
            Filter = $"動画 (*.{ext})|*.{ext}|すべてのファイル (*.*)|*.*",
            FileName = SanitizeFileName(asset.GetBestName()) + "." + ext,
        };
        if (dialog.ShowDialog() == true)
            File.WriteAllBytes(dialog.FileName, data);
    }

    private void SaveJson()
    {
        if (SelectedNode?.Asset is not { } asset || string.IsNullOrEmpty(JsonText))
            return;
        SaveFileDialog dialog = new()
        {
            Title = "JSON を保存",
            Filter = "JSON ファイル (*.json)|*.json|テキスト (*.txt)|*.txt",
            FileName = SanitizeFileName(asset.GetBestName()) + ".json",
        };
        if (dialog.ShowDialog() == true)
            File.WriteAllText(dialog.FileName, JsonText, new UTF8Encoding(false));
    }

    private void SaveText()
    {
        if (SelectedNode?.Asset is not { } asset || !HasTextContent)
            return;
        string ext = UnityAssetService.GetTextExtension(asset);
        SaveFileDialog dialog = new()
        {
            Title = "テキストを保存",
            Filter = $"テキスト (*.{ext})|*.{ext}|すべてのファイル (*.*)|*.*",
            FileName = SanitizeFileName(asset.GetBestName()) + "." + ext,
        };
        if (dialog.ShowDialog() == true)
            File.WriteAllText(dialog.FileName, TextContent, new UTF8Encoding(false));
    }

    // ===== Filter =====
    private void ApplyFilter() => _ = ApplyFilterAsync();

    private async Task ApplyFilterAsync()
    {
        if (!IsLoaded)
            return;

        string? nameLower = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim().ToLowerInvariant();
        string? className = SelectedClassOption?.ClassName;

        // No filter -> restore the full tree (cheap: only the collapsed root is realized).
        if (nameLower is null && className is null)
        {
            RootNodes = _fullRootNodes;
            StatusText = $"{AssetCount:N0} アセット / Unity {ProjectVersion}";
            return;
        }

        // Build a small result-only tree on a worker thread instead of toggling the whole tree.
        StatusText = "検索中...";
        ObservableCollection<AssetTreeNode> filtered = await Task.Run(() => BuildFilteredTree(nameLower, className));
        RootNodes = filtered;

        int shown = filtered.Count > 0
            ? filtered[0].Children.Where(g => g.Kind == AssetNodeKind.TypeGroup).Sum(g => g.Children.Count)
            : 0;
        string classPart = className ?? "すべてのクラス";
        string namePart = nameLower is null ? "" : $" / 名前 \"{SearchText}\"";
        StatusText = $"フィルタ: {classPart}{namePart} — {shown:N0} 件";
    }

    private ObservableCollection<AssetTreeNode> BuildFilteredTree(string? nameLower, string? className)
    {
        const int cap = 3000;
        Dictionary<string, List<KeyValuePair<IUnityObjectBase, string>>> grouped = new();
        int total = 0;
        bool capped = false;

        GameBundle? bundle = _service.GameData?.GameBundle;
        if (bundle is not null)
        {
            foreach (AssetCollection collection in bundle.FetchAssetCollections())
            {
                foreach (IUnityObjectBase asset in collection)
                {
                    if (className is not null && !string.Equals(asset.ClassName, className, StringComparison.Ordinal))
                        continue;
                    if (nameLower is not null && !asset.GetBestName().ToLowerInvariant().Contains(nameLower))
                        continue;
                    if (total >= cap) { capped = true; break; }

                    if (!grouped.TryGetValue(asset.ClassName, out List<KeyValuePair<IUnityObjectBase, string>>? list))
                    {
                        list = new List<KeyValuePair<IUnityObjectBase, string>>();
                        grouped[asset.ClassName] = list;
                    }
                    list.Add(new KeyValuePair<IUnityObjectBase, string>(asset, collection.Name));
                    total++;
                }
                if (capped) break;
            }
        }

        AssetTreeNode root = new($"🔎 検索結果 ({total}{(capped ? "+" : "")} 件)", AssetNodeKind.Bundle) { IsExpanded = true };
        foreach (KeyValuePair<string, List<KeyValuePair<IUnityObjectBase, string>>> entry in
                 grouped.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            AssetTreeNode group = new($"{entry.Key}  ({entry.Value.Count})", AssetNodeKind.TypeGroup) { IsExpanded = true };
            foreach (KeyValuePair<IUnityObjectBase, string> pair in
                     entry.Value.OrderBy(p => p.Key.GetBestName(), StringComparer.OrdinalIgnoreCase))
            {
                IUnityObjectBase asset = pair.Key;
                string toolTip = $"{asset.ClassName}\nPathID: {asset.PathID}\nCollection: {pair.Value}";
                group.Children.Add(new AssetTreeNode(asset.GetBestName(), AssetNodeKind.Asset, asset, toolTip, UnityAssetService.IsPreviewable(asset)));
            }
            root.Children.Add(group);
        }
        if (capped)
            root.Children.Add(new AssetTreeNode($"… 上限 {cap:N0} 件まで表示。クラスや名前でさらに絞り込んでください。", AssetNodeKind.Collection));

        return new ObservableCollection<AssetTreeNode> { root };
    }

    // ===== Logging =====
    private void OnAssetRipperLog(LogType type, LogCategory category, string message)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _log.AppendLine($"[{type}] {message}");
            if (_log.Length > 16000)
                _log.Remove(0, _log.Length - 12000);
            LogText = _log.ToString();
            if (IsBusy && !string.IsNullOrWhiteSpace(message))
                BusyMessage = message;
        });
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "asset" : name;
    }

    private static void TryLog(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "UniParse_load.log"),
                DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine);
        }
        catch { /* ignore */ }
    }
}
