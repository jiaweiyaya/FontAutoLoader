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
    private const int CurrentDbVersion = 2; // 当前数据库结构版本号

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
                Format TEXT NOT NULL,
                IsCorrupted INTEGER DEFAULT 0,
                HasWarning INTEGER DEFAULT 0
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

    private int GetDatabaseVersion()
    {
        try
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var result = command.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void SetDatabaseVersion(int version)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private void RecreateDatabase(List<string> backupFolders)
    {
        // 清理所有连接池，防止删除文件时遭遇文件占用异常
        SqliteConnection.ClearAllPools();

        if (File.Exists(DbPath))
        {
            File.Delete(DbPath);
        }

        InitDatabase();
        SetDatabaseVersion(CurrentDbVersion);

        // 重新灌入原先已添加的字体目录
        foreach (var folder in backupFolders)
        {
            AddFolder(folder);
        }
    }

    /// <summary>
    /// 手动重新扫描所有已配置的文件夹，并全量重建字体索引库
    /// </summary>
    public async Task RebuildIndexAsync(IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            var folders = GetFolders();

            // 检查数据库版本是否与当前代码匹配，若不同则直接删除旧库并重建全新数据库
            if (GetDatabaseVersion() != CurrentDbVersion)
            {
                progress?.Report("检测到数据库版本变更，正在自动重建全新结构数据库...");
                RecreateDatabase(folders);
                folders = GetFolders();
            }

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
                                    string resolvedName = string.IsNullOrWhiteSpace(full) ? family : full;
                                    // 名称中存在问号或未知字符但未彻底损坏的标记为警告标黄
                                    bool hasWarning = resolvedName.Contains('?') || family.Contains('?');

                                    discoveredFonts.Add(new FontRecord
                                    {
                                        FilePath = file,
                                        FamilyName = family,
                                        FontName = resolvedName,
                                        Format = ext.TrimStart('.').ToUpperInvariant(),
                                        HasWarning = hasWarning,
                                        IsCorrupted = false
                                    });
                                }
                            }
                            else
                            {
                                // 彻底无法解析元数据，退回文件名兜底并标红
                                string fallbackName = Path.GetFileNameWithoutExtension(file);
                                discoveredFonts.Add(new FontRecord
                                {
                                    FilePath = file,
                                    FamilyName = "无法读取内部字体名称",
                                    FontName = fallbackName,
                                    Format = ext.TrimStart('.').ToUpperInvariant(),
                                    IsCorrupted = true,
                                    HasWarning = false
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
                INSERT INTO Fonts (FilePath, FontName, FamilyName, Format, IsCorrupted, HasWarning)
                VALUES ($filePath, $fontName, $familyName, $format, $isCorrupted, $hasWarning);
            ";

            var paramPath = insertCommand.Parameters.Add("$filePath", SqliteType.Text);
            var paramName = insertCommand.Parameters.Add("$fontName", SqliteType.Text);
            var paramFamily = insertCommand.Parameters.Add("$familyName", SqliteType.Text);
            var paramFormat = insertCommand.Parameters.Add("$format", SqliteType.Text);
            var paramCorrupted = insertCommand.Parameters.Add("$isCorrupted", SqliteType.Integer);
            var paramWarning = insertCommand.Parameters.Add("$hasWarning", SqliteType.Integer);

            foreach (var font in discoveredFonts)
            {
                paramPath.Value = font.FilePath;
                paramName.Value = font.FontName;
                paramFamily.Value = font.FamilyName;
                paramFormat.Value = font.Format;
                paramCorrupted.Value = font.IsCorrupted ? 1 : 0;
                paramWarning.Value = font.HasWarning ? 1 : 0;
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
            SELECT Id, FilePath, FontName, FamilyName, Format, IsCorrupted, HasWarning
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
                Format = reader.GetString(4),
                IsCorrupted = reader.GetInt32(5) == 1,
                HasWarning = reader.GetInt32(6) == 1
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