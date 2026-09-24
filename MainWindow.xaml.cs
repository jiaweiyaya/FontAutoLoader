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

        public MainWindow()
        {
            InitializeComponent();

            AssFontListView.ItemsSource = _assFonts;
            SearchListView.ItemsSource = _searchResults;
            FolderListView.ItemsSource = _folders;

            LoadFolders();

            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            // 窗口关闭时自动释放所有临时加载的字体，保持系统干净
            FontNativeService.UnloadAll();
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
                string? matchedPath = _dbService.MatchFontFilePath(name);
                bool isFound = !string.IsNullOrEmpty(matchedPath);
                bool isLoaded = isFound && FontNativeService.IsLoaded(matchedPath!);

                if (isFound)
                {
                    matchedCount++;
                }

                _assFonts.Add(new AssFontItem
                {
                    FontName = name,
                    IsFound = isFound,
                    MatchedFilePath = matchedPath,
                    IsLoaded = isLoaded
                });
            }

            AssStatusText.Text = $"共解析出 {_assFonts.Count} 个字体需求，本地索引库已匹配 {matchedCount} 个。";
        }

        private void MountAssFonts_Click(object sender, RoutedEventArgs e)
        {
            int successCount = 0;
            foreach (var item in _assFonts)
            {
                if (item.IsFound && !string.IsNullOrEmpty(item.MatchedFilePath))
                {
                    if (FontNativeService.LoadFont(item.MatchedFilePath))
                    {
                        item.IsLoaded = true;
                        successCount++;
                    }
                }
            }

            AssStatusText.Text = $"挂载完成：成功临时挂载 {successCount} 个字体。";
        }

        private void UnmountAssFonts_Click(object sender, RoutedEventArgs e)
        {
            int unloadedCount = 0;
            foreach (var item in _assFonts)
            {
                if (!string.IsNullOrEmpty(item.MatchedFilePath) && item.IsLoaded)
                {
                    if (FontNativeService.UnloadFont(item.MatchedFilePath))
                    {
                        item.IsLoaded = false;
                        unloadedCount++;
                    }
                }
            }

            AssStatusText.Text = $"卸载完成：已卸载 {unloadedCount} 个字体。";
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
                    }
                }
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
