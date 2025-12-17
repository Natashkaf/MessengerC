using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MessengerApp
{
    public class SystemNotificationManager : IDisposable
    {
        //константы для взаимодействия с windows API
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;
        private const int NIF_SHOWTIP = 0x00000080;
        private const int WM_USER = 0x0400;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_RBUTTONDBLCLK = 0x0206;
        private const int NIN_BALLOONSHOW = WM_USER + 2;
        private const int NIN_BALLOONHIDE = WM_USER + 3;
        private const int NIN_BALLOONTIMEOUT = WM_USER + 4;
        private const int NIN_BALLOONUSERCLICK = WM_USER + 5;
        private const int NIIF_INFO = 0x00000001;
        private const int NIIF_WARNING = 0x00000002;
        private const int NIIF_ERROR = 0x00000003;
        private const int NIIF_NOSOUND = 0x00000010;
        
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
        
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
        
        private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);
        
        private static extern bool GetCursorPos(out POINT lpPoint);
        
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private NOTIFYICONDATA _iconData;
        private IntPtr _windowHandle;
        private bool _isDisposed;
        private NotificationSettings _settings;
        private string _currentActiveChatId;
        private bool _isWindowActive;
        private DateTime _lastActivityTime;
        private bool _iconCreated = false;

        public event EventHandler NotificationClicked;
        public event EventHandler ShowWindowRequested;
        public event EventHandler ExitRequested;

        private ContextMenu _trayContextMenu;
        private Window _mainWindow;

        public SystemNotificationManager(IntPtr windowHandle, NotificationSettings settings, Window mainWindow = null)
        {
            _windowHandle = windowHandle;
            _settings = settings ?? new NotificationSettings();
            _mainWindow = mainWindow;
            InitializeIcon();
            InitializeContextMenu();
            UpdateActivityTime();
        }

        private void InitializeIcon()
        {
            try
            {
                _iconData = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                    hWnd = _windowHandle,
                    uID = 1,
                    uFlags = NIF_MESSAGE | NIF_TIP | NIF_SHOWTIP, // Убрали NIF_ICON
                    szTip = "Messenger - для уведомлений",
                    dwState = 0,
                    dwStateMask = 0,
                    dwInfoFlags = NIIF_INFO,
                    uTimeoutOrVersion = 3
                };

               
                _iconData.hIcon = IntPtr.Zero;

                if (Shell_NotifyIcon(NIM_ADD, ref _iconData))
                {
                    _iconCreated = true;
                  
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void InitializeContextMenu()
        {
            _trayContextMenu = new ContextMenu();

            var openItem = new MenuItem { Header = "Открыть Messenger" };
            openItem.Click += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);

            var separator1 = new Separator();

            var settingsItem = new MenuItem { Header = "Настройки уведомлений..." };
            settingsItem.Click += (s, e) => ShowNotificationSettings();

            var separator2 = new Separator();

            var exitItem = new MenuItem { Header = "Выйти" };
            exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

            _trayContextMenu.Items.Add(openItem);
            _trayContextMenu.Items.Add(separator1);
            _trayContextMenu.Items.Add(settingsItem);
            _trayContextMenu.Items.Add(separator2);
            _trayContextMenu.Items.Add(exitItem);
        }

        private void ShowNotificationSettings()
        {
            if (_mainWindow != null && _mainWindow.Dispatcher != null)
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var settingsWindow = new NotificationSettingsWindow(_settings, this, _mainWindow as MessengerWindow);
                        settingsWindow.Owner = _mainWindow;
                        settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        
                        if (settingsWindow.ShowDialog() == true)
                        {
                            UpdateSettings(settingsWindow.Settings);
                        }
                    }
                    catch (Exception ex)
                    {
                        
                    }
                });
            }
        }

        public void ShowNotification(string title, string message, string chatId = null)
        {
            try
            {
                if (!_settings.IsEnabled || !_iconCreated)
                    return;

                // Проверка умных уведомлений
                if (_settings.SmartNotifications)
                {

                    if (_isWindowActive && chatId == _currentActiveChatId)
                    {
                        return;
                    }
                    
                    if ((DateTime.Now - _lastActivityTime).TotalSeconds < 5)
                    {
                        return;
                    }
                }

                // Проверка заглушенных чатов
                if (chatId != null && _settings.MutedChats.ContainsKey(chatId) && _settings.MutedChats[chatId])
                {
                    return;
                }
                

                // Показывать баннер если включено
                if (_settings.ShowBanner && _iconCreated)
                {
                    _iconData.uFlags = NIF_INFO | NIF_SHOWTIP;
                    
                    // Ограничиваем длину заголовка и сообщения
                    _iconData.szInfoTitle = title.Length > 63 ? title.Substring(0, 60) + "..." : title;
                    
                    if (!_settings.ShowPreview)
                    {
                        _iconData.szInfo = "У вас новое сообщение";
                    }
                    else
                    {
                        _iconData.szInfo = message.Length > 255 ? message.Substring(0, 252) + "..." : message;
                    }
                    
                    _iconData.dwInfoFlags = NIIF_INFO;
                    if (!_settings.PlaySound)
                    {
                        _iconData.dwInfoFlags |= NIIF_NOSOUND;
                    }
                    
                    _iconData.uTimeoutOrVersion = 10000; 

                    if (!Shell_NotifyIcon(NIM_MODIFY, ref _iconData))
                    {
                    }
                }
                else if (_settings.PlaySound)
                {
                    // Только звук если баннеры отключены
                    System.Media.SystemSounds.Beep.Play();
                }

                // Мигать окном в панели задач если окно свернуто
                if (!_isWindowActive && _settings.PlaySound)
                {
                    FlashWindow(_windowHandle, true);
                }
            }
            catch (Exception ex)
            {
            }
        }

        public void UpdateActivityTime()
        {
            _lastActivityTime = DateTime.Now;
        }

        public void SetActiveChat(string chatId)
        {
            _currentActiveChatId = chatId;
            UpdateActivityTime();
        }

        public void SetWindowActive(bool isActive)
        {
            _isWindowActive = isActive;
            if (isActive)
            {
                UpdateActivityTime();
            }
        }

        public void MuteChat(string chatId, bool muted)
        {
            if (_settings.MutedChats.ContainsKey(chatId))
            {
                _settings.MutedChats[chatId] = muted;
            }
            else
            {
                _settings.MutedChats.Add(chatId, muted);
            }
        }

        public bool IsChatMuted(string chatId)
        {
            return _settings.MutedChats.ContainsKey(chatId) && _settings.MutedChats[chatId];
        }

        public void UpdateSettings(NotificationSettings settings)
        {
            _settings = settings ?? new NotificationSettings();
            Console.WriteLine("⚙️ Настройки уведомлений обновлены");
        }

        public void ProcessMessage(IntPtr lParam)
        {
            try
            {
                int msg = lParam.ToInt32();
                
                switch (msg)
                {
                    case WM_LBUTTONDBLCLK:
                        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                        break;

                    case WM_RBUTTONDOWN:
                        ShowContextMenu();
                        break;

                    case WM_LBUTTONDOWN:
                        NotificationClicked?.Invoke(this, EventArgs.Empty);
                        break;

                    case NIN_BALLOONUSERCLICK:
                       
                        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                        break;
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void ShowContextMenu()
        {
            try
            {
                if (_trayContextMenu != null && _mainWindow != null)
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            GetCursorPos(out POINT cursorPos);
                            
                            _trayContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
                            _trayContextMenu.HorizontalOffset = cursorPos.X;
                            _trayContextMenu.VerticalOffset = cursorPos.Y;
                            _trayContextMenu.IsOpen = true;
                            
                            // Закрываем меню при потере фокуса
                            _trayContextMenu.Closed += (s, e) => _trayContextMenu.IsOpen = false;
                        }
                        catch (Exception ex)
                        {
                        }
                    });
                }
            }
            catch (Exception ex)
            {
            }
        }

        public void Dispose()
        {
            if (!_isDisposed && _iconCreated)
            {
                try
                {
                    _iconData.uFlags = 0;
                    Shell_NotifyIcon(NIM_DELETE, ref _iconData);
                }
                catch (Exception ex)
                {
                }
                finally
                {
                    _isDisposed = true;
                }
            }
        }
    }

    public partial class NotificationSettings
    {
        public bool IsEnabled { get; set; } = true;
        public bool PlaySound { get; set; } = true;
        public bool ShowBanner { get; set; } = true;
        public bool SmartNotifications { get; set; } = true;
        public bool Vibrate { get; set; } = false;
        public string SoundName { get; set; } = "default";
        public Dictionary<string, bool> MutedChats { get; set; } = new Dictionary<string, bool>();
    }
}