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
        private readonly ObservableCollection<AssFontItem> _assFonts = new();
        private readonly ObservableCollection<FontRecord> _searchResults = new();
        private readonly ObservableCollection<string> _folders = new();
        private readonly ObservableCollection<string> _mountedFonts = new();
        private readonly ObservableCollection<string> _systemFonts = new();
        private HashSet<string> _systemInstalledSet = new(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            InitializeComponent();

            AssFontListView.ItemsSource = _assFonts;
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

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                string tag = item.Tag?.ToString() ?? string.Empty;
                AssPagePanel.Visibility = tag == "AssPage" ? Visibility.Visible : Visibility.Collapsed;
                SearchPagePanel.Visibility = tag == "SearchPage" ? Visibility.Visible : Visibility.Collapsed;
                FolderPagePanel.Visibility = tag == "FolderPage" ? Visibility.Visible : Visibility.Collapsed;
            }
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
                ColInstalled.Width = new GridLength(0, GridUnitType.Star);
                ColMounted.Width = new GridLength(0, GridUnitType.Star);
                ColUnmounted.Width = new GridLength(1, GridUnitType.Star);
                ColError.Width = new GridLength(0, GridUnitType.Star);
                MountProgressCountText.Text = "0 / 0 / 0";
                return;
            }

            int installed = _assFonts.Count(f => f.IsSystemInstalled);
            int mounted = _assFonts.Count(f => f.IsLoaded && !f.IsSystemInstalled);
            int error = _assFonts.Count(f => !f.IsFound || f.HasError);
            int unmounted = total - installed - mounted - error;
            if (unmounted < 0) unmounted = 0;

            ColInstalled.Width = new GridLength(installed, GridUnitType.Star);
            ColMounted.Width = new GridLength(mounted, GridUnitType.Star);
            ColUnmounted.Width = new GridLength(unmounted, GridUnitType.Star);
            ColError.Width = new GridLength(error, GridUnitType.Star);

            // 右上角格式：系统已安装 / 目前已挂载 / 字幕中的总字体数
            MountProgressCountText.Text = $"{installed} / {mounted} / {total}";
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

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                return;
            }

            AssPathText.Text = file.Path;
            _assFonts.Clear();

            var fontNames = AssParserService.ParseFontsFromAssFile(file.Path);
            int matchedCount = 0;

            foreach (var name in fontNames)
            {
                bool isSystemInstalled = _systemInstalledSet.Contains(name);
                string? matchedPath = isSystemInstalled ? null : _dbService.MatchFontFilePath(name);
                bool isFound = isSystemInstalled || !string.IsNullOrEmpty(matchedPath);
                bool isLoaded = !string.IsNullOrEmpty(matchedPath) && FontNativeService.IsLoaded(matchedPath);

                if (isFound)
                {
                    matchedCount++;
                }

                _assFonts.Add(new AssFontItem
                {
                    FontName = name,
                    IsFound = isFound,
                    MatchedFilePath = matchedPath,
                    IsLoaded = isLoaded,
                    IsSystemInstalled = isSystemInstalled
                });
            }

            UpdateProgressMultiBar();
            AssStatusText.Text = $"共解析出 {_assFonts.Count} 个字体需求，本地索引库已匹配 {matchedCount} 个。";
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
                if (ok)
                {
                    item.IsLoaded = true;
                    item.HasError = false;
                    loaded++;
                }
                else
                {
                    item.HasError = true;
                }

                UpdateProgressMultiBar();
                await Task.Delay(25); // 顺畅的渲染动画
            }

            RefreshMountedFonts();
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

            // 倒退动画：从后往前逐个卸载，蓝色进度向左倒退缩回，灰色增多
            for (int i = loadedItems.Count - 1; i >= 0; i--)
            {
                var item = loadedItems[i];
                FontNativeService.UnloadFont(item.MatchedFilePath!);
                item.IsLoaded = false;

                UpdateProgressMultiBar();
                await Task.Delay(25);
            }

            RefreshMountedFonts();
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
                        item.IsLoaded = false;
                    }
                }
                else
                {
                    if (FontNativeService.LoadFont(item.MatchedFilePath))
                    {
                        item.IsLoaded = true;
                        item.HasError = false;
                    }
                    else
                    {
                        item.HasError = true;
                    }
                }
                RefreshMountedFonts();
                UpdateProgressMultiBar();
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
