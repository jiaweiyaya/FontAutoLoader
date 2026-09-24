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
        private readonly ObservableCollection<SearchFontDisplayItem> _searchResults = new();
        private readonly List<SearchFontDisplayItem> _allSearchResults = new();
        private int _searchCurrentPage = 1;
        private int _searchTotalPages = 1;
        private const int SearchPageSize = 50;
        private readonly ObservableCollection<string> _folders = new();
        private readonly ObservableCollection<string> _mountedFonts = new();
        private readonly ObservableCollection<InstalledFontDisplayItem> _installedDisplayFonts = new();
        private readonly List<InstalledFontDisplayItem> _allInstalledFontsCache = new();
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

        // 左侧字体状态面板 200Hz 平滑滑动折叠动画状态
        private double _curDrawerWidth = 360;
        private double _targetDrawerWidth = 360;
        private bool _isDrawerAnimating = false;

        public MainWindow()
        {
            InitializeComponent();

            RootWindowGrid.SizeChanged += (s, e) => UpdateCustomIndicator(false);

            AssFontListView.ItemsSource = _assFonts;
            AssGroupedListView.ItemsSource = _fileGroups;
            SearchListView.ItemsSource = _searchResults;
            FolderListView.ItemsSource = _folders;
            MountedFontListView.ItemsSource = _mountedFonts;
            InstalledFontsListView.ItemsSource = _installedDisplayFonts;

            LoadFolders();
            RefreshSystemFonts();
            LoadInstalledFontManagementList();
            RefreshSearchSummary();

            // 拦截应用窗口关闭事件，以便执行带进度条的平滑卸载
            this.AppWindow.Closing += MainWindow_AppWindowClosing;
        }

        private bool _isForceClosing = false;
        private bool _isOperationRunning = false;
        private bool _isCancelRequested = false;

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
            _targetDrawerWidth = _isLeftDrawerExpanded ? 360 : 0;

            if (_isLeftDrawerExpanded)
            {
                LeftFontSidePanel.Visibility = Visibility.Visible;
                BtnExpandDrawer.Visibility = Visibility.Collapsed;
            }

            _isDrawerAnimating = true;
            StartVsyncTracker();
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
                "InstalledPage" => InstalledPagePanel,
                "FolderPage" => FolderPagePanel,
                _ => AssPagePanel
            };

            // 隐藏其余面板
            AssPagePanel.Visibility = (target == AssPagePanel) ? Visibility.Visible : Visibility.Collapsed;
            SearchPagePanel.Visibility = (target == SearchPagePanel) ? Visibility.Visible : Visibility.Collapsed;
            InstalledPagePanel.Visibility = (target == InstalledPagePanel) ? Visibility.Visible : Visibility.Collapsed;
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
        }

        private void RefreshMountedFonts()
        {
            var currentLoaded = FontNativeService.GetLoadedFonts()
                .Select(System.IO.Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 移除已不存在于系统挂载中的项
            for (int i = _mountedFonts.Count - 1; i >= 0; i--)
            {
                if (!currentLoaded.Contains(_mountedFonts[i]))
                {
                    _mountedFonts.RemoveAt(i);
                }
            }

            // 增量追加新挂载的字体
            foreach (var name in currentLoaded)
            {
                if (!_mountedFonts.Contains(name!, StringComparer.OrdinalIgnoreCase))
                {
                    _mountedFonts.Add(name!);
                }
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

            // 200Hz 连续阻尼滑动折叠/展开左侧抽屉面板
            if (_isDrawerAnimating)
            {
                double drawerFactor = 1.0 - Math.Exp(-16.0 * dt);
                _curDrawerWidth += (_targetDrawerWidth - _curDrawerWidth) * drawerFactor;

                LeftFontSidePanel.Opacity = Math.Clamp(_curDrawerWidth / 360.0, 0.0, 1.0);
                LeftColDef.Width = new GridLength(Math.Max(0, _curDrawerWidth));

                if (Math.Abs(_targetDrawerWidth - _curDrawerWidth) < 0.8)
                {
                    _curDrawerWidth = _targetDrawerWidth;
                    LeftColDef.Width = new GridLength(_targetDrawerWidth);
                    LeftFontSidePanel.Opacity = _isLeftDrawerExpanded ? 1.0 : 0.0;

                    if (!_isLeftDrawerExpanded)
                    {
                        LeftFontSidePanel.Visibility = Visibility.Collapsed;
                        BtnExpandDrawer.Visibility = Visibility.Visible;
                    }

                    _isDrawerAnimating = false;
                }
            }

            bool isProgressDone = Math.Abs(_targetInst - _curInst) < 0.002 &&
                                  Math.Abs(_targetMount - _curMount) < 0.002 &&
                                  Math.Abs(_targetUnmount - _curUnmount) < 0.002 &&
                                  Math.Abs(_targetErr - _curErr) < 0.002;

            if (isProgressDone && !_isIndicatorAnimating && !_isPanelTransitioning && !_isDrawerAnimating)
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

                    SyncFontStatus(fullPath, false, "");
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
            if (_isOperationRunning)
            {
                _isCancelRequested = true;
                BtnMountAll.Content = "正在暂停...";
                BtnMountAll.IsEnabled = false;
                return;
            }

            var targets = _assFonts.Where(i => i.IsFound && !i.IsSystemInstalled && !string.IsNullOrEmpty(i.MatchedFilePath)).ToList();
            if (targets.Count == 0)
            {
                AssStatusText.Text = "没有需要挂载的字体（或均已在系统中安装）。";
                return;
            }

            _isOperationRunning = true;
            _isCancelRequested = false;
            BtnMountAll.Content = "暂停挂载";
            BtnUnmountAss.IsEnabled = false;
            BtnUnloadAll.IsEnabled = false;

            int loaded = 0;
            int processed = 0;

            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var item = targets[i];
                    bool ok = await Task.Run(() => FontNativeService.LoadFont(item.MatchedFilePath!));

                    SyncFontStatus(item.MatchedFilePath!, ok, ok ? "" : $"Windows GDI 接口返回挂载失败。\nWin32 错误码: {Marshal.GetLastWin32Error()}\n可能原因：字体文件损坏或格式不受支持。");
                    if (ok)
                    {
                        loaded++;
                        RefreshMountedFonts();
                    }

                    UpdateProgressMultiBar();
                    processed++;
                    await Task.Delay(40);

                    if (_isCancelRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _isOperationRunning = false;
                BtnMountAll.Content = "挂载字幕字体";
                BtnMountAll.IsEnabled = true;
                BtnUnmountAss.IsEnabled = true;
                BtnUnloadAll.IsEnabled = true;
            }

            if (_isCancelRequested)
            {
                AssStatusText.Text = $"已暂停挂载：已处理 {processed} 个，成功挂载 {loaded} 个，剩余 {targets.Count - processed} 个未处理。";
            }
            else
            {
                AssStatusText.Text = $"挂载完成：共尝试挂载 {targets.Count} 个，成功 {loaded} 个！";
            }
        }

        private async void UnmountAssFonts_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning)
            {
                _isCancelRequested = true;
                BtnUnmountAss.Content = "正在暂停...";
                BtnUnmountAss.IsEnabled = false;
                return;
            }

            var loadedItems = _assFonts.Where(i => i.IsLoaded && !string.IsNullOrEmpty(i.MatchedFilePath)).ToList();
            if (loadedItems.Count == 0)
            {
                AssStatusText.Text = "当前字幕没有已挂载的字体。";
                return;
            }

            _isOperationRunning = true;
            _isCancelRequested = false;
            BtnUnmountAss.Content = "暂停卸载";
            BtnMountAll.IsEnabled = false;
            BtnUnloadAll.IsEnabled = false;

            int unloaded = 0;
            int totalToUnload = loadedItems.Count;

            try
            {
                for (int i = loadedItems.Count - 1; i >= 0; i--)
                {
                    var item = loadedItems[i];
                    FontNativeService.UnloadFont(item.MatchedFilePath!);
                    SyncFontStatus(item.MatchedFilePath!, false, "");
                    RefreshMountedFonts();
                    UpdateProgressMultiBar();
                    unloaded++;

                    await Task.Delay(40);

                    if (_isCancelRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _isOperationRunning = false;
                BtnUnmountAss.Content = "卸载字幕字体";
                BtnUnmountAss.IsEnabled = true;
                BtnMountAll.IsEnabled = true;
                BtnUnloadAll.IsEnabled = true;
            }

            if (_isCancelRequested)
            {
                AssStatusText.Text = $"已暂停卸载：已卸载 {unloaded} 个字幕字体，剩余 {totalToUnload - unloaded} 个未卸载。";
            }
            else
            {
                AssStatusText.Text = $"卸载完成：已安全卸载 {unloaded} 个字幕字体。";
            }
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
            foreach (var f in _assFonts.Where(x => string.Equals(x.MatchedFilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                f.IsLoaded = isLoaded;
                f.HasError = !string.IsNullOrEmpty(errorMsg);
                f.ErrorMessage = errorMsg;
            }

            // 同步更新每个折叠文件组内部相同路径的字体状态
            foreach (var group in _fileGroups)
            {
                foreach (var f in group.Fonts.Where(x => string.Equals(x.MatchedFilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    f.IsLoaded = isLoaded;
                    f.HasError = !string.IsNullOrEmpty(errorMsg);
                    f.ErrorMessage = errorMsg;
                }
            }

            // 同步更新字体库检索结果及全量缓存中的挂载状态
            foreach (var s in _allSearchResults.Where(x => string.Equals(x.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                s.IsLoaded = isLoaded;
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

        private void RefreshSearchSummary()
        {
            int total = _dbService.GetTotalFontCount();
            TxtSearchSummary.Text = $"字体库共收录 {total} 个字体文件。";
        }

        private void ExecuteSearch()
        {
            string keyword = SearchBox.Text.Trim();
            _searchResults.Clear();
            _allSearchResults.Clear();

            if (string.IsNullOrEmpty(keyword))
            {
                RefreshSearchSummary();
                SearchPaginationPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var results = _dbService.SearchFonts(keyword);
            foreach (var r in results)
            {
                bool isSys = _systemInstalledSet.Contains(r.FontName) || _systemInstalledSet.Contains(r.FamilyName);
                bool isLoaded = FontNativeService.IsLoaded(r.FilePath);
                _allSearchResults.Add(new SearchFontDisplayItem(r, isSys, isLoaded));
            }

            if (_allSearchResults.Count == 0)
            {
                TxtSearchSummary.Text = $"未检索到包含 \"{keyword}\" 的字体文件。";
                SearchPaginationPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _searchTotalPages = (int)Math.Ceiling((double)_allSearchResults.Count / SearchPageSize);
            _searchCurrentPage = 1;

            UpdateSearchPageDisplay();
        }

        private void UpdateSearchPageDisplay()
        {
            if (_searchTotalPages <= 1)
            {
                SearchPaginationPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                SearchPaginationPanel.Visibility = Visibility.Visible;
            }

            _searchCurrentPage = Math.Clamp(_searchCurrentPage, 1, Math.Max(1, _searchTotalPages));

            TxtCurrentPage.Text = _searchCurrentPage.ToString();
            TxtTotalPages.Text = _searchTotalPages.ToString();

            BtnPrevPage.IsEnabled = _searchCurrentPage > 1;
            BtnNextPage.IsEnabled = _searchCurrentPage < _searchTotalPages;

            _searchResults.Clear();
            var pageItems = _allSearchResults
                .Skip((_searchCurrentPage - 1) * SearchPageSize)
                .Take(SearchPageSize);

            foreach (var item in pageItems)
            {
                _searchResults.Add(item);
            }

            int start = (_searchCurrentPage - 1) * SearchPageSize + 1;
            int end = Math.Min(_searchCurrentPage * SearchPageSize, _allSearchResults.Count);
            int totalAll = _dbService.GetTotalFontCount();
            TxtSearchSummary.Text = $"共检索到 {_allSearchResults.Count} 条记录，当前显示第 {start} - {end} 条 (全库共 {totalAll} 个字体)。";
        }

        private void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_searchCurrentPage > 1)
            {
                _searchCurrentPage--;
                UpdateSearchPageDisplay();
            }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_searchCurrentPage < _searchTotalPages)
            {
                _searchCurrentPage++;
                UpdateSearchPageDisplay();
            }
        }

        private void TxtCurrentPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitPageJump();
            }
        }

        private void TxtCurrentPage_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitPageJump();
        }

        private void CommitPageJump()
        {
            if (int.TryParse(TxtCurrentPage.Text.Trim(), out int page))
            {
                page = Math.Clamp(page, 1, _searchTotalPages);
                if (page != _searchCurrentPage)
                {
                    _searchCurrentPage = page;
                    UpdateSearchPageDisplay();
                }
                else
                {
                    TxtCurrentPage.Text = _searchCurrentPage.ToString();
                }
            }
            else
            {
                TxtCurrentPage.Text = _searchCurrentPage.ToString();
            }
        }

        private void SearchFontItemToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SearchFontDisplayItem item)
            {
                if (item.IsLoaded)
                {
                    if (FontNativeService.UnloadFont(item.FilePath))
                    {
                        SyncFontStatus(item.FilePath, false, "");
                    }
                }
                else
                {
                    if (FontNativeService.LoadFont(item.FilePath))
                    {
                        SyncFontStatus(item.FilePath, true, "");
                    }
                }

                RefreshMountedFonts();
                UpdateProgressMultiBar();
            }
        }

        private void LocateInstalledFont_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not SearchFontDisplayItem searchItem)
            {
                return;
            }

            // 1. 触发流畅页面淡入微浮动效，切到已安装字体管理页
            SwitchPageWithAnimation("InstalledPage");

            // 2. 同步左侧主导航指示条位置
            foreach (var navObj in NavView.MenuItems)
            {
                if (navObj is NavigationViewItem navItem && navItem.Tag?.ToString() == "InstalledPage")
                {
                    NavView.SelectedItem = navItem;
                    UpdateCustomIndicator(true);
                    break;
                }
            }

            // 3. 清空已安装管理页中的搜索框，展示全量字体
            SearchInstalledBox.Text = string.Empty;

            // 4. 寻找匹配的已安装字体并高亮定位
            var target = _installedDisplayFonts.FirstOrDefault(f => f.FontName.Equals(searchItem.FontName, StringComparison.OrdinalIgnoreCase))
                      ?? _installedDisplayFonts.FirstOrDefault(f => f.FontName.Equals(searchItem.FamilyName, StringComparison.OrdinalIgnoreCase))
                      ?? _installedDisplayFonts.FirstOrDefault(f => f.FontName.Contains(searchItem.FontName, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                foreach (var item in _installedDisplayFonts)
                {
                    item.IsHighlighted = false;
                }

                target.IsHighlighted = true;
                InstalledFontsListView.ScrollIntoView(target);
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
            RefreshSearchSummary();
            if (sender is Button rebuildBtn)
            {
                rebuildBtn.IsEnabled = true;
            }
        }

        private async void UnloadAllFonts_Click(object sender, RoutedEventArgs e)
        {
            if (_isOperationRunning)
            {
                _isCancelRequested = true;
                BtnUnloadAll.Content = "正在暂停...";
                BtnUnloadAll.IsEnabled = false;
                return;
            }

            var loadedList = FontNativeService.GetLoadedFonts().ToList();
            if (loadedList.Count == 0)
            {
                AssStatusText.Text = "当前系统中没有已挂载的临时字体。";
                ScanStatusText.Text = "当前系统中没有已挂载的临时字体。";
                return;
            }

            _isOperationRunning = true;
            _isCancelRequested = false;
            BtnUnloadAll.Content = "暂停卸载";
            BtnMountAll.IsEnabled = false;
            BtnUnmountAss.IsEnabled = false;

            // 识别当前已匹配到的字幕字体文件路径集合
            var assFontPaths = _assFonts
                .Where(f => !string.IsNullOrEmpty(f.MatchedFilePath))
                .Select(f => f.MatchedFilePath!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int unloadedAss = 0;
            int unloadedOther = 0;

            try
            {
                for (int i = loadedList.Count - 1; i >= 0; i--)
                {
                    var fontPath = loadedList[i];
                    bool isAssFont = assFontPaths.Contains(fontPath);

                    FontNativeService.UnloadFont(fontPath);
                    SyncFontStatus(fontPath, false, "");
                    RefreshMountedFonts();
                    UpdateProgressMultiBar();

                    if (isAssFont)
                    {
                        unloadedAss++;
                    }
                    else
                    {
                        unloadedOther++;
                    }

                    await Task.Delay(20);

                    if (_isCancelRequested)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _isOperationRunning = false;
                BtnUnloadAll.Content = "卸载所有字体";
                BtnUnloadAll.IsEnabled = true;
                BtnMountAll.IsEnabled = true;
                BtnUnmountAss.IsEnabled = true;
            }

            string resultMsg = $"已成功卸载 {unloadedOther} 个临时字体 + {unloadedAss} 个字幕字体。";
            if (_isCancelRequested)
            {
                resultMsg = $"已暂停卸载：已成功卸载 {unloadedOther} 个临时字体 + {unloadedAss} 个字幕字体。";
            }

            AssStatusText.Text = resultMsg;
            ScanStatusText.Text = resultMsg;
        }

        private void RefreshInstalledFonts_Click(object sender, RoutedEventArgs e)
        {
            RefreshSystemFonts();
            LoadInstalledFontManagementList();
        }

        private void LoadInstalledFontManagementList()
        {
            _allInstalledFontsCache.Clear();
            var list = FontNativeService.GetSystemInstalledFontInfos();

            foreach (var info in list.OrderBy(f => f.FontName))
            {
                var risk = EvaluateFontRisk(info.FontName);
                _allInstalledFontsCache.Add(new InstalledFontDisplayItem(info, risk));
            }

            ApplyInstalledFontFilter();
        }

        private void SearchInstalledBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyInstalledFontFilter();
        }

        private void ApplyInstalledFontFilter()
        {
            string keyword = SearchInstalledBox?.Text.Trim() ?? string.Empty;
            _installedDisplayFonts.Clear();

            var matches = string.IsNullOrEmpty(keyword)
                ? _allInstalledFontsCache
                : _allInstalledFontsCache.Where(f =>
                    f.FontName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    f.Info.FontFileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    f.Info.FilePath.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            foreach (var item in matches)
            {
                _installedDisplayFonts.Add(item);
            }

            if (TxtInstalledFontSummary != null)
            {
                TxtInstalledFontSummary.Text = string.IsNullOrEmpty(keyword)
                    ? $"共读取到 {_allInstalledFontsCache.Count} 个已安装字体，包含系统核心/预装与自定义扩展。"
                    : $"搜索到 {_installedDisplayFonts.Count} 个匹配项 (全系统共 {_allInstalledFontsCache.Count} 个)。";
            }
        }

        private static FontRiskLevel EvaluateFontRisk(string fontName)
        {
            // Windows 核心必需字体列表（红色级别：删了系统界面会崩或乱码）
            string[] criticalKeys =
            [
                "Segoe UI", "Segoe MDL2", "Segoe Fluent", "Segoe UI Emoji", "Segoe UI Symbol", "Segoe UI Historic",
                "Marlett", "Symbol", "Wingdings", "Webdings",
                "Tahoma", "Arial", "Courier New", "Times New Roman", "Verdana",
                "Microsoft YaHei", "微软雅黑", "SimSun", "NSimSun", "宋体", "新宋体",
                "MS Gothic", "MS PGothic", "MS UI Gothic", "Meiryo", "Yu Gothic",
                "Malgun Gothic", "Batang", "Gulim",
                "MingLiU", "PMingLiU", "Microsoft JhengHei", "微软正黑体"
            ];

            // Windows 预装/推荐保留字体列表（黄色级别：不建议删除，可能导致特定程序或网页显示异常）
            string[] warningKeys =
            [
                "SimHei", "黑体", "KaiTi", "楷体", "FangSong", "仿宋",
                "Calibri", "Cambria", "Candara", "Consolas", "Constantia", "Corbel",
                "Comic Sans", "Impact", "Trebuchet", "Georgia", "Palatino",
                "Segoe Print", "Segoe Script", "Bahnschrift", "Ebrima", "Gadugi",
                "Leelawadee", "Nirmala", "Ink Free", "Gabriola", "Sitka", "Sylfaen"
            ];

            foreach (var k in criticalKeys)
            {
                if (fontName.Contains(k, StringComparison.OrdinalIgnoreCase))
                {
                    return FontRiskLevel.Critical;
                }
            }

            foreach (var k in warningKeys)
            {
                if (fontName.Contains(k, StringComparison.OrdinalIgnoreCase))
                {
                    return FontRiskLevel.Warning;
                }
            }

            return FontRiskLevel.Safe;
        }

        private async void DeleteInstalledFont_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not InstalledFontDisplayItem item)
            {
                return;
            }

            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                CloseButtonText = "取消"
            };

            if (item.Risk == FontRiskLevel.Critical)
            {
                // 红色高危警示弹窗
                dialog.Title = "危险警告：系统核心必需字体";
                dialog.PrimaryButtonText = "执意强行删除";
                dialog.DefaultButton = ContentDialogButton.Close;

                var sp = new StackPanel { Spacing = 10 };
                var headerText = new TextBlock
                {
                    Text = $"【严重风险】字体 \"{item.FontName}\" 是 Windows 系统必需的核心依赖字体！",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 57, 53)),
                    TextWrapping = TextWrapping.Wrap
                };
                var tipText = new TextBlock
                {
                    Text = "删除此字体将极大概率导致 Windows 窗口按钮乱码、系统设置崩溃或桌面文字无法正常渲染！\n\n强烈建议不要删除该字体。您确定要继续强行尝试删除它吗？",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.9
                };
                sp.Children.Add(headerText);
                sp.Children.Add(tipText);
                dialog.Content = sp;
            }
            else if (item.Risk == FontRiskLevel.Warning)
            {
                // 黄色提示警示弹窗
                dialog.Title = "提示：系统默认推荐字体";
                dialog.PrimaryButtonText = "确认删除";
                dialog.DefaultButton = ContentDialogButton.Close;

                var sp = new StackPanel { Spacing = 10 };
                var headerText = new TextBlock
                {
                    Text = $"【注意】字体 \"{item.FontName}\" 是 Windows 系统预装的默认字体或常用办公字体。",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 192, 45)),
                    TextWrapping = TextWrapping.Wrap
                };
                var tipText = new TextBlock
                {
                    Text = "删除此字体可能导致部分软件、网页排版或特定字幕显示异常。\n\n您确定要从系统中将其移除吗？",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.9
                };
                sp.Children.Add(headerText);
                sp.Children.Add(tipText);
                dialog.Content = sp;
            }
            else
            {
                // 普通安全确认弹窗
                dialog.Title = "确认删除字体";
                dialog.PrimaryButtonText = "删除";
                dialog.DefaultButton = ContentDialogButton.Primary;
                dialog.Content = new TextBlock
                {
                    Text = $"确定要从系统中卸载并删除字体 \"{item.FontName}\" 吗？此操作将移除该字体的注册表引用及物理文件。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.9
                };
            }

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                bool ok = FontNativeService.DeleteSystemInstalledFont(item.Info, out string error);
                if (ok)
                {
                    _allInstalledFontsCache.Remove(item);
                    _installedDisplayFonts.Remove(item);
                    RefreshSystemFonts();
                    TxtInstalledFontSummary.Text = $"已成功从系统中移除字体：{item.FontName}";
                }
                else
                {
                    var errDialog = new ContentDialog
                    {
                        Title = "删除失败",
                        Content = new TextBlock
                        {
                            Text = $"无法删除字体 \"{item.FontName}\"。\n\n系统返回错误信息：\n{error}\n\n可能原因：操作需要管理员权限（系统盘 Windows\\Fonts 目录通常受只读保护），或该字体正在被系统/播放器程序占用。",
                            TextWrapping = TextWrapping.Wrap
                        },
                        CloseButtonText = "确定",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await errDialog.ShowAsync();
                }
            }
        }
    }

    public enum FontRiskLevel
    {
        Critical,
        Warning,
        Safe
    }

    public class InstalledFontDisplayItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public InstalledFontInfo Info { get; }
        public FontRiskLevel Risk { get; }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsHighlighted)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BorderBrush)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BorderThickness)));
                }
            }
        }

        public string FontName => Info.FontName;
        public string FileInfoText => $"{Info.FontFileName} • {(Info.IsCurrentUser ? "当前用户安装" : "系统全局安装")}";

        public string RiskTagText => Risk switch
        {
            FontRiskLevel.Critical => "系统核心必需 (严禁删除)",
            FontRiskLevel.Warning => "系统默认预装 (不建议删除)",
            _ => "扩展/用户字体 (安全)"
        };

        public SolidColorBrush BadgeBackgroundBrush => Risk switch
        {
            FontRiskLevel.Critical => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 211, 47, 47)),
            FontRiskLevel.Warning => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 192, 45)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 142, 60))
        };

        public SolidColorBrush BadgeForegroundBrush => Risk switch
        {
            FontRiskLevel.Warning => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 33, 33, 33)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
        };

        public SolidColorBrush CardBackgroundBrush => Risk switch
        {
            FontRiskLevel.Critical => new SolidColorBrush(Windows.UI.Color.FromArgb(35, 211, 47, 47)),
            FontRiskLevel.Warning => new SolidColorBrush(Windows.UI.Color.FromArgb(25, 251, 192, 45)),
            _ => new SolidColorBrush(Windows.UI.Color.FromArgb(10, 255, 255, 255))
        };

        // 高亮状态下呈现加粗的亮蓝色重点聚焦外框
        public SolidColorBrush BorderBrush => IsHighlighted
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215))
            : Risk switch
            {
                FontRiskLevel.Critical => new SolidColorBrush(Windows.UI.Color.FromArgb(140, 211, 47, 47)),
                FontRiskLevel.Warning => new SolidColorBrush(Windows.UI.Color.FromArgb(120, 251, 192, 45)),
                _ => new SolidColorBrush(Windows.UI.Color.FromArgb(35, 128, 128, 128))
            };

        public Thickness BorderThickness => IsHighlighted ? new Thickness(2) : new Thickness(1);

        public InstalledFontDisplayItem(InstalledFontInfo info, FontRiskLevel risk)
        {
            Info = info;
            Risk = risk;
        }
    }

    public class SearchFontDisplayItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public FontRecord Record { get; }
        public bool IsSystemInstalled { get; }

        private bool _isLoaded;
        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                if (_isLoaded != value)
                {
                    _isLoaded = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsLoaded)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ActionButtonText)));
                }
            }
        }

        public string FontName => Record.FontName;
        public string FamilyName => Record.FamilyName;
        public string FilePath => Record.FilePath;
        public string Format => Record.Format;
        public Brush BackgroundBrush => Record.BackgroundBrush;
        public Visibility WarningVisibility => Record.WarningVisibility;
        public Visibility CorruptedVisibility => Record.CorruptedVisibility;

        public Visibility ActionButtonVisibility => IsSystemInstalled ? Visibility.Collapsed : Visibility.Visible;
        public Visibility LocateButtonVisibility => IsSystemInstalled ? Visibility.Visible : Visibility.Collapsed;
        public string ActionButtonText => IsLoaded ? "卸载" : "挂载";

        public SearchFontDisplayItem(FontRecord record, bool isSystemInstalled, bool isLoaded)
        {
            Record = record;
            IsSystemInstalled = isSystemInstalled;
            _isLoaded = isLoaded;
        }
    }
}