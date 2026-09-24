using CommunityToolkit.Mvvm.ComponentModel;

namespace FontAutoLoader.Models;

public partial class AssFontItem : ObservableObject
{
    [ObservableProperty]
    private string _fontName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchStatusText))]
    private bool _isFound;

    [ObservableProperty]
    private string? _matchedFilePath;

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
    private bool _hasError;

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
            if (IsLoaded) return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)); // 蓝色
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
}