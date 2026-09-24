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
    public bool HasWarning { get; set; }

    // 彻底损坏标红，存在解析瑕疵标黄，正常则使用半透明底色
    public Brush BackgroundBrush
    {
        get
        {
            if (IsCorrupted)
                return new SolidColorBrush(Color.FromArgb(50, 240, 60, 60)); // 红色
            if (HasWarning)
                return new SolidColorBrush(Color.FromArgb(45, 220, 170, 0)); // 黄色
            return new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
        }
    }

    public Visibility CorruptedVisibility => IsCorrupted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WarningVisibility => (!IsCorrupted && HasWarning) ? Visibility.Visible : Visibility.Collapsed;
}