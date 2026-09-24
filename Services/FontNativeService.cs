using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Microsoft.Win32;

namespace FontAutoLoader.Services;

public static class FontNativeService
{
    // 改为 0 表示对当前会话中所有程序（如播放器）可见，且无需注册表持久写入
    private const uint FR_GLOBAL_SESSION = 0x00;
    private const uint WM_FONTCHANGE = 0x001D;
    private static readonly IntPtr HWND_BROADCAST = new(0xffff);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool RemoveFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    // 记录当前已挂载的文件路径
    private static readonly ConcurrentDictionary<string, byte> MountedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 检查指定路径的字体是否已经被加载
    /// </summary>
    public static bool IsLoaded(string fontPath)
    {
        return MountedPaths.ContainsKey(fontPath);
    }

    /// <summary>
    /// 获取所有当前已挂载的字体路径列表
    /// </summary>
    public static IReadOnlyCollection<string> GetLoadedFonts()
    {
        return MountedPaths.Keys.ToArray();
    }

    /// <summary>
    /// 临时挂载字体
    /// </summary>
    public static bool LoadFont(string fontPath)
    {
        if (string.IsNullOrWhiteSpace(fontPath) || !File.Exists(fontPath))
        {
            return false;
        }

        if (MountedPaths.ContainsKey(fontPath))
        {
            return true;
        }

        int result = AddFontResourceEx(fontPath, FR_GLOBAL_SESSION, IntPtr.Zero);
        if (result > 0)
        {
            MountedPaths.TryAdd(fontPath, 0);
            NotifySystemFontChange();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 临时卸载字体
    /// </summary>
    public static bool UnloadFont(string fontPath)
    {
        if (string.IsNullOrWhiteSpace(fontPath))
        {
            return false;
        }

        if (!MountedPaths.ContainsKey(fontPath))
        {
            return true;
        }

        bool result = RemoveFontResourceEx(fontPath, FR_GLOBAL_SESSION, IntPtr.Zero);
        if (result)
        {
            MountedPaths.TryRemove(fontPath, out _);
            NotifySystemFontChange();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 卸载所有当前挂载的字体（软件关闭或一键清空时使用）
    /// </summary>
    public static void UnloadAll()
    {
        foreach (var path in MountedPaths.Keys)
        {
            RemoveFontResourceEx(path, FR_GLOBAL_SESSION, IntPtr.Zero);
        }
        MountedPaths.Clear();
        NotifySystemFontChange();
    }

    private static void NotifySystemFontChange()
    {
        // 广播通知正在运行的播放器等程序字体库已更新，超时设为500毫秒防止卡死
        SendMessageTimeout(HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, 0x0002, 500, out _);
    }

    /// <summary>
    /// 获取当前系统注册表中所有已安装字体的名称列表
    /// </summary>
    public static HashSet<string> GetSystemInstalledFontNames()
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] registryKeys =
        [
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Fonts"
        ];

        foreach (var keyPath in registryKeys)
        {
            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(keyPath);
                if (hklmKey != null)
                {
                    foreach (var val in hklmKey.GetValueNames())
                    {
                        string clean = val.Replace(" (TrueType)", "", StringComparison.OrdinalIgnoreCase)
                                          .Replace(" (OpenType)", "", StringComparison.OrdinalIgnoreCase)
                                          .Trim();
                        installed.Add(clean);
                    }
                }

                using var hkcuKey = Registry.CurrentUser.OpenSubKey(keyPath);
                if (hkcuKey != null)
                {
                    foreach (var val in hkcuKey.GetValueNames())
                    {
                        string clean = val.Replace(" (TrueType)", "", StringComparison.OrdinalIgnoreCase)
                                          .Replace(" (OpenType)", "", StringComparison.OrdinalIgnoreCase)
                                          .Trim();
                        installed.Add(clean);
                    }
                }
            }
            catch
            {
                // 忽略注册表访问异常
            }
        }

        return installed;
    }

    /// <summary>
    /// 获取系统中已安装字体的完整元数据（包含注册表键名、文件物理路径、是否属于当前用户等）
    /// </summary>
    public static List<InstalledFontInfo> GetSystemInstalledFontInfos()
    {
        var result = new List<InstalledFontInfo>();
        string winFontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        string userFontsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");

        string[] registryKeys =
        [
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Fonts"
        ];

        foreach (var keyPath in registryKeys)
        {
            // 1. 本机全局字体 (HKLM)
            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(keyPath);
                if (hklmKey != null)
                {
                    foreach (var val in hklmKey.GetValueNames())
                    {
                        string rawFileName = hklmKey.GetValue(val)?.ToString() ?? string.Empty;
                        string cleanName = val.Replace(" (TrueType)", "", StringComparison.OrdinalIgnoreCase)
                                              .Replace(" (OpenType)", "", StringComparison.OrdinalIgnoreCase)
                                              .Trim();
                        string fullPath = Path.IsPathRooted(rawFileName) ? rawFileName : Path.Combine(winFontsDir, rawFileName);

                        result.Add(new InstalledFontInfo(cleanName, val, rawFileName, fullPath, false));
                    }
                }
            }
            catch
            {
            }

            // 2. 当前用户安装字体 (HKCU)
            try
            {
                using var hkcuKey = Registry.CurrentUser.OpenSubKey(keyPath);
                if (hkcuKey != null)
                {
                    foreach (var val in hkcuKey.GetValueNames())
                    {
                        string rawFileName = hkcuKey.GetValue(val)?.ToString() ?? string.Empty;
                        string cleanName = val.Replace(" (TrueType)", "", StringComparison.OrdinalIgnoreCase)
                                              .Replace(" (OpenType)", "", StringComparison.OrdinalIgnoreCase)
                                              .Trim();
                        string fullPath = Path.IsPathRooted(rawFileName) ? rawFileName : Path.Combine(userFontsDir, rawFileName);

                        result.Add(new InstalledFontInfo(cleanName, val, rawFileName, fullPath, true));
                    }
                }
            }
            catch
            {
            }
        }

        return result;
    }

    /// <summary>
    /// 从系统中卸载并删除字体文件及注册表项
    /// </summary>
    public static bool DeleteSystemInstalledFont(InstalledFontInfo info, out string error)
    {
        error = string.Empty;
        string keyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
        try
        {
            // 1. 从注册表中注销
            if (info.IsCurrentUser)
            {
                using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
                key?.DeleteValue(info.ValueName, false);
            }
            else
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                key?.DeleteValue(info.ValueName, false);
            }

            // 2. 尝试删除物理文件
            if (!string.IsNullOrEmpty(info.FilePath) && File.Exists(info.FilePath))
            {
                File.Delete(info.FilePath);
            }

            NotifySystemFontChange();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

public record InstalledFontInfo(
    string FontName,
    string ValueName,
    string FontFileName,
    string FilePath,
    bool IsCurrentUser
);