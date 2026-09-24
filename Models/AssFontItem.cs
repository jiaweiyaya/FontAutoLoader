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
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _isLoaded;

    public string MatchStatusText => IsFound ? "库内已匹配" : "库内缺失";
    public string ActionText => IsLoaded ? "卸载" : "挂载";
}