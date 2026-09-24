using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;
using FontAutoLoader.Models;
using FontAutoLoader.Services;
using System.Runtime.InteropServices;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FontAutoLoader
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly DatabaseService _dbService = new();
        private readonly ObservableCollection<AssFontItem> _assFonts = new(); // 整体显示数据源(去重)
        private readonly ObservableCollection<AssFileGroup> _fileGroups = new(); // 按 ASS 文件折叠分组数据源
        private readonly ObservableCollection<FontRecord> _searchResults = new();
        private readonly ObservableCollection<string> _folders = new();
        private readonly ObservableCollection<string> _mountedFonts = new();
        private readonly ObservableCollection<string> _systemFonts = new();
        private HashSet<string> _systemInstalledSet = new(StringComparer.OrdinalIgnoreCase);

        // 200Hz 屏幕垂直同步阻尼追踪动画状态
        private double _curInst = 0, _curMount = 0, _curUnmount = 1, _curErr = 0;
        private double _targetInst = 0, _targetMount = 0, _targetUnmount = 1, _targetErr = 0;
        private bool _isRenderingHooked = false;
        private readonly System.Diagnostics.Stopwatch _animStopwatch = new();
        private TimeSpan _lastRenderingTime;

        // 自定义指示条 200Hz 无缝滑行状态
        private double _curIndicatorY = 0;
        private double _targetIndicatorY = 0;
        private double _indicatorX = 4;
        private bool _isIndicatorAnimating = false;

        // 右侧面板 200Hz 淡入微浮入动效状态
        private Grid? _currentTransitionPanel;
        private double _panelAnimTime = 0;
        private bool _isPanelTransitioning = false;

        public MainWindow()
        {
            InitializeComponent();

            RootWindowGrid.SizeChanged += (s, e) => UpdateCustomIndicator(false);

            AssFontListView.ItemsSource = _assFonts;
            AssGroupedListView.ItemsSource = _fileGroups;
            SearchListView.ItemsSource = _searchResults;
            FolderListView.ItemsSource = _folders;
            MountedFontListView.ItemsSource = _mountedFonts;
            SystemFontListView.ItemsSource = _systemFonts;

            LoadFolders();
            RefreshSystemFonts();
            RefreshMountedFonts();

            // 拦截应用窗口关闭事件，以便执行带进度条的平滑卸载
            this.AppWindow.Closing += MainWindow_AppWindowClosing;
        }

        private bool _isForceClosing = false;

        private async void MainWindow_AppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (_isForceClosing)
            {
                return;
            }

            var loadedFonts = FontNativeService.GetLoadedFonts();
            if (loadedFonts.Count == 0)
            {
                return;
            }

            args.Cancel = true;

            var dialog = new ContentDialog
            {
                Title = "正在安全卸载临时字体",
                XamlRoot = this.Content.XamlRoot
            };

            var sp = new StackPanel { Spacing = 12, Margin = new Thickness(0, 8, 0, 8) };
            var tipText = new TextBlock { Text = $"正在释放当前已挂载的 {loadedFonts.Count} 个临时字体，请稍候...", Opacity = 0.8 };
            var pBar = new ProgressBar { Minimum = 0, Maximum = loadedFonts.Count, Value = 0, Height = 6 };
            sp.Children.Add(tipText);
            sp.Children.Add(pBar);
            dialog.Content = sp;

            _ = dialog.ShowAsync();

            await Task.Run(async () =>
            {
                int current = 0;
                foreach (var fontPath in loadedFonts)
                {
                    FontNativeService.UnloadFont(fontPath);
                    current++;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        pBar.Value = current;
                    });
                    await Task.Delay(20);
                }
            });

            dialog.Hide();
            _isForceClosing = true;
            this.Close();
        }

        private bool _isLeftDrawerExpanded = true;

        private void ToggleLeftDrawer_Click(object sender, RoutedEventArgs e)
        {
            _isLeftDrawerExpanded = !_isLeftDrawerExpanded;
            if (_isLeftDrawerExpanded)
            {
                LeftColDef.Width = new GridLength(360);
                LeftFontSidePanel.Visibility = Visibility.Visible;
                BtnExpandDrawer.Visibility = Visibility.Collapsed;
            }
            else
            {
                LeftColDef.Width = new GridLength(0);
                LeftFontSidePanel.Visibility = Visibility.Collapsed;
                BtnExpandDrawer.Visibility = Visibility.Visible;
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateCustomIndicator(false);
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                string tag = item.Tag?.ToString() ?? string.Empty;

                // 立即启动小蓝条滑动
                UpdateCustomIndicator(true);

                // 启动右侧内容区域的平滑过渡动画
                SwitchPageWithAnimation(tag);
            }
        }

        private void SwitchPageWithAnimation(string tag)
        {
            Grid target = tag switch
            {
                "AssPage" => AssPagePanel,
                "SearchPage" => SearchPagePanel,
                "FolderPage" => FolderPagePanel,
                _ => AssPagePanel
            };

            // 隐藏其余面板
            AssPagePanel.Visibility = (target == AssPagePanel) ? Visibility.Visible : Visibility.Collapsed;
            SearchPagePanel.Visibility = (target == SearchPagePanel) ? Visibility.Visible : Visibility.Collapsed;
            FolderPagePanel.Visibility = (target == FolderPagePanel) ? Visibility.Visible : Visibility.Collapsed;

            // 初始化新面板在下方向上微移 16px 且透明
            target.Opacity = 0;
            target.Translation = new System.Numerics.Vector3(0, 16, 0);

            _currentTransitionPanel = target;
            _panelAnimTime = 0;
            _isPanelTransitioning = true;

            StartVsyncTracker();
        }

        private void UpdateCustomIndicator(bool animate)
        {
            if (NavView.SelectedItem is not NavigationViewItem targetItem) return;

            try
            {
                var transform = targetItem.TransformToVisual(RootWindowGrid);
                var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                _indicatorX = pt.X + 4;
                _targetIndicatorY = pt.Y + (targetItem.ActualHeight - CustomIndicator.Height) / 2;

                if (!animate || CustomIndicator.Opacity == 0)
                {
                    _curIndicatorY = _targetIndicatorY;
                    CustomIndicator.Opacity = 1;
                    ApplyIndicatorPosition();
                }
                else
                {
                    _isIndicatorAnimating = true;
                    StartVsyncTracker();
                }
            }
            catch
            {
                // 窗口刚初始化时容错
            }
        }

        private void ApplyIndicatorPosition()
        {
            CustomIndicator.Margin = new Thickness(_indicatorX, 0, 0, 0);
            CustomIndicator.Translation = new System.Numerics.Vector3(0, (float)_curIndicatorY, 0);
        }

        private void LoadFolders()
        {
            _folders.Clear();
            var list = _dbService.GetFolders();
            foreach (var folder in list)
            {
                _folders.Add(folder);
            }
        }

        private void RefreshSystemFonts()
        {
            _systemInstalledSet = FontNativeService.GetSystemInstalledFontNames();
            _systemFonts.Clear();
            foreach (var font in _systemInstalledSet.OrderBy(x => x))
            {
                _systemFonts.Add(font);
            }
        }

        private void RefreshMountedFonts()
        {
            _mountedFonts.Clear();
            foreach (var path in FontNativeService.GetLoadedFonts())
            {
                _mountedFonts.Add(System.IO.Path.GetFileName(path));
            }
        }

        private void UpdateProgressMultiBar()
        {
            int total = _assFonts.Count;
            if (total == 0)
            {
                _targetInst = 0;
                _targetMount = 0;
                _targetUnmount = 1;
                _targetErr = 0;
                StartVsyncTracker();
                TxtCountInstalled.Text = "0";
                TxtCountMounted.Text = "0";
                TxtCountTotal.Text = "0";
                return;
            }

            int installed = _assFonts.Count(f => f.IsSystemInstalled);
            int mounted = _assFonts.Count(f => f.IsLoaded && !f.IsSystemInstalled);
            int error = _assFonts.Count(f => !f.IsFound || f.HasError);
            int unmounted = Math.Max(0, total - installed - mounted - error);

            // 分别更新绿、蓝(主题色)、白三个独立数字
            TxtCountInstalled.Text = installed.ToString();
            TxtCountMounted.Text = mounted.ToString();
            TxtCountTotal.Text = total.ToString();

            _targetInst = installed;
            _targetMount = mounted;
            _targetUnmount = unmounted;
            _targetErr = error;

            StartVsyncTracker();
        }

        private void StartVsyncTracker()
        {
            if (!_isRenderingHooked)
            {
                _isRenderingHooked = true;
                _animStopwatch.Restart();
                _lastRenderingTime = _animStopwatch.Elapsed;
                CompositionTarget.Rendering += OnVsyncRendering;
            }
        }

        private void OnVsyncRendering(object? sender, object e)
        {
            var now = _animStopwatch.Elapsed;
            double dt = (now - _lastRenderingTime).TotalSeconds;
            _lastRenderingTime = now;

            if (dt > 0.05) dt = 0.05;
            if (dt <= 0) return;

            // 高帧率阻尼平滑跟踪算子（自动吃满200Hz等高刷新率屏幕）
            double factor = 1.0 - Math.Exp(-14.0 * dt);

            _curInst += (_targetInst - _curInst) * factor;
            _curMount += (_targetMount - _curMount) * factor;
            _curUnmount += (_targetUnmount - _curUnmount) * factor;
            _curErr += (_targetErr - _curErr) * factor;

            // 200Hz 阻尼跟踪小蓝条位置，由 GPU 合成器硬件加速执行 Translation 平移
            if (_isIndicatorAnimating)
            {
                double indFactor = 1.0 - Math.Exp(-18.0 * dt);
                _curIndicatorY += (_targetIndicatorY - _curIndicatorY) * indFactor;

                if (Math.Abs(_targetIndicatorY - _curIndicatorY) < 0.3)
                {
                    _curIndicatorY = _targetIndicatorY;
                    _isIndicatorAnimating = false;
                }

                ApplyIndicatorPosition();
            }

            // 右侧面板 200Hz 三次贝塞尔缓动浮入 (FadeIn + SlideUp)
            if (_isPanelTransitioning && _currentTransitionPanel != null)
            {
                _panelAnimTime += dt;
                double progress = Math.Min(1.0, _panelAnimTime / 0.22); // 220ms 经典时长
                // Cubic EaseOut 算子
                double ease = 1.0 - Math.Pow(1.0 - progress, 3);

                _currentTransitionPanel.Opacity = ease;
                _currentTransitionPanel.Translation = new System.Numerics.Vector3(0, (float)((1.0 - ease) * 16.0), 0);

                if (progress >= 1.0)
                {
                    _currentTransitionPanel.Opacity = 1.0;
                    _currentTransitionPanel.Translation = System.Numerics.Vector3.Zero;
                    _isPanelTransitioning = false;
                }
            }

            bool isProgressDone = Math.Abs(_targetInst - _curInst) < 0.002 &&
                                  Math.Abs(_targetMount - _curMount) < 0.002 &&
                                  Math.Abs(_targetUnmount - _curUnmount) < 0.002 &&
                                  Math.Abs(_targetErr - _curErr) < 0.002;

            if (isProgressDone && !_isIndicatorAnimating && !_isPanelTransitioning)
            {
                _curInst = _targetInst;
                _curMount = _targetMount;
                _curUnmount = _targetUnmount;
                _curErr = _targetErr;

                CompositionTarget.Rendering -= OnVsyncRendering;
                _isRenderingHooked = false;
                _animStopwatch.Stop();
            }

            ColInstalled.Width = new GridLength(Math.Max(0.0001, _curInst), GridUnitType.Star);
            ColMounted.Width = new GridLength(Math.Max(0.0001, _curMount), GridUnitType.Star);
            ColUnmounted.Width = new GridLength(Math.Max(0.0001, _curUnmount), GridUnitType.Star);
            ColError.Width = new GridLength(Math.Max(0.0001, _curErr), GridUnitType.Star);
        }

        private void UnloadMountedItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string fileName)
            {
                var fullPath = FontNativeService.GetLoadedFonts()
                    .FirstOrDefault(p => System.IO.Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(fullPath))
                {
                    FontNativeService.UnloadFont(fullPath);
                    RefreshMountedFonts();

                    var matched = _assFonts.FirstOrDefault(f => f.MatchedFilePath == fullPath);
                    if (matched != null)
                    {
                        matched.IsLoaded = false;
                    }
                    UpdateProgressMultiBar();
                }
            }
        }

        private async void SelectAssFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".ass");

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0)
            {
                return;
            }

            AssPathText.Text = files.Count == 1 ? files[0].Path : $"已选择 {files.Count} 个 ASS 字幕文件 (例如: {files[0].Name} 等)";
            _assFonts.Clear();
            _fileGroups.Clear();

            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileToFonts = new List<(string FileName, string FilePath, HashSet<string> Fonts)>();

            foreach (var file in files)
            {
                var fFonts = AssParserService.ParseFontsFromAssFile(file.Path);
                fileToFonts.Add((file.Name, file.Path, fFonts));
                foreach (var font in fFonts)
                {
                    uniqueNames.Add(font);
                }
            }

            // 1. 构建整体去重视图数据
            int matchedCount = 0;
            foreach (var name in uniqueNames)
            {
                bool isSystemInstalled = _systemInstalledSet.Contains(name);
                string? matchedPath = isSystemInstalled ? null : _dbService.MatchFontFilePath(name);
                bool isFound = isSystemInstalled || !string.IsNullOrEmpty(matchedPath);
                bool isLoaded = !string.IsNullOrEmpty(matchedPath) && FontNativeService.IsLoaded(matchedPath);

                if (isFound) matchedCount++;

                _assFonts.Add(new AssFontItem
                {
                    FontName = name,
                    IsFound = isFound,
                    MatchedFilePath = matchedPath,
                    IsLoaded = isLoaded,
                    IsSystemInstalled = isSystemInstalled,
                    SourceFileName = string.Empty
                });
            }

            // 2. 构建按 ASS 文件分组折叠数据源（默认折叠）
            foreach (var (fileName, filePath, fFonts) in fileToFonts)
            {
                var group = new AssFileGroup
                {
                    FileName = fileName,
                    FilePath = filePath,
                    Summary = $"{fFonts.Count} 个字体需求"
                };

                foreach (var name in fFonts)
                {
                    var baseItem = _assFonts.First(u => u.FontName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    group.Fonts.Add(new AssFontItem
                    {
                        FontName = name,
                        IsFound = baseItem.IsFound,
                        MatchedFilePath = baseItem.MatchedFilePath,
                        IsLoaded = baseItem.IsLoaded,
                        IsSystemInstalled = baseItem.IsSystemInstalled,
                        SourceFileName = fileName
                    });
                }

                _fileGroups.Add(group);
            }

            UpdateProgressMultiBar();
            RefreshViewMode();
            AssStatusText.Text = $"共解析 {files.Count} 个文件，整体去重后共 {_assFonts.Count} 个字体需求（已匹配 {matchedCount} 个）。";
        }

        private void ViewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshViewMode();
        }

        private void RefreshViewMode()
        {
            if (AssFontListView == null || AssGroupedListView == null || ViewModeComboBox == null) return;

            if (ViewModeComboBox.SelectedIndex == 1)
            {
                // 切换为按 ASS 文件折叠列表视图
                AssFontListView.Visibility = Visibility.Collapsed;
                AssGroupedListView.Visibility = Visibility.Visible;
            }
            else
            {
                // 切换为整体去重视图
                AssGroupedListView.Visibility = Visibility.Collapsed;
                AssFontListView.Visibility = Visibility.Visible;
            }
        }

        private async void MountAssFonts_Click(object sender, RoutedEventArgs e)
        {
            var targets = _assFonts.Where(i => i.IsFound && !i.IsSystemInstalled && !string.IsNullOrEmpty(i.MatchedFilePath)).ToList();
            if (targets.Count == 0)
            {
                AssStatusText.Text = "没有需要挂载的字体（或均已在系统中安装）。";
                return;
            }

            BtnMountAll.IsEnabled = false;

            int loaded = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var item = targets[i];
                bool ok = await Task.Run(() => FontNativeService.LoadFont(item.MatchedFilePath!));
                
                // 同步更新整体集合与按文件集合中相同路径的所有项
                SyncFontStatus(item.MatchedFilePath!, ok, ok ? "" : $"Windows GDI 接口返回挂载失败。\nWin32 错误码: {Marshal.GetLastWin32Error()}\n可能原因：字体文件损坏或格式不受支持。");
                if (ok)
                {
                    loaded++;
                    RefreshMountedFonts();
                }

                UpdateProgressMultiBar();
                await Task.Delay(40); // 留足40ms给高刷阻尼自然流动推进
            }

            BtnMountAll.IsEnabled = true;
            AssStatusText.Text = $"挂载完成：共尝试挂载 {targets.Count} 个，成功 {loaded} 个！";
        }

        private async void UnmountAssFonts_Click(object sender, RoutedEventArgs e)
        {
            var loadedItems = _assFonts.Where(i => i.IsLoaded && !string.IsNullOrEmpty(i.MatchedFilePath)).ToList();
            if (loadedItems.Count == 0)
            {
                AssStatusText.Text = "当前字幕没有已挂载的字体。";
                return;
            }

            for (int i = loadedItems.Count - 1; i >= 0; i--)
            {
                var item = loadedItems[i];
                FontNativeService.UnloadFont(item.MatchedFilePath!);
                SyncFontStatus(item.MatchedFilePath!, false, "");
                RefreshMountedFonts();

                UpdateProgressMultiBar();
                await Task.Delay(40);
            }

            AssStatusText.Text = $"卸载完成：已安全卸载 {loadedItems.Count} 个字体。";
        }

        private void AssFontItemActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AssFontItem item && !string.IsNullOrEmpty(item.MatchedFilePath))
            {
                if (item.IsLoaded)
                {
                    if (FontNativeService.UnloadFont(item.MatchedFilePath))
                    {
                        SyncFontStatus(item.MatchedFilePath, false, "");
                    }
                }
                else
                {
                    if (FontNativeService.LoadFont(item.MatchedFilePath))
                    {
                        SyncFontStatus(item.MatchedFilePath, true, "");
                    }
                    else
                    {
                        int win32Err = Marshal.GetLastWin32Error();
                        SyncFontStatus(item.MatchedFilePath, false, $"Windows GDI 接口返回挂载失败。\nWin32 错误码: {win32Err}\n可能原因：字体文件损坏或格式不受支持。");
                    }
                }
                RefreshMountedFonts();
                UpdateProgressMultiBar();
            }
        }

        private void SyncFontStatus(string filePath, bool isLoaded, string errorMsg)
        {
            // 同步更新整体去重列表中的状态
            foreach (var f in _assFonts.Where(x => x.MatchedFilePath == filePath))
            {
                f.IsLoaded = isLoaded;
                f.HasError = !string.IsNullOrEmpty(errorMsg);
                f.ErrorMessage = errorMsg;
            }

            // 同步更新每个折叠文件组内部相同路径的字体状态
            foreach (var group in _fileGroups)
            {
                foreach (var f in group.Fonts.Where(x => x.MatchedFilePath == filePath))
                {
                    f.IsLoaded = isLoaded;
                    f.HasError = !string.IsNullOrEmpty(errorMsg);
                    f.ErrorMessage = errorMsg;
                }
            }
        }

        private async void ShowErrorDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AssFontItem item)
            {
                var dialog = new ContentDialog
                {
                    Title = $"挂载失败详情: {item.FontName}",
                    Content = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(item.ErrorMessage) ? "未知错误，未能获取到底层系统报错详情。" : item.ErrorMessage,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.9
                    },
                    CloseButtonText = "确定",
                    XamlRoot = this.Content.XamlRoot
                };

                await dialog.ShowAsync();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteSearch();
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ExecuteSearch();
            }
        }

        private void ExecuteSearch()
        {
            string keyword = SearchBox.Text.Trim();
            _searchResults.Clear();

            if (string.IsNullOrEmpty(keyword))
            {
                SearchResultCountText.Text = "请输入关键词后搜索。";
                return;
            }

            var results = _dbService.SearchFonts(keyword);
            foreach (var item in results)
            {
                _searchResults.Add(item);
            }

            SearchResultCountText.Text = $"搜索到 {_searchResults.Count} 条匹配的字体记录。";
        }

        private void SearchFontItemToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is FontRecord record)
            {
                if (FontNativeService.IsLoaded(record.FilePath))
                {
                    FontNativeService.UnloadFont(record.FilePath);
                }
                else
                {
                    FontNativeService.LoadFont(record.FilePath);
                }
            }
        }

        private async void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                if (_dbService.AddFolder(folder.Path))
                {
                    LoadFolders();
                    ScanStatusText.Text = $"已添加目录：{folder.Path}，请点击【立即扫描并重建索引】生效。";
                }
            }
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string folderPath)
            {
                if (_dbService.RemoveFolder(folderPath))
                {
                    LoadFolders();
                    ScanStatusText.Text = $"已移除目录：{folderPath}。";
                }
            }
        }

        private async void RebuildIndex_Click(object sender, RoutedEventArgs e)
        {
            ScanProgressBar.Visibility = Visibility.Visible;
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
            }

            var progress = new Progress<string>(status =>
            {
                ScanStatusText.Text = status;
            });

            await _dbService.RebuildIndexAsync(progress);

            ScanProgressBar.Visibility = Visibility.Collapsed;
            if (sender is Button rebuildBtn)
            {
                rebuildBtn.IsEnabled = true;
            }
        }

        private void UnloadAllFonts_Click(object sender, RoutedEventArgs e)
        {
            FontNativeService.UnloadAll();
            foreach (var font in _assFonts)
            {
                font.IsLoaded = false;
            }
            ScanStatusText.Text = "已将所有挂载在系统中的临时字体卸载释放完毕。";
        }
    }
}
