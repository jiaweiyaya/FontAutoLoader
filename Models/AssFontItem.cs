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

    public string MatchStatusText
    {
        get
        {
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
            return IsLoaded ? "卸载" : "挂载";
        }
    }
}