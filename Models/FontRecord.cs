namespace FontAutoLoader.Models;

public class FontRecord
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FontName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
}