using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using FontAutoLoader.Models;

namespace FontAutoLoader.Services;

public class DatabaseService
{
    private static readonly string DbFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FontAutoLoader");

    private static readonly string DbPath = Path.Combine(DbFolder, "fonts.db");
    private static readonly string ConnectionString = $"Data Source={DbPath}";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".ttc", ".woff", ".woff2"
    };

    public DatabaseService()
    {
        InitDatabase();
    }

    private void InitDatabase()
    {
        if (!Directory.Exists(DbFolder))
        {
            Directory.CreateDirectory(DbFolder);
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Folders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Path TEXT UNIQUE NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Fonts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath TEXT NOT NULL,
                FontName TEXT NOT NULL,
                FamilyName TEXT NOT NULL,
                Format TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_fonts_name ON Fonts(FontName);
            CREATE INDEX IF NOT EXISTS idx_fonts_family ON Fonts(FamilyName);
        ";
        command.ExecuteNonQuery();
    }

    public List<string> GetFolders()
    {
        var list = new List<string>();
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Path FROM Folders ORDER BY Id ASC;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public bool AddFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO Folders (Path) VALUES ($path);";
        command.Parameters.AddWithValue("$path", folderPath);
        return command.ExecuteNonQuery() > 0;
    }

    public bool RemoveFolder(string folderPath)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Folders WHERE Path = $path;";
        command.Parameters.AddWithValue("$path", folderPath);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 手动重新扫描所有已配置的文件夹，并全量重建字体索引库
    /// </summary>
    public async Task RebuildIndexAsync(IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            var folders = GetFolders();
            var discoveredFonts = new List<FontRecord>();

            progress?.Report("正在枚举文件夹内的字体文件...");
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                try
                {
                    var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file);
                        if (SupportedExtensions.Contains(ext))
                        {
                            var namesList = FontParserService.ExtractFontNames(file);
                            if (namesList.Count > 0)
                            {
                                foreach (var (family, full) in namesList)
                                {
                                    discoveredFonts.Add(new FontRecord
                                    {
                                        FilePath = file,
                                        FamilyName = family,
                                        FontName = string.IsNullOrWhiteSpace(full) ? family : full,
                                        Format = ext.TrimStart('.').ToUpperInvariant()
                                    });
                                }
                            }
                            else
                            {
                                // 兜底：如果解析不到内部名称，回退使用无扩展名的文件名
                                string fallbackName = Path.GetFileNameWithoutExtension(file);
                                discoveredFonts.Add(new FontRecord
                                {
                                    FilePath = file,
                                    FamilyName = fallbackName,
                                    FontName = fallbackName,
                                    Format = ext.TrimStart('.').ToUpperInvariant()
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略无权限访问的子目录
                }
            }

            progress?.Report($"正在将 {discoveredFonts.Count} 条字体信息写入索引数据库...");

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using var clearCommand = connection.CreateCommand();
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = "DELETE FROM Fonts;";
            clearCommand.ExecuteNonQuery();

            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = @"
                INSERT INTO Fonts (FilePath, FontName, FamilyName, Format)
                VALUES ($filePath, $fontName, $familyName, $format);
            ";

            var paramPath = insertCommand.Parameters.Add("$filePath", SqliteType.Text);
            var paramName = insertCommand.Parameters.Add("$fontName", SqliteType.Text);
            var paramFamily = insertCommand.Parameters.Add("$familyName", SqliteType.Text);
            var paramFormat = insertCommand.Parameters.Add("$format", SqliteType.Text);

            foreach (var font in discoveredFonts)
            {
                paramPath.Value = font.FilePath;
                paramName.Value = font.FontName;
                paramFamily.Value = font.FamilyName;
                paramFormat.Value = font.Format;
                insertCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            progress?.Report($"索引构建完成，共载入 {discoveredFonts.Count} 条记录。");
        });
    }

    /// <summary>
    /// 在数据库中搜索字体（按字体名、家族名或文件名搜索）
    /// </summary>
    public List<FontRecord> SearchFonts(string keyword)
    {
        var list = new List<FontRecord>();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return list;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, FilePath, FontName, FamilyName, Format 
            FROM Fonts 
            WHERE FontName LIKE $kw OR FamilyName LIKE $kw OR FilePath LIKE $kw
            LIMIT 200;
        ";
        command.Parameters.AddWithValue("$kw", $"%{keyword}%");

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new FontRecord
            {
                Id = reader.GetInt32(0),
                FilePath = reader.GetString(1),
                FontName = reader.GetString(2),
                FamilyName = reader.GetString(3),
                Format = reader.GetString(4)
            });
        }

        return list;
    }

    /// <summary>
    /// 根据字幕中的字体名称寻找匹配的字体文件物理路径
    /// </summary>
    public string? MatchFontFilePath(string assFontName)
    {
        if (string.IsNullOrWhiteSpace(assFontName))
        {
            return null;
        }

        string cleanName = assFontName.Trim().TrimStart('@'); // 排除纵向排版前缀@

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        // 优先精确匹配，再模糊匹配
        command.CommandText = @"
            SELECT FilePath FROM Fonts 
            WHERE FontName = $exact OR FamilyName = $exact
            LIMIT 1;
        ";
        command.Parameters.AddWithValue("$exact", cleanName);

        var result = command.ExecuteScalar();
        if (result != null && result != DBNull.Value)
        {
            return (string)result;
        }

        // 尝试包含匹配
        using var fuzzyCommand = connection.CreateCommand();
        fuzzyCommand.CommandText = @"
            SELECT FilePath FROM Fonts 
            WHERE FontName LIKE $fuzzy OR FamilyName LIKE $fuzzy
            LIMIT 1;
        ";
        fuzzyCommand.Parameters.AddWithValue("$fuzzy", $"%{cleanName}%");

        var fuzzyResult = fuzzyCommand.ExecuteScalar();
        if (fuzzyResult != null && fuzzyResult != DBNull.Value)
        {
            return (string)fuzzyResult;
        }

        return null;
    }
}