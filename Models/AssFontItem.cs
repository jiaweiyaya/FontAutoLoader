using CommunityToolkit.Mvvm.ComponentModel;

namespace FontAutoLoader.Models;

public partial class AssFontItem : ObservableObject
{
    [ObservableProperty]
    private string _fontName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchStatusText))]
    [NotifyPropertyChangedFor(nameof(CardBackgroundBrush))]
    private bool _isFound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathVisibility))]
    private string? _matchedFilePath;

    public Microsoft.UI.Xaml.Visibility PathVisibility =>
        string.IsNullOrEmpty(MatchedFilePath) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    // 系统已安装的字体不显示右侧操作按钮，只保留纯粹的状态标签
    public Microsoft.UI.Xaml.Visibility ActionButtonVisibility =>
        IsSystemInstalled ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchStatusText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchStatusText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _isSystemInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchStatusText))]
    [NotifyPropertyChangedFor(nameof(BadgeBrush))]
    [NotifyPropertyChangedFor(nameof(BadgeTextBrush))]
    [NotifyPropertyChangedFor(nameof(CardBackgroundBrush))]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public string MatchStatusText
    {
        get
        {
            if (HasError) return "挂载异常";
            if (IsSystemInstalled) return "系统已安装";
            if (IsLoaded) return "已被挂载";
            return IsFound ? "库内已就绪" : "库内缺失";
        }
    }

    public string ActionText
    {
        get
        {
            if (IsSystemInstalled) return "已安装";
            if (HasError) return "重试";
            return IsLoaded ? "卸载" : "挂载";
        }
    }

    // 状态标签底色：绿(已安装)、蓝(已挂载)、黄(缺失)、红(错误)
    public Microsoft.UI.Xaml.Media.Brush BadgeBrush
    {
        get
        {
            if (HasError) return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 209, 52, 56)); // 红色
            if (IsSystemInstalled) return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 124, 65)); // 绿色
            if (IsLoaded)
            {
                // 自动提取全局主题紫画刷，保证与界面主题严格统一
                if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush accentBrush)
                {
                    return accentBrush;
                }
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252));
            }
            if (!IsFound) return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 216, 160, 0)); // 黄色
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(80, 128, 128, 128));
        }
    }

    public Microsoft.UI.Xaml.Media.Brush BadgeTextBrush
    {
        get
        {
            // 黄色底色搭配黑字，其余搭配白字保证高对比度
            if (!IsFound && !HasError && !IsSystemInstalled && !IsLoaded)
            {
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30));
            }
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }
    }

    // 整行卡片底色：异常变红，缺失变黄，正常自适应当前系统浅深主题卡片底色
    public Microsoft.UI.Xaml.Media.Brush CardBackgroundBrush
    {
        get
        {
            if (HasError)
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(35, 230, 40, 40)); // 挂载异常红
            if (!IsFound)
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, 245, 170, 0)); // 库内缺失黄

            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush defaultBrush)
            {
                return defaultBrush;
            }
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
}