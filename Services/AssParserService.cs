using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FontAutoLoader.Services;

public static class AssParserService
{
    private static readonly Regex InlineFontRegex = new(@"\\fn([^\\}]+)", RegexOptions.Compiled);

    /// <summary>
    /// 解析 ASS 字幕文件中引用的所有字体名称（包括样式表与行内 \fn 标签）
    /// </summary>
    public static HashSet<string> ParseFontsFromAssFile(string filePath)
    {
        var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath))
        {
            return fonts;
        }

        var lines = File.ReadAllLines(filePath);
        bool inStylesSection = false;
        int styleFontIndex = -1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                inStylesSection = line.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) ||
                                  line.Equals("[V4 Styles]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inStylesSection)
            {
                if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Substring("Format:".Length).Split(',');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].Trim().Equals("Fontname", StringComparison.OrdinalIgnoreCase))
                        {
                            styleFontIndex = i;
                            break;
                        }
                    }
                }
                else if (line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase) && styleFontIndex >= 0)
                {
                    var values = line.Substring("Style:".Length).Split(',');
                    if (values.Length > styleFontIndex)
                    {
                        string fontName = values[styleFontIndex].Trim().TrimStart('@');
                        if (!string.IsNullOrWhiteSpace(fontName))
                        {
                            fonts.Add(fontName);
                        }
                    }
                }
            }
            else
            {
                if (line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                {
                    var matches = InlineFontRegex.Matches(line);
                    foreach (Match m in matches)
                    {
                        if (m.Success && m.Groups.Count > 1)
                        {
                            string fontName = m.Groups[1].Value.Trim().TrimStart('@');
                            if (!string.IsNullOrWhiteSpace(fontName))
                            {
                                fonts.Add(fontName);
                            }
                        }
                    }
                }
            }
        }

        return fonts;
    }
}