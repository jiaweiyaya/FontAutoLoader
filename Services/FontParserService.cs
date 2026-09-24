using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FontAutoLoader.Services;

public static class FontParserService
{
    /// <summary>
    /// 解析字体文件支持的所有名称（包括中英文 Family Name、Full Name）
    /// 支持 .ttf, .otf, .ttc 等常见矢量格式
    /// </summary>
    public static List<(string FamilyName, string FullName)> ExtractFontNames(string filePath)
    {
        var result = new List<(string FamilyName, string FullName)>();
        if (!File.Exists(filePath))
        {
            return result;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 12)
            {
                return result;
            }

            uint magic = ReadUInt32Be(reader);

            // 判断是否是 TTC (TrueType Collection)
            if (magic == 0x74746366) // 'ttcf'
            {
                reader.BaseStream.Seek(8, SeekOrigin.Begin);
                uint numFonts = ReadUInt32Be(reader);
                var offsets = new List<uint>();
                for (int i = 0; i < numFonts; i++)
                {
                    offsets.Add(ReadUInt32Be(reader));
                }

                foreach (var offset in offsets)
                {
                    var names = ParseSingleFont(reader, offset);
                    if (names.FamilyName.Length > 0 || names.FullName.Length > 0)
                    {
                        result.Add(names);
                    }
                }
            }
            else
            {
                // 普通 TTF / OTF 字体
                var names = ParseSingleFont(reader, 0);
                if (names.FamilyName.Length > 0 || names.FullName.Length > 0)
                {
                    result.Add(names);
                }
            }
        }
        catch
        {
            // 对于非标或损坏的字体文件跳过处理
        }

        return result;
    }

    private static (string FamilyName, string FullName) ParseSingleFont(BinaryReader reader, uint fontOffset)
    {
        string family = string.Empty;
        string full = string.Empty;

        reader.BaseStream.Seek(fontOffset + 4, SeekOrigin.Begin);
        ushort numTables = ReadUInt16Be(reader);
        reader.BaseStream.Seek(fontOffset + 12, SeekOrigin.Begin);

        uint nameTableOffset = 0;
        for (int i = 0; i < numTables; i++)
        {
            uint tag = ReadUInt32Be(reader);
            reader.ReadUInt32(); // 跳过 checksum
            uint offset = ReadUInt32Be(reader);
            reader.ReadUInt32(); // 跳过 length

            if (tag == 0x6E616D65) // 'name' 表
            {
                nameTableOffset = offset;
                break;
            }
        }

        if (nameTableOffset == 0)
        {
            return (family, full);
        }

        reader.BaseStream.Seek(nameTableOffset, SeekOrigin.Begin);
        reader.ReadUInt16(); // format
        ushort count = ReadUInt16Be(reader);
        ushort stringOffset = ReadUInt16Be(reader);
        long storageOffset = nameTableOffset + stringOffset;

        var familyNames = new List<string>();
        var fullNames = new List<string>();

        for (int i = 0; i < count; i++)
        {
            ushort platformId = ReadUInt16Be(reader);
            ushort encodingId = ReadUInt16Be(reader);
            ushort languageId = ReadUInt16Be(reader);
            ushort nameId = ReadUInt16Be(reader);
            ushort length = ReadUInt16Be(reader);
            ushort recordOffset = ReadUInt16Be(reader);

            if (nameId == 1 || nameId == 4) // 1: Family Name, 4: Full Name
            {
                long currentPos = reader.BaseStream.Position;
                reader.BaseStream.Seek(storageOffset + recordOffset, SeekOrigin.Begin);
                byte[] bytes = reader.ReadBytes(length);
                reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);

                string decoded = DecodeName(bytes, platformId, encodingId);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    decoded = decoded.Trim();
                    if (nameId == 1 && !familyNames.Contains(decoded))
                    {
                        familyNames.Add(decoded);
                    }
                    else if (nameId == 4 && !fullNames.Contains(decoded))
                    {
                        fullNames.Add(decoded);
                    }
                }
            }
        }

        family = familyNames.Count > 0 ? string.Join(" / ", familyNames) : string.Empty;
        full = fullNames.Count > 0 ? string.Join(" / ", fullNames) : string.Empty;
        return (family, full);
    }

    static FontParserService()
    {
        // 注册代码页编码器以支持 GBK/Big5 等中文老字体的解码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string DecodeName(byte[] bytes, ushort platformId, ushort encodingId)
    {
        try
        {
            string text = string.Empty;

            if (platformId == 3) // Windows 平台
            {
                if (encodingId == 1 || encodingId == 10)
                {
                    text = Encoding.BigEndianUnicode.GetString(bytes);
                }
                else if (encodingId == 3) // PRC / GB2312 / GBK
                {
                    text = Encoding.GetEncoding(936).GetString(bytes);
                }
                else if (encodingId == 4) // Big5
                {
                    text = Encoding.GetEncoding(950).GetString(bytes);
                }
                else if (encodingId == 2) // Shift-JIS
                {
                    text = Encoding.GetEncoding(932).GetString(bytes);
                }
                else
                {
                    text = Encoding.BigEndianUnicode.GetString(bytes);
                }
            }
            else if (platformId == 0) // Unicode
            {
                text = Encoding.BigEndianUnicode.GetString(bytes);
            }
            else if (platformId == 1) // Mac 平台
            {
                if (encodingId == 0)
                {
                    text = Encoding.ASCII.GetString(bytes);
                }
                else if (encodingId == 25) // Mac 简体中文
                {
                    text = Encoding.GetEncoding(936).GetString(bytes);
                }
                else if (encodingId == 2) // Mac 繁体中文
                {
                    text = Encoding.GetEncoding(950).GetString(bytes);
                }
                else
                {
                    text = Encoding.ASCII.GetString(bytes);
                }
            }
            else
            {
                text = Encoding.UTF8.GetString(bytes);
            }

            text = text.Replace("\0", "").Trim();

            // 过滤掉包含不可解码字符()或控制字符的乱码串
            if (text.Contains('\uFFFD') || text.Any(c => char.IsControl(c) && c != '\t'))
            {
                return string.Empty;
            }

            return text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ushort ReadUInt16Be(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(2);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static uint ReadUInt32Be(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}