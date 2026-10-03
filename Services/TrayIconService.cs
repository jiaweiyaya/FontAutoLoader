using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace FontAutoLoader.Services
{
    public class TrayIconService : IDisposable
    {
        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 204;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;
        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const uint LR_DEFAULTSIZE = 0x00000040;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;

        private const int SW_RESTORE = 9;

        private readonly MainWindow _mainWindow;
        private readonly IntPtr _mainHwnd;
        private readonly SubclassProc _subclassProc;
        private IntPtr _hTrayIcon = IntPtr.Zero;
        private bool _isDisposed = false;
        private TrayMenuCoordinator? _menuCoordinator;

        public TrayIconService(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _mainHwnd = WindowNative.GetWindowHandle(_mainWindow);

            _subclassProc = new SubclassProc(WndProc);
            SetWindowSubclass(_mainHwnd, _subclassProc, new UIntPtr(1001), IntPtr.Zero);

            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                _hTrayIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            }

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _mainHwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_TIP | (_hTrayIcon != IntPtr.Zero ? NIF_ICON : 0),
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hTrayIcon,
                szTip = "FontAutoLoader"
            };

            Shell_NotifyIcon(NIM_ADD, ref nid);
        }

        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_TRAYICON)
            {
                int msg = lParam.ToInt32() & 0xFFFF;
                if (msg == WM_LBUTTONUP)
                {
                    _mainWindow.DispatcherQueue.TryEnqueue(OpenMainWindow);
                }
                else if (msg == WM_RBUTTONUP)
                {
                    _mainWindow.DispatcherQueue.TryEnqueue(ShowContextMenu);
                }
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        public void OpenMainWindow()
        {
            _mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                _mainWindow.AppWindow.Show();
                ShowWindow(_mainHwnd, SW_RESTORE);
                SetForegroundWindow(_mainHwnd);
                _mainWindow.Activate();
            });
        }

        private void ShowContextMenu()
        {
            _menuCoordinator ??= new TrayMenuCoordinator(_mainWindow);
            GetCursorPos(out POINT pt);
            _menuCoordinator.ShowAt(pt.X, pt.Y);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            RemoveWindowSubclass(_mainHwnd, _subclassProc, new UIntPtr(1001));

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _mainHwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);

            if (_hTrayIcon != IntPtr.Zero)
            {
                DestroyIcon(_hTrayIcon);
                _hTrayIcon = IntPtr.Zero;
            }

            _menuCoordinator?.Dispose();
        }

        /// <summary>
        /// 模仿 TranslucentTB 机制：全局鼠标外部点击检测、彻底干掉白框与任务栏图标
        /// </summary>
        private class TrayMenuCoordinator : IDisposable
        {
            private readonly MainWindow _mainWindow;
            private readonly MainMenuWindow _mainWin;
            private readonly SubMenuWindow _subWin;

            private IntPtr _mouseHook = IntPtr.Zero;
            private readonly LowLevelMouseProc _mouseProc;

            public TrayMenuCoordinator(MainWindow mainWindow)
            {
                _mainWindow = mainWindow;
                _mainWin = new MainMenuWindow(_mainWindow, this);
                _subWin = new SubMenuWindow(_mainWindow, this);
                _mouseProc = LowLevelMouseHandler;
            }

            public void ShowAt(int x, int y)
            {
                _subWin.HideImmediately();
                _mainWin.ShowAt(x, y);
                InstallHook();
            }

            public void ToggleSubMenu()
            {
                if (_subWin.IsVisible)
                {
                    _subWin.HideWithAnimation();
                }
                else
                {
                    _subWin.ShowBeside(_mainWin.CurrentX, _mainWin.CurrentY, _mainWin.WindowWidth, _mainWin.WindowHeight);
                }
            }

            public void HideAll()
            {
                _mainWin.HideImmediately();
                _subWin.HideImmediately();
                UninstallHook();
            }

            private void InstallHook()
            {
                if (_mouseHook == IntPtr.Zero)
                {
                    using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule;
                    IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
                    _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
                }
            }

            private void UninstallHook()
            {
                if (_mouseHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHook);
                    _mouseHook = IntPtr.Zero;
                }
            }

            private IntPtr LowLevelMouseHandler(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN)
                    {
                        var hookStruct = Marshal.PtrToStructure<POINT>(lParam);
                        int px = hookStruct.X;
                        int py = hookStruct.Y;

                        bool inMain = px >= _mainWin.CurrentX && px <= _mainWin.CurrentX + _mainWin.WindowWidth &&
                                      py >= _mainWin.CurrentY && py <= _mainWin.CurrentY + _mainWin.WindowHeight;

                        bool inSub = false;
                        if (_subWin.IsVisible)
                        {
                            inSub = px >= _subWin.CurrentX && px <= _subWin.CurrentX + _subWin.CurrentWidth &&
                                    py >= _subWin.CurrentY && py <= _subWin.CurrentY + _subWin.CurrentHeight;
                        }

                        // 点击到两个菜单以外的任何地方（包含主窗口、桌面、任务栏）立刻关闭
                        if (!inMain && !inSub)
                        {
                            _mainWindow.DispatcherQueue.TryEnqueue(HideAll);
                        }
                    }
                }
                return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            public void Dispose()
            {
                UninstallHook();
                _mainWin.Close();
                _subWin.Close();
            }
        }

        private static void ApplyTranslucentTBWindowStyle(IntPtr hwnd, AppWindow appWindow)
        {
            // 1. 彻底从 Windows 任务栏和 Alt+Tab 切换器中抹掉 (消除 WinUI Desktop 图标)
            appWindow.IsShownInSwitchers = false;

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            // 2. 剥除 WS_THICKFRAME 与 WS_CAPTION，转为纯粹的 WS_POPUP (彻底清除 Windows 11 DWM 亮白框)
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
            style |= WS_POPUP;
            SetWindowLong(hwnd, GWL_STYLE, style);

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        /// <summary>
        /// 独立主菜单窗口
        /// </summary>
        private class MainMenuWindow : Window
        {
            private readonly MainWindow _mainWindow;
            private readonly TrayMenuCoordinator _coordinator;
            public IntPtr Hwnd { get; }
            public int CurrentX { get; private set; }
            public int CurrentY { get; private set; }
            public int WindowWidth => 192;
            public int WindowHeight => 136;

            public MainMenuWindow(MainWindow mainWindow, TrayMenuCoordinator coordinator)
            {
                _mainWindow = mainWindow;
                _coordinator = coordinator;
                Hwnd = WindowNative.GetWindowHandle(this);

                ApplyTranslucentTBWindowStyle(Hwnd, this.AppWindow);

                var rootBorder = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(240, 30, 30, 30)),
                    Padding = new Thickness(4)
                };

                var stack = new StackPanel { Spacing = 2 };

                var itemManage = CreateItem("管理已挂载字体", "\uE8B9", true, (s, e) => _coordinator.ToggleSubMenu());
                var itemOpen = CreateItem("打开主窗口", "\uE737", false, (s, e) =>
                {
                    _coordinator.HideAll();
                    ShowWindow(WindowNative.GetWindowHandle(_mainWindow), SW_RESTORE);
                    SetForegroundWindow(WindowNative.GetWindowHandle(_mainWindow));
                    _mainWindow.Activate();
                });
                var separator = new Border
                {
                    Height = 1,
                    Margin = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255))
                };
                var itemExit = CreateItem("退出应用", "\uE7E8", false, (s, e) =>
                {
                    _coordinator.HideAll();
                    _mainWindow.DispatcherQueue.TryEnqueue(_mainWindow.RequestAppExit);
                });

                stack.Children.Add(itemManage);
                stack.Children.Add(itemOpen);
                stack.Children.Add(separator);
                stack.Children.Add(itemExit);

                rootBorder.Child = stack;
                this.Content = rootBorder;
            }

            private Grid CreateItem(string title, string iconGlyph, bool hasArrow, RoutedEventHandler onClick)
            {
                var row = new Grid
                {
                    Height = 34,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 0, 8, 0),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (hasArrow) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new FontIcon { Glyph = iconGlyph, FontSize = 13, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var text = new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(icon, 0);
                Grid.SetColumn(text, 1);
                row.Children.Add(icon);
                row.Children.Add(text);

                if (hasArrow)
                {
                    var arrow = new FontIcon { Glyph = "\uE76C", FontSize = 10, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(arrow, 2);
                    row.Children.Add(arrow);
                }

                row.PointerEntered += (s, e) => row.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255));
                row.PointerExited += (s, e) => row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                row.PointerPressed += (s, e) => onClick?.Invoke(row, new RoutedEventArgs());
                return row;
            }

            public void ShowAt(int x, int y)
            {
                var display = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(x, y), Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
                int posX = x - WindowWidth;
                int posY = y - WindowHeight;

                if (posX < display.WorkArea.X + 10) posX = x + 10;
                if (posY < display.WorkArea.Y + 10) posY = y + 10;
                if (posX + WindowWidth > display.WorkArea.X + display.WorkArea.Width) posX = display.WorkArea.X + display.WorkArea.Width - WindowWidth;

                CurrentX = posX;
                CurrentY = posY;

                this.AppWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidth, WindowHeight));
                this.AppWindow.Move(new Windows.Graphics.PointInt32(CurrentX, CurrentY));
                this.AppWindow.Show();
                SetForegroundWindow(Hwnd);
            }

            public void HideImmediately()
            {
                this.AppWindow.Hide();
            }
        }

        /// <summary>
        /// 独立子菜单窗口：高刷滑动动画、永不褪色的红色确认按钮
        /// </summary>
        private class SubMenuWindow : Window
        {
            private readonly MainWindow _mainWindow;
            private readonly TrayMenuCoordinator _coordinator;
            public IntPtr Hwnd { get; }
            public bool IsVisible { get; private set; }
            public int CurrentX { get; private set; }
            public int CurrentY { get; private set; }
            public int CurrentWidth => 330;
            public int CurrentHeight { get; private set; }

            private readonly Border _rootBorder;
            private readonly StackPanel _fontItemsContainer;
            private readonly TextBlock _emptyNoticeBlock;
            private readonly Grid _pagerGrid;
            private readonly Button _btnPrev;
            private readonly Button _btnNext;

            private List<string> _loadedFonts = new();
            private int _currentPage = 0;
            private const int PageSize = 10;
            private string? _pendingConfirmPath = null;

            // 动画状态
            private bool _isAnimating = false;
            private double _animTime = 0;
            private const double AnimDuration = 0.18;
            private bool _isEntering = true;
            private double _startX, _targetX;
            private readonly System.Diagnostics.Stopwatch _stopwatch = new();
            private TimeSpan _lastTime;

            public SubMenuWindow(MainWindow mainWindow, TrayMenuCoordinator coordinator)
            {
                _mainWindow = mainWindow;
                _coordinator = coordinator;
                Hwnd = WindowNative.GetWindowHandle(this);

                ApplyTranslucentTBWindowStyle(Hwnd, this.AppWindow);

                _rootBorder = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(240, 30, 30, 30)),
                    Padding = new Thickness(6)
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                _emptyNoticeBlock = new TextBlock
                {
                    Text = "当前无临时挂载字体",
                    FontSize = 12,
                    Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 14, 0, 14),
                    Visibility = Visibility.Collapsed
                };
                Grid.SetRow(_emptyNoticeBlock, 0);

                _fontItemsContainer = new StackPanel { Spacing = 2 };
                Grid.SetRow(_fontItemsContainer, 1);

                _pagerGrid = new Grid { Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 4 };
                _pagerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _pagerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                _btnPrev = new Button { Content = new FontIcon { Glyph = "\uE70E", FontSize = 11 }, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 4, 0, 4) };
                _btnPrev.Click += (s, e) => { if (_currentPage > 0) { _currentPage--; _pendingConfirmPath = null; RenderPage(); } };
                Grid.SetColumn(_btnPrev, 0);

                _btnNext = new Button { Content = new FontIcon { Glyph = "\uE70D", FontSize = 11 }, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0, 4, 0, 4) };
                _btnNext.Click += (s, e) =>
                {
                    int totalPages = Math.Max(1, (int)Math.Ceiling(_loadedFonts.Count / (double)PageSize));
                    if (_currentPage < totalPages - 1) { _currentPage++; _pendingConfirmPath = null; RenderPage(); }
                };
                Grid.SetColumn(_btnNext, 1);

                _pagerGrid.Children.Add(_btnPrev);
                _pagerGrid.Children.Add(_btnNext);
                Grid.SetRow(_pagerGrid, 2);

                grid.Children.Add(_emptyNoticeBlock);
                grid.Children.Add(_fontItemsContainer);
                grid.Children.Add(_pagerGrid);
                _rootBorder.Child = grid;

                this.Content = _rootBorder;
            }

            public void ShowBeside(int mainX, int mainY, int mainW, int mainH)
            {
                _loadedFonts = FontNativeService.GetLoadedFonts().ToList();
                _currentPage = 0;
                _pendingConfirmPath = null;
                RenderPage();

                CurrentHeight = CalculateHeight();

                var display = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(mainX, mainY), Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
                int screenRight = display.WorkArea.X + display.WorkArea.Width;

                bool openRight = (mainX + mainW + 6 + CurrentWidth) <= screenRight;
                _targetX = openRight ? (mainX + mainW + 6) : (mainX - CurrentWidth - 6);
                int targetY = mainY;

                if (targetY + CurrentHeight > display.WorkArea.Y + display.WorkArea.Height)
                {
                    targetY = display.WorkArea.Y + display.WorkArea.Height - CurrentHeight - 8;
                }

                CurrentX = (int)_targetX;
                CurrentY = targetY;
                _startX = openRight ? (_targetX - 16) : (_targetX + 16);

                this.AppWindow.Resize(new Windows.Graphics.SizeInt32(CurrentWidth, CurrentHeight));
                this.AppWindow.Move(new Windows.Graphics.PointInt32((int)_startX, CurrentY));

                _rootBorder.Opacity = 0;
                _rootBorder.Translation = new System.Numerics.Vector3(openRight ? -14 : 14, 0, 0);

                this.AppWindow.Show();
                IsVisible = true;

                StartSlideAnimation(true);
            }

            public void HideWithAnimation()
            {
                if (!IsVisible) return;
                StartSlideAnimation(false);
            }

            public void HideImmediately()
            {
                IsVisible = false;
                _isAnimating = false;
                CompositionTarget.Rendering -= OnVsyncRendering;
                this.AppWindow.Hide();
            }

            private int CalculateHeight()
            {
                if (_loadedFonts.Count == 0) return 60;
                int itemCount = Math.Min(PageSize, _loadedFonts.Count - _currentPage * PageSize);
                int h = 12 + (itemCount * 34);
                if (_loadedFonts.Count > PageSize) h += 38;
                return h;
            }

            private void RenderPage()
            {
                _fontItemsContainer.Children.Clear();

                if (_loadedFonts.Count == 0)
                {
                    _emptyNoticeBlock.Visibility = Visibility.Visible;
                    _pagerGrid.Visibility = Visibility.Collapsed;
                    return;
                }

                _emptyNoticeBlock.Visibility = Visibility.Collapsed;
                int totalPages = Math.Max(1, (int)Math.Ceiling(_loadedFonts.Count / (double)PageSize));
                if (_currentPage >= totalPages) _currentPage = totalPages - 1;
                if (_currentPage < 0) _currentPage = 0;

                _pagerGrid.Visibility = totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
                _btnPrev.IsEnabled = _currentPage > 0;
                _btnNext.IsEnabled = _currentPage < totalPages - 1;

                var pageList = _loadedFonts.Skip(_currentPage * PageSize).Take(PageSize);

                foreach (var fontPath in pageList)
                {
                    string fileName = Path.GetFileName(fontPath);
                    var row = new Grid
                    {
                        Height = 32,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 0, 4, 0),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(14, 255, 255, 255))
                    };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var tbName = new TextBlock
                    {
                        Text = fileName,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    ToolTipService.SetToolTip(tbName, fontPath);
                    Grid.SetColumn(tbName, 0);

                    var btnUnload = new Button { FontSize = 11, Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
                    bool isPending = string.Equals(_pendingConfirmPath, fontPath, StringComparison.OrdinalIgnoreCase);

                    if (isPending)
                    {
                        btnUnload.Content = "确定";
                        var redBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 211, 47, 47));
                        var hoverRedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 57, 53));
                        var pressedRedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 183, 28, 28));
                        var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);

                        // 覆盖 WinUI 默认 Hover/Pressed 样式，保证鼠标移上去依然是鲜亮大红色
                        btnUnload.Background = redBrush;
                        btnUnload.Foreground = whiteBrush;
                        btnUnload.Resources["ButtonBackgroundPointerOver"] = hoverRedBrush;
                        btnUnload.Resources["ButtonBackgroundPressed"] = pressedRedBrush;
                        btnUnload.Resources["ButtonForegroundPointerOver"] = whiteBrush;
                        btnUnload.Resources["ButtonForegroundPressed"] = whiteBrush;
                    }
                    else
                    {
                        btnUnload.Content = "卸载";
                        btnUnload.ClearValue(Button.BackgroundProperty);
                        btnUnload.ClearValue(Button.ForegroundProperty);
                        btnUnload.Resources.Remove("ButtonBackgroundPointerOver");
                        btnUnload.Resources.Remove("ButtonBackgroundPressed");
                        btnUnload.Resources.Remove("ButtonForegroundPointerOver");
                        btnUnload.Resources.Remove("ButtonForegroundPressed");
                    }

                    string capturedPath = fontPath;
                    btnUnload.Click += (s, e) =>
                    {
                        if (string.Equals(_pendingConfirmPath, capturedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _pendingConfirmPath = null;
                            _mainWindow.UnloadFontFromTray(capturedPath);
                            _loadedFonts = FontNativeService.GetLoadedFonts().ToList();
                            RenderPage();
                            CurrentHeight = CalculateHeight();
                            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(CurrentWidth, CurrentHeight));
                        }
                        else
                        {
                            _pendingConfirmPath = capturedPath;
                            RenderPage();
                        }
                    };
                    Grid.SetColumn(btnUnload, 1);

                    row.Children.Add(tbName);
                    row.Children.Add(btnUnload);
                    _fontItemsContainer.Children.Add(row);
                }
            }

            private void StartSlideAnimation(bool isEntering)
            {
                _isEntering = isEntering;
                _animTime = 0;
                _stopwatch.Restart();
                _lastTime = _stopwatch.Elapsed;

                if (!_isAnimating)
                {
                    _isAnimating = true;
                    CompositionTarget.Rendering += OnVsyncRendering;
                }
            }

            private void OnVsyncRendering(object? sender, object e)
            {
                var now = _stopwatch.Elapsed;
                double dt = (now - _lastTime).TotalSeconds;
                _lastTime = now;

                _animTime += dt;
                double progress = Math.Clamp(_animTime / AnimDuration, 0.0, 1.0);
                double ease = 1.0 - Math.Pow(1.0 - progress, 3);

                if (_isEntering)
                {
                    _rootBorder.Opacity = ease;
                    double offsetX = (1.0 - ease) * (_startX < _targetX ? -14.0 : 14.0);
                    _rootBorder.Translation = new System.Numerics.Vector3((float)offsetX, 0, 0);

                    double curWinX = _startX + (_targetX - _startX) * ease;
                    CurrentX = (int)curWinX;
                    this.AppWindow.Move(new Windows.Graphics.PointInt32(CurrentX, CurrentY));

                    if (progress >= 1.0)
                    {
                        _rootBorder.Opacity = 1.0;
                        _rootBorder.Translation = System.Numerics.Vector3.Zero;
                        CurrentX = (int)_targetX;
                        this.AppWindow.Move(new Windows.Graphics.PointInt32(CurrentX, CurrentY));
                        _isAnimating = false;
                        CompositionTarget.Rendering -= OnVsyncRendering;
                    }
                }
                else
                {
                    _rootBorder.Opacity = 1.0 - ease;
                    double offsetX = ease * (_startX < _targetX ? -14.0 : 14.0);
                    _rootBorder.Translation = new System.Numerics.Vector3((float)offsetX, 0, 0);

                    if (progress >= 1.0)
                    {
                        _isAnimating = false;
                        CompositionTarget.Rendering -= OnVsyncRendering;
                        HideImmediately();
                    }
                }
            }
        }
    }
}