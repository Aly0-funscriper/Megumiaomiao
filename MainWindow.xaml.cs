using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VideoShelf.Controls;
using VideoShelf.Models;
using VideoShelf.Services;
using System.IO;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;


namespace VideoShelf
{

    public partial class MainWindow : Window
    {


        private List<VideoInfo> currentVideos =
            new List<VideoInfo>();
        private readonly Dictionary<VideoInfo, VideoCard> videoCards = new();
        private readonly HashSet<VideoInfo> selectedVideos = new();
        private readonly List<VideoInfo> playlist = new();
        private readonly PlaylistService playlistService = new();
        private readonly CollectionService collectionService = new();
        private List<MediaCollection> collections = new();
        private MediaCollection? activeCollection;
        private bool endOfTrackHandled;

        private readonly ConfigService configService = new();
        private readonly MpvService mpvService = new();
        private AppConfig config = new();
        private VideoInfo? pendingVideo;
        private VideoInfo? currentPlayingVideo;
        private bool isPlayerFullscreen;
        private bool playlistVisibleBeforeFullscreen;
        private WindowState windowStateBeforeFullscreen;
        private WindowStyle windowStyleBeforeFullscreen;
        private bool isPaused;
        private bool isSeeking;
        private bool suppressPreviewToggle;
        private bool closeAfterPlayerCleanup;
        private CancellationTokenSource scanCancellation = new();
        private bool folderViewMode;
        private List<(double Time, string Text)> currentLyrics = new();
        private string currentFolderPath = "";
        private readonly List<string> folderHistory = new();
        private int folderHistoryIndex = -1;
        private bool updatingFolderTree;
        private readonly System.Windows.Threading.DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
        private readonly System.Windows.Threading.DispatcherTimer fullscreenControlsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private readonly System.Windows.Threading.DispatcherTimer playerClickTimer = new() { Interval = TimeSpan.FromMilliseconds(520) };
        private readonly System.Windows.Threading.DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly bool launchedByMfp = Environment.GetCommandLineArgs()
            .Skip(1).Any(x => x.Contains("input-ipc-server", StringComparison.OrdinalIgnoreCase));
        private double currentPlaybackPosition;
        private double? loopA;
        private double? loopB;




        public MainWindow()
        {

            InitializeComponent();

            config = configService.Load();
            MinimumSizeBox.Text = config.MinimumFileSizeMb.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
            MinimumDurationBox.Text = config.MinimumDurationSeconds.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
            collections = collectionService.Load();
            string cachedLibraryRoot = CacheService.ReadCachedRootFolder();
            StoragePaths.Configure(config.CacheDirectory, cachedLibraryRoot);
            if (string.IsNullOrWhiteSpace(config.CacheDirectory) && !string.IsNullOrWhiteSpace(cachedLibraryRoot))
            {
                config.CacheDirectory = StoragePaths.AssetRoot;
                configService.Save(config);
            }
            ApplyMfpCommandLineCompatibility();
            EmbeddedCheck.IsChecked = config.UseEmbeddedPlayer;
            PreviewCheck.IsChecked = config.EnableHoverPreview;
            PreviewCheck.Content = "动态预览";
            LanguageCheck.IsChecked = config.UseEnglish;
            ApplyLanguage();
            mpvService.PlaybackProgressChanged += MpvService_PlaybackProgressChanged;
            mpvService.MediaPathChanged += MpvService_MediaPathChanged;
            PlayerHost.HandleCreated += async (_, _) =>
            {
                if (pendingVideo != null) await StartPlaybackAsync(pendingVideo);
                else if (launchedByMfp && !mpvService.IsRunning) await StartMfpHostAsync();
            };
            searchTimer.Tick += (_, _) => { searchTimer.Stop(); RefreshVideoList(); };
            fullscreenControlsTimer.Tick += (_, _) => { fullscreenControlsTimer.Stop(); if (isPlayerFullscreen) SetFullscreenControls(false); };
            playerClickTimer.Tick += async (_, _) => { playerClickTimer.Stop(); await TogglePlayPauseAsync(); };
            toastTimer.Tick += (_, _) => { toastTimer.Stop(); ToastPanel.Visibility = Visibility.Collapsed; };
            PlayerHost.Clicked += (_, _) => { playerClickTimer.Stop(); playerClickTimer.Start(); };
            PlayerHost.DoubleClicked += async (_, _) => { playerClickTimer.Stop(); await TogglePlayerFullscreenAsync(); };
            PlayerHost.MouseBottomChanged += (_, atBottom) => Dispatcher.BeginInvoke(() => { if (isPlayerFullscreen && atBottom) { SetFullscreenControls(true); fullscreenControlsTimer.Stop(); fullscreenControlsTimer.Start(); } });
            PlayerHost.KeyPressed += async (_, key) => await HandlePlayerKeyAsync(key);
            SizeChanged += (_, _) => { if (PlayerPanel.Visibility == Visibility.Visible && !isPlayerFullscreen) UpdatePlayerLayout(false); };


            Loaded += async (_, _) =>
            {

                await LoadCacheAsync();

                if (launchedByMfp)
                {
                    if (!EnsureMpvConfigured())
                    {
                        StatusText.Text = "请选择真正的 mpv.exe 后，再在 MFP 中重新连接";
                        return;
                    }
                    PlayerPanel.Visibility = Visibility.Visible;
                    PlayerPanel.Height = 1;
                    PlayerPanel.Margin = new Thickness(0);
                    NowPlayingText.Text = "等待 MFP 打开视频";
                    StatusText.Text = "由 MFP 启动 · 正在建立 MPV 连接";
                    if (PlayerHost.Handle != IntPtr.Zero && !mpvService.IsRunning)
                        await StartMfpHostAsync();
                }

                // Display the persisted library immediately. Disk sync runs
                // only when the user explicitly selects a library folder.

            };

        }






        private async Task LoadCacheAsync()
        {

            CacheService cacheService =
                new CacheService();



            VideoCache cache =
                cacheService.LoadCache();

            int contaminated = cache.Videos.RemoveAll(video => IsInsideCacheDirectory(video.FilePath));
            if (contaminated > 0)
                cacheService.Save(cache.Videos, cache.RootFolder);



            currentVideos =
                cache.Videos;

            // Repair only missing or legacy low-quality covers. This does not
            // rescan the media folder and each old cover is evaluated once.
            await Task.Run(() =>
            {
                foreach (var video in currentVideos)
                {
                    bool hasThumbnail = !string.IsNullOrWhiteSpace(video.ThumbnailPath) && File.Exists(video.ThumbnailPath);
                    if (!hasThumbnail && video.ThumbnailFailed && string.IsNullOrWhiteSpace(video.ThumbnailError))
                    {
                        video.ThumbnailFailed = false;
                        video.IsLoading = true;
                    }
                    if (video.IsAudio)
                    {
                        video.IsLoading = !hasThumbnail;
                        continue;
                    }
                    if (hasThumbnail && !video.ThumbnailQualityChecked)
                    {
                        video.IsLoading = FFmpegService.IsLowQualityThumbnail(video.ThumbnailPath);
                        if (!video.IsLoading) video.ThumbnailQualityChecked = true;
                    }
                    else if (hasThumbnail) video.IsLoading = false;
                }
            });
            cacheService.Save(currentVideos, cache.RootFolder);
            FolderText.Text = cache.RootFolder;



            RefreshVideoList();
            StatusText.Text = $"缓存加载 {currentVideos.Count} 个视频";
            RestorePlaylist();

            if (currentVideos.Any(video => video.IsLoading) && !string.IsNullOrWhiteSpace(cache.RootFolder))
                _ = ProcessLoadingVideos(currentVideos, cache.RootFolder, scanCancellation.Token);

        }









        private async void SelectFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {


            var dialog =
                new OpenFolderDialog();



            if (dialog.ShowDialog() != true)
                return;

            if (string.IsNullOrWhiteSpace(config.CacheDirectory))
            {
                config.CacheDirectory = Path.Combine(dialog.FolderName, ".VideoShelfCache");
                StoragePaths.Configure(config.CacheDirectory);
                configService.Save(config);
            }

            scanCancellation.Cancel();
            scanCancellation.Dispose();
            scanCancellation = new CancellationTokenSource();
            CancellationToken token = scanCancellation.Token;

            string selectedRoot = dialog.FolderName;
            FolderText.Text = selectedRoot;
            if (folderViewMode)
            {
                currentFolderPath = selectedRoot;
                folderHistory.Clear();
                folderHistoryIndex = -1;
            }
            StatusText.Text = "正在快速检查视频数量和缓存变化…";
            ShowProgress("正在快速同步视频库", 0, 0);

            CacheService cacheService = new CacheService();
            VideoCache folderCache = cacheService.LoadCacheForRoot(selectedRoot);
            List<VideoInfo> previousVideos = folderCache.Videos.ToList();
            long minimumBytes = (long)Math.Max(0, config.MinimumFileSizeMb * 1024 * 1024);
            List<VideoInfo> videos = await new SyncService().Sync(folderCache, minimumBytes, config.MinimumDurationSeconds);
            if (token.IsCancellationRequested) return;

            cacheService.CleanupRemovedAssets(previousVideos, videos);
            var activePaths = videos.Select(v => v.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var collection in collections)
                collection.VideoPaths.RemoveAll(path => path.StartsWith(selectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !activePaths.Contains(path));
            collectionService.Save(collections);
            currentVideos = videos;
            RefreshVideoList();
            cacheService.Save(videos, selectedRoot);
            HideProgress();

            int newCount = videos.Count(video => video.IsLoading);
            int removedCount = previousVideos.Count(old => !videos.Any(current => PathsEqual(old.FilePath, current.FilePath)));
            if (newCount > 0)
            {
                StatusText.Text = $"发现 {newCount} 个新视频，移除 {removedCount} 个失效记录，正在补齐缩略图";
                _ = ProcessLoadingVideos(videos, selectedRoot, token);
            }
            else
            {
                StatusText.Text = $"快速同步完成：{videos.Count} 个视频，移除 {removedCount} 个失效记录";
            }

        }








        private async Task SyncCache()
        {

            try
            {

                CacheService cacheService =
                    new CacheService();



                VideoCache cache =
                    cacheService.LoadCache();



                if (string.IsNullOrEmpty(
                    cache.RootFolder))
                    return;



                SyncService sync =
                    new SyncService();



                List<VideoInfo> videos =
                    await sync.Sync(cache);



                currentVideos =
                    videos;



                RefreshVideoList();



                cacheService.Save(
                    videos,
                    cache.RootFolder);



                _ = ProcessLoadingVideos(
                    videos, cache.RootFolder);


            }
            catch (Exception ex)
            {

                MessageBox.Show(
                    ex.ToString());

            }

        }









        private async Task ProcessLoadingVideos(
            List<VideoInfo> videos,
            string rootFolder,
            CancellationToken cancellationToken = default)
        {


            VideoProcessor processor =
                new VideoProcessor();



            int total = videos.Count(x => x.IsLoading);
            int completed = 0;
            int failed = 0;
            int excluded = 0;
            if (total > 0) Dispatcher.Invoke(() => ShowProgress("正在生成静态缩略图", 0, total));

            foreach (var video in videos.Where(x => x.IsLoading).ToList())
            {
                if (cancellationToken.IsCancellationRequested) return;
                try
                {

                    // Always finish lightweight static thumbnails for the whole
                    // library before doing any expensive animated previews.
                    await processor.Process(video, generatePreview: false, cancellationToken: cancellationToken);
                    if (config.MinimumDurationSeconds > 0 && video.Duration.TotalSeconds > 0 && video.Duration.TotalSeconds < config.MinimumDurationSeconds)
                    {
                        videos.Remove(video);
                        videoCards.Remove(video);
                        TryDeleteGeneratedAsset(video.ThumbnailPath);
                        excluded++;
                    }

                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    video.IsLoading = false;
                    video.ThumbnailFailed = true;
                    video.ThumbnailError = ex.Message;
                    failed++;
                }

                completed++;
                Dispatcher.Invoke(() =>
                {
                    if (videoCards.TryGetValue(video, out var card))
                        card.Refresh();
                    ShowProgress("正在生成静态缩略图", completed, total);
                });

            }



            if (cancellationToken.IsCancellationRequested || !PathsEqual(FolderText.Text, rootFolder)) return;
            currentVideos = videos;



            Dispatcher.Invoke(() =>
            {

                RefreshVideoList();

            });



            CacheService cacheService =
                new CacheService();



            cacheService.Save(
                videos,
                rootFolder);

            Dispatcher.Invoke(() =>
            {
                HideProgress();
                StatusText.Text = failed == 0
                    ? $"完成，共 {videos.Count} 个视频；按条件排除 {excluded} 个"
                    : $"完成，共 {videos.Count} 个视频；{failed} 个预览生成失败；排除 {excluded} 个";
            });

            if (config.EnableHoverPreview)
                await GenerateMissingPreviewsAsync();


        }









        private void SortBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {


            if (currentVideos.Count == 0)
                return;



            switch (SortBox.SelectedIndex)
            {


                case 1:

                    currentVideos =
                        currentVideos
                        .OrderBy(x => x.FileSize)
                        .ToList();

                    break;



                case 2:

                    currentVideos =
                        currentVideos
                        .OrderByDescending(
                            x => x.FileSize)
                        .ToList();

                    break;



                case 3:

                    currentVideos =
                        currentVideos
                        .OrderBy(
                            x => x.CreatedTime)
                        .ToList();

                    break;



                case 4:

                    currentVideos =
                        currentVideos
                        .OrderByDescending(
                            x => x.CreatedTime)
                        .ToList();

                    break;



                case 5:

                    currentVideos =
                        currentVideos
                        .OrderBy(
                            x => x.Duration)
                        .ToList();

                    break;



                case 6:

                    currentVideos =
                        currentVideos
                        .OrderByDescending(
                            x => x.Duration)
                        .ToList();

                    break;

            }



            RefreshVideoList();

        }








        private void RefreshVideoList()
        {
            foreach (var card in videoCards.Values) card.DeactivatePreview();
            VideoList.Items.Clear();
            UpdateFunscriptMissingCount();
            IEnumerable<VideoInfo> visibleVideos = GetFilteredVideos();
            if (folderViewMode)
            {
                RenderFolderContents(visibleVideos);
                return;
            }
            foreach (var video in visibleVideos) AddVideoCard(video);
            int visibleCount = visibleVideos.Count();
            StatusText.Text = FavoritesCheck.IsChecked == true
                ? $"收藏夹中有 {visibleCount} 个视频"
                : $"显示 {visibleCount} 个视频";

        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(left))
                    .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private IEnumerable<VideoInfo> GetFilteredVideos()
        {
            IEnumerable<VideoInfo> videos = FavoritesCheck.IsChecked == true ? currentVideos.Where(video => video.IsFavorite) : currentVideos;
            if (FunscriptOnlyCheck.IsChecked == true) videos = videos.Where(video => !video.IsAudio && HasMatchingFunscript(video));
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) videos = videos.Where(video => video.FileName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
            if (activeCollection != null) videos = videos.Where(video => activeCollection.VideoPaths.Contains(video.FilePath, StringComparer.OrdinalIgnoreCase));
            return videos.ToList();
        }

        private static bool HasMatchingFunscript(VideoInfo video)
        {
            if (video.IsAudio || string.IsNullOrWhiteSpace(video.FilePath)) return false;
            return FunscriptService.HasMatchingScript(video.FilePath);
        }

        private void UpdateFunscriptMissingCount()
        {
            int missing = currentVideos.Count(video => !video.IsAudio && !HasMatchingFunscript(video));
            FunscriptMissingText.Text = config.UseEnglish
                ? $"No matching funscript: {missing}"
                : $"无对应 funscript：{missing}";
        }

        private void Overview_Click(object sender, RoutedEventArgs e)
        {
            activeCollection = null; folderViewMode = false; OverviewCheck.IsChecked = true; FolderViewCheck.IsChecked = false;
            ExplorerToolbar.Visibility = FolderTreePanel.Visibility = Visibility.Collapsed; FolderTreeColumn.Width = new GridLength(0);
            RefreshVideoList();
        }

        private void FolderView_Click(object sender, RoutedEventArgs e)
        {
            folderViewMode = true; OverviewCheck.IsChecked = false; FolderViewCheck.IsChecked = true;
            ExplorerToolbar.Visibility = FolderTreePanel.Visibility = Visibility.Visible; FolderTreeColumn.Width = new GridLength(240);
            string root = GetLibraryRoot();
            if (string.IsNullOrWhiteSpace(currentFolderPath) || !IsWithinFolder(currentFolderPath, root))
            {
                folderHistory.Clear(); folderHistoryIndex = -1;
                NavigateToFolder(root);
            }
            else { BuildFolderTree(); RefreshBreadcrumb(); RefreshVideoList(); }
        }
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; searchTimer.Stop(); searchTimer.Start(); }

        private void SetFullscreenControls(bool visible)
        {
            PlayerControls.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            PlayerControlsRow.Height = visible ? new GridLength(108) : new GridLength(0);
            FullscreenPlaylistButton.Visibility = isPlayerFullscreen && visible ? Visibility.Visible : Visibility.Collapsed;
            RandomPlayCheck.Visibility = isPlayerFullscreen ? Visibility.Collapsed : Visibility.Visible;
        }
        private void RenderFolderContents(IEnumerable<VideoInfo> filteredVideos)
        {
            string folder = string.IsNullOrWhiteSpace(currentFolderPath) ? GetLibraryRoot() : currentFolderPath;
            string search = SearchBox.Text.Trim();
            var mediaFolders = currentVideos.Select(video => Path.GetDirectoryName(video.FilePath) ?? "")
                .Where(path => IsWithinFolder(path, folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(search))
            {
                foreach (string child in GetImmediateChildFolders(folder, mediaFolders))
                    VideoList.Items.Add(CreateFolderTile(child));
            }
            var videos = filteredVideos.Where(video =>
            {
                string parent = Path.GetDirectoryName(video.FilePath) ?? "";
                return string.IsNullOrWhiteSpace(search) ? PathsEqual(parent, folder) : IsWithinFolder(parent, folder);
            }).ToList();
            foreach (var video in videos) AddVideoCard(video);
            if (VideoList.Items.Count == 0)
                VideoList.Items.Add(new TextBlock { Text = config.UseEnglish ? "This folder is empty" : "此文件夹中没有媒体", Foreground = Brushes.LightGray, Margin = new Thickness(18) });
            StatusText.Text = config.UseEnglish ? $"{videos.Count} media items" : $"当前文件夹显示 {videos.Count} 个媒体";
            FolderBackButton.IsEnabled = folderHistoryIndex > 0;
            FolderUpButton.IsEnabled = !PathsEqual(folder, GetLibraryRoot());
        }

        private Button CreateFolderTile(string path)
        {
            var button = new Button
            {
                Content = $"📁  {Path.GetFileName(path)}", Tag = path, Width = 210, Height = 64,
                Margin = new Thickness(8), Padding = new Thickness(14, 8, 14, 8), HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(43, 48, 59)), ToolTip = path
            };
            button.Click += (_, _) => NavigateToFolder(path);
            return button;
        }

        private void NavigateToFolder(string path, bool addHistory = true)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            currentFolderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (addHistory)
            {
                if (folderHistoryIndex < folderHistory.Count - 1) folderHistory.RemoveRange(folderHistoryIndex + 1, folderHistory.Count - folderHistoryIndex - 1);
                if (folderHistoryIndex < 0 || !PathsEqual(folderHistory[folderHistoryIndex], currentFolderPath)) { folderHistory.Add(currentFolderPath); folderHistoryIndex = folderHistory.Count - 1; }
            }
            RefreshBreadcrumb(); RefreshVideoList(); SelectFolderTreeItem(currentFolderPath);
        }

        private void FolderBack_Click(object sender, RoutedEventArgs e)
        {
            if (folderHistoryIndex <= 0) return;
            folderHistoryIndex--; NavigateToFolder(folderHistory[folderHistoryIndex], false);
        }

        private void FolderUp_Click(object sender, RoutedEventArgs e)
        {
            string root = GetLibraryRoot();
            if (PathsEqual(currentFolderPath, root)) return;
            string? parent = Directory.GetParent(currentFolderPath)?.FullName;
            if (parent != null && IsWithinFolder(parent, root)) NavigateToFolder(parent);
        }

        private void RefreshBreadcrumb()
        {
            FolderBreadcrumb.Children.Clear();
            string root = GetLibraryRoot();
            if (string.IsNullOrWhiteSpace(root)) return;
            var parts = new List<(string Label, string Path)> { (new DirectoryInfo(root).Name, root) };
            string relative = Path.GetRelativePath(root, currentFolderPath);
            string cursor = root;
            if (relative != ".") foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) { cursor = Path.Combine(cursor, part); parts.Add((part, cursor)); }
            foreach (var part in parts)
            {
                if (FolderBreadcrumb.Children.Count > 0) FolderBreadcrumb.Children.Add(new TextBlock { Text = "›", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) });
                var button = new Button { Content = part.Label, Tag = part.Path, Padding = new Thickness(8, 4, 8, 4), Background = Brushes.Transparent };
                button.Click += (_, _) => NavigateToFolder((string)button.Tag); FolderBreadcrumb.Children.Add(button);
            }
        }

        private void BuildFolderTree()
        {
            updatingFolderTree = true; FolderTree.Items.Clear();
            string root = GetLibraryRoot();
            var mediaFolders = currentVideos.Select(video => Path.GetDirectoryName(video.FilePath) ?? "")
                .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(root)) FolderTree.Items.Add(CreateFolderTreeItem(root, mediaFolders));
            updatingFolderTree = false;
        }

        private TreeViewItem CreateFolderTreeItem(string folder, IReadOnlyCollection<string> mediaFolders)
        {
            var item = new TreeViewItem { Header = $"📁  {new DirectoryInfo(folder).Name}", Tag = folder, IsExpanded = IsWithinFolder(currentFolderPath, folder) };
            foreach (string child in GetImmediateChildFolders(folder, mediaFolders)) item.Items.Add(CreateFolderTreeItem(child, mediaFolders));
            return item;
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!updatingFolderTree && e.NewValue is TreeViewItem { Tag: string path }) NavigateToFolder(path);
        }

        private void SelectFolderTreeItem(string path)
        {
            updatingFolderTree = true;
            foreach (var root in FolderTree.Items.OfType<TreeViewItem>()) if (SelectFolderTreeItem(root, path)) break;
            updatingFolderTree = false;
        }

        private static bool SelectFolderTreeItem(TreeViewItem item, string path)
        {
            if (item.Tag is string itemPath && PathsEqual(itemPath, path)) { item.IsSelected = true; item.BringIntoView(); return true; }
            foreach (var child in item.Items.OfType<TreeViewItem>()) if (SelectFolderTreeItem(child, path)) { item.IsExpanded = true; return true; }
            return false;
        }

        private string GetLibraryRoot()
        {
            if (!string.IsNullOrWhiteSpace(FolderText.Text)) return Path.TrimEndingDirectorySeparator(FolderText.Text);
            return currentVideos.Select(video => Path.GetDirectoryName(video.FilePath)).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? "";
        }

        private static IEnumerable<string> GetImmediateChildFolders(string parent, IEnumerable<string> folders)
        {
            var children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                if (!IsWithinFolder(folder, parent) || PathsEqual(folder, parent)) continue;
                string relative = Path.GetRelativePath(parent, folder);
                string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (!string.IsNullOrWhiteSpace(first) && first != "..") children.Add(Path.Combine(parent, first));
            }
            return children.OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase);
        }

        private static bool IsWithinFolder(string path, string parent)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(parent)) return false;
            try
            {
                string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                string fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
                return fullPath.Equals(fullParent, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void ApplyMfpCommandLineCompatibility()
        {
            string[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];
                string? pipe = null;
                const string option = "--input-ipc-server=";
                if (argument.StartsWith(option, StringComparison.OrdinalIgnoreCase))
                    pipe = argument[option.Length..].Trim('"');
                else if (argument.Equals("--input-ipc-server", StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Length)
                    pipe = arguments[++index].Trim('"');

                if (string.IsNullOrWhiteSpace(pipe)) continue;
                const string windowsPrefix = @"\\.\pipe\";
                if (pipe.StartsWith(windowsPrefix, StringComparison.OrdinalIgnoreCase))
                    pipe = pipe[windowsPrefix.Length..];
                if (!string.IsNullOrWhiteSpace(pipe))
                {
                    config.IpcPipeName = pipe;
                    configService.Save(config);
                }
                break;
            }
        }

        private void AddVideoCard(VideoInfo video)
        {
            if (videoCards.TryGetValue(video, out var existing)) { existing.Refresh(); VideoList.Items.Add(existing); return; }
            var card = new VideoCard(video, config.EnableHoverPreview);
            card.PlayRequested += async (_, selected) => await PlayVideoAsync(selected);
            card.PlaylistAddRequested += (_, selected) => ShowAddMenu(card, selected);
            card.SelectionChanged += (_, change) =>
            {
                if (change.IsSelected) selectedVideos.Add(change.Video);
                else selectedVideos.Remove(change.Video);
                UpdateBulkActions();
            };
            card.FavoriteChanged += (_, selected) =>
            {
                var cache = new CacheService().LoadCache();
                new CacheService().Save(currentVideos, cache.RootFolder);
                if (FavoritesCheck.IsChecked == true && !selected.IsFavorite)
                    RefreshVideoList();
                else
                    StatusText.Text = selected.IsFavorite ? "已加入收藏夹" : "已取消收藏";
            };
            videoCards[video] = card;
            VideoList.Items.Add(card);
        }

        private void FavoritesFilterChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            RefreshVideoList();
        }

        private void FunscriptFilterChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            RefreshVideoList();
        }

        private void LanguageChanged(object sender, RoutedEventArgs e)
        {
            if (config == null) return;
            config.UseEnglish = LanguageCheck.IsChecked == true;
            configService.Save(config);
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            bool en = config.UseEnglish;
            Title = "Megumiaomiao";
            BrandText.Text = "Megumiaomiao";
            AuthorText.Text = en ? "By Aly0 (generated with ChatGPT)" : "作者 Aly0（使用 ChatGPT 生成）";
            SelectFolderButton.Content = en ? "Choose folder" : "选择视频目录";
            OverviewCheck.Content = en ? "Overview" : "总览";
            FolderViewCheck.Content = en ? "Folders" : "文件夹浏览";
            FolderTreeTitle.Text = en ? "Folders" : "文件夹";
            FolderBackButton.ToolTip = en ? "Back" : "后退";
            FolderUpButton.ToolTip = en ? "Up one level" : "上一级";
            SearchBox.ToolTip = en ? "Search by file name" : "搜索文件名";
            EmbeddedCheck.Content = en ? "Embedded player" : "内嵌播放";
            PreviewCheck.Content = en ? "Hover preview" : "动态预览";
            FavoritesCheck.Content = en ? "♥ Favorites" : "♥ 收藏夹";
            FunscriptOnlyCheck.Content = en ? "Has funscript" : "有 funscript";
            FunscriptOnlyCheck.ToolTip = en
                ? "Show only videos with a matching funscript"
                : "勾选后只显示拥有同名 funscript 的视频";
            CollectionsButton.Content = en ? "Collections" : "合集";
            SettingsButton.Content = en ? "Settings" : "设置";
            RandomPlayCheck.Content = en ? "Shuffle" : "随机播放";
            CacheFolderButton.Content = en ? "Cache folder" : "缓存目录";
            SelectMpvButton.Content = en ? "Choose mpv" : "选择 mpv";
            LanguageCheck.Content = en ? "中文" : "EN";
            LanguageCheck.ToolTip = en ? "切换到中文" : "Switch to English";
            PlaylistTitleText.Text = en ? "Playlist" : "播放列表";
            ShuffleCheck.Content = en ? "Shuffle" : "随机播放";
            ClearPlaylistButton.Content = en ? "Clear" : "清空";
            BulkFavoriteButton.Content = en ? "♥ Favorite selected" : "♥ 收藏所选";
            BulkPlaylistButton.Content = en ? "＋ Add selected" : "＋ 加入列表";
            BulkCollectionButton.Content = en ? "Add to collection" : "加入合集";
            BulkDeleteButton.Content = en ? "Delete selected" : "删除所选";
            ClearSelectionButton.Content = en ? "Clear selection" : "取消选择";
            UpdateFunscriptMissingCount();
            string[] labels = en
                ? new[] { "Default order", "File size ↑", "File size ↓", "Date added ↑", "Date added ↓", "Duration ↑", "Duration ↓" }
                : new[] { "默认排序", "文件大小 ↑", "文件大小 ↓", "新增时间 ↑", "新增时间 ↓", "视频时长 ↑", "视频时长 ↓" };
            for (int i = 0; i < SortBox.Items.Count && i < labels.Length; i++)
                if (SortBox.Items[i] is ComboBoxItem item) item.Content = labels[i];
            RefreshPlaylist();
        }

        private async Task PlayVideoAsync(VideoInfo video)
        {
            if (!File.Exists(video.FilePath)) { StatusText.Text = "视频文件不存在"; return; }
            pendingVideo = video;
            currentPlayingVideo = video;
            endOfTrackHandled = false;
            ExpandPlayer(video.FilePath);
            PlaylistOpenButton_Click(this, new RoutedEventArgs());
            NowPlayingText.Text = video.FileName;
            ScriptTimeline.LoadForVideo(video.FilePath);
            LoadAudioPresentation(video);
            if (!config.UseEmbeddedPlayer || PlayerHost.Handle != IntPtr.Zero)
                await StartPlaybackAsync(video);
        }

        private void LoadAudioPresentation(VideoInfo video)
        {
            currentLyrics.Clear(); LyricsPanel.Visibility = Visibility.Collapsed; AudioCoverImage.Visibility = Visibility.Collapsed; AudioCoverImage.Source = null;
            if (!video.IsAudio) return;
            if (File.Exists(video.ThumbnailPath)) { var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(video.ThumbnailPath); bitmap.EndInit(); AudioCoverImage.Source = bitmap; AudioCoverImage.Visibility = Visibility.Visible; }
            string lrc = !string.IsNullOrWhiteSpace(video.LrcPath) ? video.LrcPath : Path.ChangeExtension(video.FilePath, ".lrc");
            if (!File.Exists(lrc)) return;
            foreach (string line in File.ReadLines(lrc)) foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(line, @"\[(\d+):(\d+(?:\.\d+)?)\]")) if (double.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sec)) currentLyrics.Add((int.Parse(match.Groups[1].Value) * 60 + sec, System.Text.RegularExpressions.Regex.Replace(line, @"\[[^\]]+\]", "").Trim()));
            currentLyrics.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private void AddToPlaylist(VideoInfo video)
        {
            bool added = !playlist.Any(item => PathsEqual(item.FilePath, video.FilePath));
            if (added)
                playlist.Add(video);
            RefreshPlaylist();
            SavePlaylist();
            StatusText.Text = $"已加入播放列表：{video.FileName}";
            ShowToast(added ? "已加入播放列表" : "该作品已在播放列表中");
        }

        private void ShowAddMenu(VideoCard card, VideoInfo video)
        {
            var menu = new ContextMenu { PlacementTarget = card, Placement = PlacementMode.MousePoint };
            var playlistItem = new MenuItem { Header = "加入播放列表" }; playlistItem.Click += (_, _) => AddToPlaylist(video); menu.Items.Add(playlistItem);
            var collectionsItem = new MenuItem { Header = "加入合集" };
            if (collections.Count == 0)
            {
                var create = new MenuItem { Header = "新建“我的合集”并加入" }; create.Click += (_, _) => { var collection = new MediaCollection { Name = "我的合集", VideoPaths = new List<string> { video.FilePath } }; collections.Add(collection); collectionService.Save(collections); ShowToast("已新建并加入“我的合集”"); }; collectionsItem.Items.Add(create);
            }
            else foreach (var collection in collections)
            {
                var item = new MenuItem { Header = collection.Name, IsCheckable = true, IsChecked = collection.VideoPaths.Contains(video.FilePath, StringComparer.OrdinalIgnoreCase) };
                item.Click += (_, _) => { bool added = !collection.VideoPaths.Contains(video.FilePath, StringComparer.OrdinalIgnoreCase); if (added) collection.VideoPaths.Add(video.FilePath); collectionService.Save(collections); StatusText.Text = $"已加入 {collection.Name}"; ShowToast(added ? $"已加入合集：{collection.Name}" : $"已在合集：{collection.Name}"); };
                collectionsItem.Items.Add(item);
            }
            menu.Items.Add(collectionsItem); menu.IsOpen = true;
        }

        private void RefreshPlaylist()
        {
            PlaylistItems.Children.Clear();
            if (playlist.Count == 0)
            {
                PlaylistItems.Children.Add(new TextBlock { Text = config.UseEnglish ? "Playlist is empty" : "播放列表为空", Foreground = Brushes.LightGray, Margin = new Thickness(6) });
                return;
            }
            foreach (var video in playlist.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(2, 2, 2, 6) };
                var remove = new Button { Content = "×", Width = 32, Padding = new Thickness(0), Tag = video, Background = new SolidColorBrush(Color.FromRgb(91, 48, 57)) };
                remove.Click += (_, _) => { playlist.Remove(video); RefreshPlaylist(); SavePlaylist(); };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var play = new Button { Content = video.FileName, Tag = video, HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(59, 67, 84)) };
                play.Click += async (_, _) => await PlayVideoAsync(video);
                row.Children.Add(play);
                PlaylistItems.Children.Add(row);
            }
        }

        private void PlaylistOpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlayerPanel.Visibility != Visibility.Visible || currentPlayingVideo == null)
            {
                RightPlaylistPanel.Visibility = Visibility.Collapsed;
                PlaylistRestoreButton.Visibility = Visibility.Collapsed;
                return;
            }
            RefreshPlaylist();
            PlaylistRestoreButton.Visibility = Visibility.Collapsed;
            RightPlaylistPanel.Visibility = Visibility.Visible;
            RightPlaylistPanel.Opacity = 0;
            RightPlaylistPanel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            UpdatePlayerLayout(true);
        }
        private void CollapsePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) => { RightPlaylistPanel.Visibility = Visibility.Collapsed; RightPlaylistPanel.Opacity = 1; PlaylistRestoreButton.Visibility = isPlayerFullscreen || currentPlayingVideo == null ? Visibility.Collapsed : Visibility.Visible; if (isPlayerFullscreen) PlayerPanel.Margin = new Thickness(0); };
            RightPlaylistPanel.BeginAnimation(OpacityProperty, fade);
            UpdatePlayerLayout(true, forcePlaylistCollapsed: true);
        }
        private void RestorePlaylist_Click(object sender, RoutedEventArgs e) => PlaylistOpenButton_Click(sender, e);

        private void ClearPlaylist_Click(object sender, RoutedEventArgs e)
        {
            playlist.Clear();
            RefreshPlaylist();
            SavePlaylist();
        }

        private void SavePlaylist() => playlistService.Save(playlist.Select(video => video.FilePath));
        private void RestorePlaylist()
        {
            playlist.Clear();
            foreach (string path in playlistService.Load())
            {
                if (!File.Exists(path)) continue;
                VideoInfo video = currentVideos.FirstOrDefault(item => PathsEqual(item.FilePath, path))
                    ?? new VideoInfo { FilePath = path, FileName = Path.GetFileName(path), FileSize = new FileInfo(path).Length, CreatedTime = File.GetCreationTime(path) };
                if (!playlist.Any(item => PathsEqual(item.FilePath, path))) playlist.Add(video);
            }
            RefreshPlaylist();
        }

        private async Task PlayNextFromPlaylistAsync()
        {
            List<VideoInfo> candidates = playlist.Count > 0 ? playlist.ToList() : currentVideos.Where(v => activeCollection == null || activeCollection.VideoPaths.Contains(v.FilePath, StringComparer.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0) return;
            VideoInfo next;
            if (ShuffleCheck.IsChecked == true || RandomPlayCheck.IsChecked == true)
                next = candidates[Random.Shared.Next(candidates.Count)];
            else
            {
                int currentIndex = candidates.FindIndex(video => currentPlayingVideo != null && PathsEqual(video.FilePath, currentPlayingVideo.FilePath));
                next = candidates[(currentIndex + 1 + candidates.Count) % candidates.Count];
            }
            await PlayVideoAsync(next);
        }

        private void UpdateBulkActions()
        {
            Visibility visibility = selectedVideos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BulkFavoriteButton.Visibility = BulkPlaylistButton.Visibility = BulkCollectionButton.Visibility = BulkDeleteButton.Visibility = ClearSelectionButton.Visibility = visibility;
            if (selectedVideos.Count > 0) ClearSelectionButton.Content = $"取消选择 ({selectedVideos.Count})";
        }

        private void BulkFavorite_Click(object sender, RoutedEventArgs e)
        {
            foreach (var video in selectedVideos) video.IsFavorite = true;
            SaveCurrentCache();
            foreach (var video in selectedVideos) if (videoCards.TryGetValue(video, out var card)) card.Refresh();
            StatusText.Text = $"已收藏 {selectedVideos.Count} 个视频";
        }

        private void BulkPlaylist_Click(object sender, RoutedEventArgs e)
        {
            foreach (var video in selectedVideos) AddToPlaylist(video);
            StatusText.Text = $"已将 {selectedVideos.Count} 个视频加入播放列表";
        }

        private void CollectionsButton_Click(object sender, RoutedEventArgs e) { RefreshCollections(); CollectionsPopup.IsOpen = true; }
        private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsPopup.IsOpen = true;

        private void SaveExclusionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(MinimumSizeBox.Text, out double sizeMb) || sizeMb < 0 ||
                !double.TryParse(MinimumDurationBox.Text, out double seconds) || seconds < 0)
            {
                ShowToast("请输入大于或等于 0 的数字");
                return;
            }
            config.MinimumFileSizeMb = sizeMb;
            config.MinimumDurationSeconds = seconds;
            configService.Save(config);
            SettingsPopup.IsOpen = false;
            ShowToast("排除条件已保存，下次扫描生效");
        }
        private void CreateCollection_Click(object sender, RoutedEventArgs e) { collections.Add(new MediaCollection { Name = $"合集 {collections.Count + 1}" }); collectionService.Save(collections); RefreshCollections(); }
        private void BulkCollection_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = sender as UIElement, Placement = PlacementMode.Bottom };
            if (collections.Count == 0)
            {
                var create = new MenuItem { Header = "新建“我的合集”并加入所选视频" };
                create.Click += (_, _) => { var target = new MediaCollection { Name = "我的合集" }; AddSelectedToCollection(target); collections.Add(target); collectionService.Save(collections); };
                menu.Items.Add(create);
            }
            else foreach (var collection in collections)
            {
                int existing = selectedVideos.Count(v => collection.VideoPaths.Contains(v.FilePath, StringComparer.OrdinalIgnoreCase));
                var item = new MenuItem { Header = $"{collection.Name}  ({collection.VideoPaths.Count} 个视频)", IsCheckable = true, IsChecked = existing == selectedVideos.Count };
                item.Click += (_, _) => { AddSelectedToCollection(collection); collectionService.Save(collections); StatusText.Text = $"已将 {selectedVideos.Count} 个视频加入 {collection.Name}"; };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }
        private void AddSelectedToCollection(MediaCollection collection) { foreach (var video in selectedVideos) if (!collection.VideoPaths.Contains(video.FilePath, StringComparer.OrdinalIgnoreCase)) collection.VideoPaths.Add(video.FilePath); }
        private void RefreshCollections()
        {
            CollectionsItems.Children.Clear();
            foreach (var collection in collections)
            {
                var latest = collection.VideoPaths.Select(path => currentVideos.FirstOrDefault(v => PathsEqual(v.FilePath, path))).LastOrDefault(v => v != null);
                var panel = new StackPanel();
                if (latest != null && File.Exists(latest.ThumbnailPath)) { var image = new Image { Width = 160, Height = 90, Stretch = Stretch.UniformToFill }; var bitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(latest.ThumbnailPath)); image.Source = bitmap; panel.Children.Add(image); }
                panel.Children.Add(new TextBlock { Text = $"{collection.Name}  ·  {collection.VideoPaths.Count}", Foreground = Brushes.White, Margin = new Thickness(6), TextTrimming = TextTrimming.CharacterEllipsis });
                var button = new Button { Content = panel, Width = 180, Margin = new Thickness(5), Tag = collection, Background = new SolidColorBrush(Color.FromRgb(52, 78, 58)) };
                button.Click += (_, _) =>
                {
                    activeCollection = collection;
                    folderViewMode = false;
                    OverviewCheck.IsChecked = false;
                    FolderViewCheck.IsChecked = false;
                    ExplorerToolbar.Visibility = FolderTreePanel.Visibility = Visibility.Collapsed;
                    FolderTreeColumn.Width = new GridLength(0);
                    CollectionsPopup.IsOpen = false;
                    RefreshVideoList();
                };
                CollectionsItems.Children.Add(button);
            }
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            selectedVideos.Clear();
            RefreshVideoList();
            UpdateBulkActions();
        }

        private void BulkDelete_Click(object sender, RoutedEventArgs e)
        {
            int count = selectedVideos.Count;
            if (count == 0 || MessageBox.Show($"确定永久删除选中的 {count} 个视频文件吗？此操作无法撤销。", "删除视频", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            int failed = 0;
            foreach (var video in selectedVideos.ToList())
            {
                try { File.Delete(video.FilePath); currentVideos.Remove(video); playlist.Remove(video); }
                catch { failed++; }
            }
            selectedVideos.Clear();
            SaveCurrentCache();
            RefreshPlaylist();
            RefreshVideoList();
            UpdateBulkActions();
            StatusText.Text = failed == 0 ? $"已删除 {count} 个视频" : $"删除完成，{failed} 个文件删除失败";
        }

        private void SaveCurrentCache()
        {
            var cacheService = new CacheService();
            var cache = cacheService.LoadCache();
            cacheService.Save(currentVideos, cache.RootFolder);
        }

        private async Task StartPlaybackAsync(VideoInfo video)
        {
            try
            {
                pendingVideo = null;
                if (mpvService.IsRunning)
                {
                    // Keep the existing process and named pipe alive so MFP stays connected.
                    StatusText.Text = "正在切换视频…";
                    await mpvService.LoadFileAsync(video.FilePath);
                    await ClearAbLoopAsync();
                    await mpvService.PlayAsync();
                }
                else
                {
                    StatusText.Text = "正在连接 mpv…";
                    await mpvService.StartAsync(config, video.FilePath, PlayerHost.Handle, exposeMfpPipe: launchedByMfp);
                }
                isPaused = false;
                ResetAbLoopVisuals();
                SetPlayPauseVisual(false);
                StatusText.Text = launchedByMfp
                    ? $"正在播放 · MFP IPC: {config.IpcPipeName}"
                    : "正在播放 · 独立模式";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex is FileNotFoundException ? "请先点击“选择 mpv”设置 mpv.exe" : "mpv 启动失败";
                MessageBox.Show(ex.Message, "VideoShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task StartMfpHostAsync()
        {
            try
            {
                await mpvService.StartAsync(config, null, PlayerHost.Handle, startPaused: true, exposeMfpPipe: true);
                isPaused = true;
                SetPlayPauseVisual(true);
                StatusText.Text = "已启动 MPV · 请在 MFP 点击连接";
            }
            catch (Exception ex)
            {
                StatusText.Text = "MFP 兼容启动失败";
                if (!launchedByMfp)
                    MessageBox.Show(ex.Message, "VideoShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool EnsureMpvConfigured()
        {
            if (File.Exists(config.MpvPath) &&
                !Path.GetFullPath(config.MpvPath).Equals(Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase))
                return true;

            string appFolder = AppContext.BaseDirectory;
            string[] candidates =
            [
                Path.Combine(appFolder, "real-mpv.exe"),
                Path.Combine(appFolder, "Bin", "mpv.exe"),
                Path.Combine(appFolder, "Bin", "mpv", "mpv.exe")
            ];
            string? discovered = candidates.FirstOrDefault(File.Exists);
            if (discovered != null)
            {
                config.MpvPath = discovered;
                configService.Save(config);
                return true;
            }

            var dialog = new OpenFileDialog
            {
                Title = "首次使用：请选择真正的 mpv.exe（不是 MFP 兼容入口）",
                Filter = "mpv 播放器 (mpv.exe)|mpv.exe|可执行文件 (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true) return false;
            if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("请选择真正的 mpv 播放器，不能选择当前的 MFP 兼容入口。", "VideoShelf");
                return false;
            }
            config.MpvPath = dialog.FileName;
            configService.Save(config);
            return true;
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e) => await TogglePlayPauseAsync();

        private async Task TogglePlayPauseAsync()
        {
            try
            {
                await mpvService.TogglePauseAsync();
                isPaused = !isPaused;
                SetPlayPauseVisual(isPaused);
                PlayPauseButton.ToolTip = isPaused ? "继续播放（空格）" : "暂停（空格）";
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }

        private async void Stop_Click(object sender, RoutedEventArgs e)
        { try { if (isPlayerFullscreen) await TogglePlayerFullscreenAsync(forceExit: true); await mpvService.StopPlaybackAsync(); isPaused = true; SetPlayPauseVisual(true); AudioCoverImage.Source = null; AudioCoverImage.Visibility = Visibility.Collapsed; LyricsPanel.Visibility = Visibility.Collapsed; currentLyrics.Clear(); PlayerPanel.Visibility = Visibility.Collapsed; RightPlaylistPanel.Visibility = Visibility.Collapsed; PlaylistRestoreButton.Visibility = Visibility.Collapsed; currentPlayingVideo = null; StatusText.Text = "播放已停止"; } catch (Exception ex) { StatusText.Text = ex.Message; } }

        private void SetPlayPauseVisual(bool showPlay)
        {
            PlayIcon.Visibility = showPlay ? Visibility.Visible : Visibility.Collapsed;
            PauseIcon.Visibility = showPlay ? Visibility.Collapsed : Visibility.Visible;
            PlayPauseButton.ToolTip = showPlay ? "继续播放（空格）" : "暂停（空格）";
        }

        private async void Fullscreen_Click(object sender, RoutedEventArgs e)
            => await TogglePlayerFullscreenAsync();

        private async void LoopA_Click(object sender, RoutedEventArgs e)
        {
            if (!mpvService.IsRunning) return;
            if (loopA.HasValue && loopB.HasValue)
            {
                await ClearAbLoopAsync();
                ShowToast("A-B 循环已清除");
                return;
            }
            loopA = currentPlaybackPosition;
            loopB = null;
            await mpvService.SetAbLoopAsync(loopA, null);
            LoopAButton.Background = new SolidColorBrush(Color.FromRgb(88, 101, 242));
            LoopBButton.Background = new SolidColorBrush(Color.FromRgb(59, 67, 84));
            ShowToast($"循环起点 A：{FormatTime(loopA.Value)}");
        }

        private async void LoopB_Click(object sender, RoutedEventArgs e)
        {
            if (!mpvService.IsRunning) return;
            if (!loopA.HasValue) { ShowToast("请先设置循环起点 A"); return; }
            if (currentPlaybackPosition <= loopA.Value + 0.05) { ShowToast("循环终点 B 必须晚于 A"); return; }
            loopB = currentPlaybackPosition;
            await mpvService.SetAbLoopAsync(loopA, loopB);
            LoopBButton.Background = new SolidColorBrush(Color.FromRgb(88, 101, 242));
            ShowToast($"A-B 循环：{FormatTime(loopA.Value)} – {FormatTime(loopB.Value)}");
        }

        private async Task ClearAbLoopAsync()
        {
            loopA = loopB = null;
            if (mpvService.IsRunning) await mpvService.SetAbLoopAsync(null, null);
            ResetAbLoopVisuals();
        }

        private void ResetAbLoopVisuals()
        {
            loopA = loopB = null;
            LoopAButton.Background = new SolidColorBrush(Color.FromRgb(59, 67, 84));
            LoopBButton.Background = new SolidColorBrush(Color.FromRgb(59, 67, 84));
        }

        private void FullscreenPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (!isPlayerFullscreen) return;
            if (RightPlaylistPanel.Visibility == Visibility.Visible)
            {
                RightPlaylistPanel.Visibility = Visibility.Collapsed;
                PlayerPanel.Margin = new Thickness(0);
                FullscreenPlaylistButton.Content = config.UseEnglish ? "Playlist" : "播放列表";
            }
            else
            {
                RefreshPlaylist();
                RightPlaylistPanel.BeginAnimation(OpacityProperty, null);
                RightPlaylistPanel.Opacity = 1;
                RightPlaylistPanel.Visibility = Visibility.Visible;
                RightPlaylistPanel.Height = Math.Max(360, ActualHeight);
                PlaylistRestoreButton.Visibility = Visibility.Collapsed;
                PlayerPanel.Margin = new Thickness(0, 0, 350, 0);
                FullscreenPlaylistButton.Content = config.UseEnglish ? "Hide playlist" : "收起列表";
            }
        }

        private async Task TogglePlayerFullscreenAsync(bool forceExit = false)
        {
            if (!mpvService.IsRunning) return;
            if (forceExit && !isPlayerFullscreen) return;
            isPlayerFullscreen = forceExit ? false : !isPlayerFullscreen;
            if (isPlayerFullscreen)
            {
                playlistVisibleBeforeFullscreen = RightPlaylistPanel.Visibility == Visibility.Visible;
                RightPlaylistPanel.Visibility = Visibility.Collapsed;
                PlaylistRestoreButton.Visibility = Visibility.Collapsed;
                windowStateBeforeFullscreen = WindowState;
                windowStyleBeforeFullscreen = WindowStyle;
                TopToolbar.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                LibraryPanel.Visibility = Visibility.Collapsed;
                PlayerHeader.Visibility = Visibility.Collapsed; PlayerControls.Visibility = Visibility.Collapsed;
                PlayerHeaderRow.Height = new GridLength(0); PlayerControlsRow.Height = new GridLength(0);
                RootGrid.Margin = new Thickness(0); PlayerPanel.Margin = new Thickness(0); PlayerPanel.CornerRadius = new CornerRadius(0);
                PlayerPanel.Height = SystemParameters.PrimaryScreenHeight;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                TopToolbar.Visibility = Visibility.Visible;
                LibraryPanel.Visibility = Visibility.Visible;
                PlayerHeader.Visibility = Visibility.Visible; PlayerControls.Visibility = Visibility.Visible;
                PlayerHeaderRow.Height = new GridLength(42); PlayerControlsRow.Height = new GridLength(108);
                FullscreenPlaylistButton.Visibility = Visibility.Collapsed;
                RandomPlayCheck.Visibility = Visibility.Visible;
                RootGrid.Margin = new Thickness(20); PlayerPanel.Margin = new Thickness(0, 0, 0, 16); PlayerPanel.CornerRadius = new CornerRadius(12);
                if (playlistVisibleBeforeFullscreen) { RightPlaylistPanel.Visibility = Visibility.Visible; PlaylistRestoreButton.Visibility = Visibility.Collapsed; }
                else PlaylistRestoreButton.Visibility = currentPlayingVideo == null ? Visibility.Collapsed : Visibility.Visible;
                WindowStyle = windowStyleBeforeFullscreen;
                WindowState = windowStateBeforeFullscreen;
                _ = Dispatcher.BeginInvoke(() => UpdatePlayerLayout(false));
            }
            await Task.CompletedTask;
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && mpvService.IsRunning)
            {
                e.Handled = true;
                await TogglePlayPauseAsync();
            }
            else if (e.Key == Key.Escape && isPlayerFullscreen)
            {
                e.Handled = true;
                await TogglePlayerFullscreenAsync(forceExit: true);
            }
            else if (e.Key == Key.Left && mpvService.IsRunning)
            {
                e.Handled = true;
                await mpvService.SeekAsync(-5);
            }
            else if (e.Key == Key.Right && mpvService.IsRunning)
            {
                e.Handled = true;
                await mpvService.SeekAsync(5);
            }
        }

        private void Window_Activated(object? sender, EventArgs e)
        {
            if (!PlayerHost.IsKeyboardFocusWithin)
                Keyboard.Focus(this);
        }

        private async Task HandlePlayerKeyAsync(int virtualKey)
        {
            if (!mpvService.IsRunning) return;
            switch (virtualKey)
            {
                case 0x20: await TogglePlayPauseAsync(); break;
                case 0x25: await mpvService.SeekAsync(-5); break;
                case 0x27: await mpvService.SeekAsync(5); break;
                case 0x1B when isPlayerFullscreen: await TogglePlayerFullscreenAsync(forceExit: true); break;
            }
        }

        private void TopToolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<Button>(e.OriginalSource as DependencyObject) != null) return;
            if (e.ClickCount == 2) ToggleWindowMaximized();
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void WindowChrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.GetPosition(this).Y > 72) return;
            var source = e.OriginalSource as DependencyObject;
            if (FindVisualParent<ButtonBase>(source) != null ||
                FindVisualParent<ComboBox>(source) != null ||
                FindVisualParent<TextBoxBase>(source) != null ||
                FindVisualParent<Slider>(source) != null)
                return;

            if (e.ClickCount == 2)
            {
                ToggleWindowMaximized();
                e.Handled = true;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
                e.Handled = true;
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match) return match;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleWindowMaximized();
        private void ToggleWindowMaximized() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

        private void MpvService_PlaybackProgressChanged(object? sender, PlaybackProgressEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                currentPlaybackPosition = e.Position;
                PlaybackSlider.Maximum = Math.Max(1, e.Duration);
                if (!isSeeking)
                    PlaybackSlider.Value = Math.Clamp(e.Position, 0, PlaybackSlider.Maximum);
                CurrentTimeText.Text = FormatTime(e.Position);
                DurationText.Text = FormatTime(e.Duration);
                ScriptTimeline.UpdatePlayback(e.Position, e.Duration);
                if (currentLyrics.Count > 0) { string lyric = currentLyrics.LastOrDefault(item => item.Time <= e.Position).Text ?? ""; LyricsText.Text = lyric; LyricsPanel.Visibility = string.IsNullOrWhiteSpace(lyric) ? Visibility.Collapsed : Visibility.Visible; }
                if (!endOfTrackHandled && e.Duration > 1 && e.Position >= e.Duration - 0.35 && (playlist.Count > 0 || RandomPlayCheck.IsChecked == true))
                {
                    endOfTrackHandled = true;
                    _ = PlayNextFromPlaylistAsync();
                }
            });
        }

        private void MpvService_MediaPathChanged(object? sender, string path)
        {
            Dispatcher.BeginInvoke(() => ExpandPlayer(path));
        }

        private void ExpandPlayer(string? mediaPath)
        {
            PlayerPanel.Visibility = Visibility.Visible;
            UpdatePlayerLayout(false);
            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                NowPlayingText.Text = Path.GetFileName(mediaPath);
                ScriptTimeline.LoadForVideo(mediaPath);
            }
        }

        private void UpdatePlayerLayout(bool animate, bool forcePlaylistCollapsed = false)
        {
            if (isPlayerFullscreen) return;
            bool sidebar = !forcePlaylistCollapsed && RightPlaylistPanel.Visibility == Visibility.Visible;
            // Reserve a narrow strip for the restore tab. Native mpv video
            // cannot be overlaid by WPF controls, so the tab must sit outside it.
            double right = sidebar ? 350 : (currentPlayingVideo != null ? 34 : 0);
            var targetMargin = new Thickness(0, 0, right, 16);
            double width = Math.Max(420, RootGrid.ActualWidth - right);
            double ratio = currentPlayingVideo is { Width: > 0, Height: > 0 }
                ? (double)currentPlayingVideo.Width / currentPlayingVideo.Height : 16d / 9d;
            double maxHeight = Math.Max(436, ActualHeight * 0.68);
            double targetHeight = Math.Clamp(width / Math.Max(0.35, ratio) + 150, 380, maxHeight);
            if (animate)
            {
                Thickness fromMargin = PlayerPanel.Margin; double fromHeight = PlayerPanel.ActualHeight > 0 ? PlayerPanel.ActualHeight : PlayerPanel.Height;
                PlayerPanel.BeginAnimation(MarginProperty, null); PlayerPanel.BeginAnimation(HeightProperty, null);
                PlayerPanel.Margin = targetMargin; PlayerPanel.Height = targetHeight;
                PlayerPanel.BeginAnimation(MarginProperty, new System.Windows.Media.Animation.ThicknessAnimation(fromMargin, targetMargin, TimeSpan.FromMilliseconds(170)));
                PlayerPanel.BeginAnimation(HeightProperty, new System.Windows.Media.Animation.DoubleAnimation(fromHeight, targetHeight, TimeSpan.FromMilliseconds(170)));
            }
            else { PlayerPanel.BeginAnimation(MarginProperty, null); PlayerPanel.BeginAnimation(HeightProperty, null); PlayerPanel.Margin = targetMargin; PlayerPanel.Height = targetHeight; }
            RightPlaylistPanel.Height = targetHeight;
        }

        private async void PlaybackSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isSeeking = true;
            PlaybackSeekSurface.CaptureMouse();
            UpdateSeekSlider(e.GetPosition(PlaybackSeekSurface).X);
            e.Handled = true;
            try { await mpvService.SeekAbsoluteAsync(PlaybackSlider.Value); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }

        private void PlaybackSlider_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isSeeking || e.LeftButton != MouseButtonState.Pressed) return;
            UpdateSeekSlider(e.GetPosition(PlaybackSeekSurface).X);
            e.Handled = true;
        }

        private async void PlaybackSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isSeeking) return;
            UpdateSeekSlider(e.GetPosition(PlaybackSeekSurface).X);
            isSeeking = false;
            PlaybackSeekSurface.ReleaseMouseCapture();
            e.Handled = true;
            try { await mpvService.SeekAbsoluteAsync(PlaybackSlider.Value); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }

        private void UpdateSeekSlider(double mouseX)
        {
            if (PlaybackSeekSurface.ActualWidth <= 0) return;
            double ratio = Math.Clamp(mouseX / PlaybackSeekSurface.ActualWidth, 0, 1);
            PlaybackSlider.Value = ratio * PlaybackSlider.Maximum;
            CurrentTimeText.Text = FormatTime(PlaybackSlider.Value);
        }

        private async void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || !mpvService.IsRunning) return;
            try { await mpvService.SetVolumeAsync(e.NewValue); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
        }

        private async void ClosePlayer_Click(object sender, RoutedEventArgs e)
        { if (isPlayerFullscreen) await TogglePlayerFullscreenAsync(forceExit: true); pendingVideo = null; await mpvService.StopAsync(); PlayerPanel.Visibility = Visibility.Collapsed; RightPlaylistPanel.Visibility = Visibility.Collapsed; PlaylistRestoreButton.Visibility = Visibility.Collapsed; currentPlayingVideo = null; }

        private void SelectMpv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "mpv (mpv.exe)|mpv.exe|可执行文件 (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog() != true) return;
            config.MpvPath = dialog.FileName; configService.Save(config);
            StatusText.Text = "mpv 路径已保存";
        }

        private async void SelectCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "选择缩略图和动态预览缓存目录" };
            if (dialog.ShowDialog() != true) return;
            string target = Path.Combine(dialog.FolderName, "VideoShelfCache");
            if (Path.GetFullPath(target).Equals(Path.GetFullPath(StoragePaths.AssetRoot), StringComparison.OrdinalIgnoreCase))
                return;

            ProgressPanel.Visibility = Visibility.Visible;
            ScanProgress.IsIndeterminate = true;
            ProgressText.Text = "正在迁移缓存，请勿关闭程序";
            try
            {
                string libraryRoot = new CacheService().LoadCache().RootFolder;
                await Task.Run(() => new CacheService().MoveAssetsTo(target, currentVideos, libraryRoot));
                config.CacheDirectory = target;
                configService.Save(config);
                StatusText.Text = $"缓存已迁移到 {target}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "缓存迁移失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { HideProgress(); }
        }

        private void PlaybackModeChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            config.UseEmbeddedPlayer = EmbeddedCheck.IsChecked == true;
            configService.Save(config);
            StatusText.Text = config.UseEmbeddedPlayer ? "已切换为内嵌播放" : "已切换为外部 mpv 窗口";
        }

        private void PreviewModeChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || suppressPreviewToggle) return;
            bool enable = PreviewCheck.IsChecked == true;
            int missingCount = currentVideos.Count(v => !IsUsablePreview(v.PreviewPath));

            if (enable && currentVideos.Count >= 60 && missingCount > 0)
            {
                var result = MessageBox.Show(
                    $"当前视频库有 {currentVideos.Count} 个视频，其中 {missingCount} 个需要生成动态预览。\n\n" +
                    "视频过多会导致渲染时间较长，并明显占用 CPU、硬盘和存储空间。是否继续开启？",
                    "动态预览性能提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    suppressPreviewToggle = true;
                    PreviewCheck.IsChecked = false;
                    suppressPreviewToggle = false;
                    return;
                }
            }

            config.EnableHoverPreview = enable;
            PreviewCheck.Content = "动态预览";
            configService.Save(config);
            RefreshVideoList();
            StatusText.Text = enable ? "已开启动态预览" : "已关闭动态预览，可减少资源占用";
            if (enable && missingCount > 0)
                _ = GenerateMissingPreviewsAsync();
        }

        private async Task GenerateMissingPreviewsAsync()
        {
            var missing = currentVideos
                .Where(v => !v.IsAudio && File.Exists(v.FilePath) && !IsUsablePreview(v.PreviewPath))
                .ToList();
            if (missing.Count == 0) return;

            var processor = new VideoProcessor();
            int completed = 0;
            int failed = 0;
            ShowProgress("正在生成动态预览", 0, missing.Count);
            foreach (var video in missing)
            {
                if (!config.EnableHoverPreview || scanCancellation.IsCancellationRequested) break;
                try { await processor.CreatePreviewOnly(video, scanCancellation.Token); }
                catch (OperationCanceledException) { return; }
                catch { failed++; }
                completed++;
                ShowProgress("正在生成动态预览", completed, missing.Count);
            }

            var cache = new CacheService().LoadCache();
            new CacheService().Save(currentVideos, cache.RootFolder);
            HideProgress();
            RefreshVideoList();
            StatusText.Text = failed == 0 ? "动态预览生成完成" : $"动态预览完成，{failed} 个生成失败";
        }

        private static bool IsUsablePreview(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length >= 1024; }
            catch { return false; }
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (closeAfterPlayerCleanup) return;
            e.Cancel = true;
            closeAfterPlayerCleanup = true;
            // Give immediate visual feedback; media cleanup continues for at
            // most the short mpv shutdown timeout.
            Hide();
            scanCancellation.Cancel();
            try { await mpvService.StopAsync(); }
            finally { scanCancellation.Dispose(); Close(); }
        }

        private void ShowProgress(string label, int value, int maximum)
        {
            ProgressPanel.Visibility = Visibility.Visible;
            ScanProgress.IsIndeterminate = maximum <= 0;
            ScanProgress.Maximum = Math.Max(1, maximum);
            ScanProgress.Value = Math.Min(value, ScanProgress.Maximum);
            ProgressText.Text = maximum <= 0 ? label : $"{label}  {value}/{maximum}";
            // Do not leave a completed progress banner pinned while cache
            // serialization or the final card refresh finishes in background.
            if (maximum > 0 && value >= maximum)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(HideProgress));
        }

        private void HideProgress() => ProgressPanel.Visibility = Visibility.Collapsed;

        private void ShowToast(string message)
        {
            toastTimer.Stop();
            ToastText.Text = message;
            ToastPanel.Visibility = Visibility.Visible;
            ToastPanel.Opacity = 1;
            toastTimer.Start();
        }

        private static bool IsInsideCacheDirectory(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string configured = Path.TrimEndingDirectorySeparator(Path.GetFullPath(StoragePaths.AssetRoot)) + Path.DirectorySeparatorChar;
                if (full.StartsWith(configured, StringComparison.OrdinalIgnoreCase)) return true;
                return full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part.Equals(".VideoShelfCache", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        private static void TryDeleteGeneratedAsset(string path)
        {
            try { if (IsInsideCacheDirectory(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }


    }

}
