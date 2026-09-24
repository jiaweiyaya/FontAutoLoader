using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FontAutoLoader.Models;

public partial class AssFileGroup : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    public ObservableCollection<AssFontItem> Fonts { get; } = new();
}