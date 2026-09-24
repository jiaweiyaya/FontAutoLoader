using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FontAutoLoader.Models;

public class FontRecord
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FontName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public bool IsCorrupted { get; set; }

    // 损坏字体时显示半透明警示红底色，正常时显示默认半透明底色
    public Brush BackgroundBrush => IsCorrupted
        ? new SolidColorBrush(Color.FromArgb(48, 235, 60, 60))
        : new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));

    public Visibility WarningVisibility => IsCorrupted
        ? Visibility.Visible
        : Visibility.Collapsed;
}