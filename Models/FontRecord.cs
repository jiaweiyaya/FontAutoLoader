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

    // 彻底损坏标红，存在解析瑕疵标黄，正常自适应系统卡片画刷
    public Brush BackgroundBrush
    {
        get
        {
            if (IsCorrupted)
                return new SolidColorBrush(Color.FromArgb(35, 230, 40, 40)); // 柔和红
            if (HasWarning)
                return new SolidColorBrush(Color.FromArgb(40, 245, 170, 0)); // 醒目暖黄
            
            if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var brushObj) && brushObj is Brush defaultBrush)
            {
                return defaultBrush;
            }
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    public Visibility CorruptedVisibility => IsCorrupted ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WarningVisibility => (!IsCorrupted && HasWarning) ? Visibility.Visible : Visibility.Collapsed;
}