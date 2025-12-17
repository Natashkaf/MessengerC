// главное окно мессенджера 
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MessengerApp
{
    public partial class MessengerWindow : Window
    {
        private readonly string _userEmail;
        private readonly string _userId;
        private readonly string _idToken;
        private BitmapImage _cachedAvatar;
        private ChatService _chatService;
        private PresenceService _presenceService;
        private FirebaseRealtimeListener _realtimeListener;
        private ProfileRestService _profileService;
        private Dictionary<string, List<Message>> _activeChatMessages = new();
        private SystemNotificationManager _notificationManager;
        private NotificationSettings _notificationSettings;
        
        private string _currentContactId;
        private string _currentContactName;
        
        private ObservableCollection<Contact> _contacts;
        private ObservableCollection<Message> _messages;
        private List<Contact> _allContacts;
        
        private DispatcherTimer _typingTimer;
        private DispatcherTimer _statusTimer;
        private DateTime _lastTypingTime;

        
        private FileInfo _fileToSend;
        private int _statusCounter = 0;
        private bool _isLoadingMore = false;
        private bool _isAvatarLoading = false;
        private EnhancedChatService _enhancedChatService;
        private string _replyToMessageId = null;
        
        private const string FirebaseBaseUrl = "https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app";
        private string _activeChatId;

        public MessengerWindow(string userEmail, string userId, string idToken)
        {
            try
            {
                _contacts = new ObservableCollection<Contact>();
                _messages = new ObservableCollection<Message>();
                _allContacts = new List<Contact>();
                
                InitializeComponent();
                
                _userEmail = userEmail;
                _userId = userId;
                _idToken = idToken;

                ContactsList.ItemsSource = _contacts;
                MessagesList.ItemsSource = _messages;
                
                _profileService = new ProfileRestService(_userId, _idToken);
                _presenceService = new PresenceService(_userId, _idToken);
                _realtimeListener = new FirebaseRealtimeListener(_userId, _idToken);
                _chatService = new ChatService(_userId, _idToken);
                _enhancedChatService = new EnhancedChatService(_userId, _idToken);

                _notificationSettings = LoadNotificationSettings();
                _notificationManager = new SystemNotificationManager(
                    new System.Windows.Interop.WindowInteropHelper(this).Handle,
                    _notificationSettings
                );
                
                _notificationManager.ShowWindowRequested += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                    });
                };
                
                _notificationManager.ExitRequested += (s, e) =>
                {
                    Dispatcher.Invoke(() => Close());
                };
                
                InitializeTimers();

                Title = $"Messenger - {userEmail}";
                
                _ = LoadSavedUserDataAsync();
                _ = LoadContactsAsync();
                _realtimeListener.NewMessageReceived += RealtimeListener_NewMessageReceived;
                _realtimeListener.UserStatusChanged += RealtimeListener_UserStatusChanged;
                
                _ = _presenceService.StartTracking();
                _ = _realtimeListener.StartListening(1); 
                _ = _chatService.StartRealtimeListeners();
                
                SubscribeToEvents();
                
                _enhancedChatService.NewMessageReceived += EnhancedChatService_NewMessageReceived;
                _enhancedChatService.MessageEdited += EnhancedChatService_MessageEdited;
                _enhancedChatService.MessageDeleted += EnhancedChatService_MessageDeleted;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания окна мессенджера: {ex.Message}\n\nStackTrace: {ex.StackTrace}", 
                    "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
        //настроаивает обработку системных сообщений windows, получая дескритор окна 
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            var source = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            source.AddHook(WndProc);
        }
// обработчик сообщений windows 
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_TRAYICON = 0x0400 + 1;
    
            if (msg == WM_TRAYICON)
            {
                _notificationManager?.ProcessMessage(lParam);
                handled = true;
            }
    
            return IntPtr.Zero;
        }
// обработывает получение нового сообщения в реальном времени 
private void RealtimeListener_NewMessageReceived(object sender, Message message)
{
    
    Dispatcher.Invoke(() =>
    {
        try
        {
            message.IsMyMessage = message.SenderId == _userId;
            
            // Проверяем, для этого ли  чата сообщение
            bool isForCurrentChat = message.SenderId == _currentContactId || 
                                   message.ReceiverId == _currentContactId;
            
            if (isForCurrentChat)
            {
                // Просто добавляем в UI
                AddDateSeparatorIfNeeded(message.Timestamp);
                
                if (!_messages.Any(m => m.MessageId == message.MessageId))
                {
                    _messages.Add(message);
                    ScrollToBottom();
                    Console.WriteLine($"✅ Сообщение добавлено в чат: {message.Text}");
                    
                    // Помечаем как прочитанное
                    if (!message.IsMyMessage && !message.IsRead)
                    {
                        _ = Task.Run(async () =>
                        {
                            await MarkMessageAsReadAsync(message.MessageId);
                        });
                    }
                }
            }
            else
            {
                //показывает системное уведомление 
                var contact = _contacts.FirstOrDefault(c => c.UserId == message.SenderId);
                if (contact != null)
                {
                    // Показываем уведомление
                    _notificationManager?.ShowNotification(
                        contact.Name,
                        message.Text,
                        GetChatId(_userId, message.SenderId)
                    );
                    
                    // Обновляем счетчик непрочитанных
                    contact.UnreadCount++;
                    contact.LastMessage = message.Text.Length > 30 
                        ? message.Text.Substring(0, 27) + "..." 
                        : message.Text;
                    contact.Time = "сейчас";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.Message}");
        }
    });
}
//загружает настройки уведомлений из json файла 
private NotificationSettings LoadNotificationSettings()
{
    try
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MessengerApp",
            "notifications.json");
        
        if (File.Exists(settingsPath))
        {
            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<NotificationSettings>(json);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка загрузки настроек уведомлений: {ex.Message}");
    }
    
    return new NotificationSettings();
}
//сохраняет настройки уведомлений в jso файл 
private void SaveNotificationSettings()
{
    try
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MessengerApp");
        
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }
        
        var settingsPath = Path.Combine(appDataPath, "notifications.json");
        var json = JsonSerializer.Serialize(_notificationSettings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{ex.Message}");
    }
}
//открывает чат с выбранным контактом 
        private async Task OpenChatAsync(string otherUserId, string otherUserName)
        {
            try
            {
                _currentContactId = otherUserId;
                _currentContactName = otherUserName;
                _activeChatId = GetChatId(_userId, otherUserId);
        
                // обновляет активный чат 
                _notificationManager?.SetActiveChat(_activeChatId);

                // Регистрируем этот чат в слушателе
                _realtimeListener.AddChatToMonitor(_activeChatId);
        
                // Проверяем чат на наличие сообщений
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    await _realtimeListener.ForceCheckChatAsync(_activeChatId);
                });

                Dispatcher.Invoke(() =>
                {
                    ChatContactName.Text = otherUserName;
                    ChatContactInitials.Text = GetInitials(otherUserName);
            
                    NoChatSelectedPanel.Visibility = Visibility.Collapsed;
                    ChatHeader.Visibility = Visibility.Visible;
                    MessageInputPanel.Visibility = Visibility.Visible;
            
                    _messages.Clear();
                    MessageTextBox.Focus();
                });

                await LoadRecentMessagesAsync(otherUserId);

                var status = await _presenceService.GetUserStatusAsync(otherUserId);
                ChatContactStatus.Text = GetStatusDisplayText(status.status, status.statusText);

                var contact = _contacts.FirstOrDefault(c => c.UserId == otherUserId);
                if (contact != null)
                {
                    contact.UnreadCount = 0;
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
        // эти двое отслеживают активность окна 
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _notificationManager?.SetWindowActive(true);
            _notificationManager?.UpdateActivityTime();
        }
        
        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            _notificationManager?.SetWindowActive(false);
        }
        //обрабатывает изменения статуса пользователя 
        private void RealtimeListener_UserStatusChanged(object sender, UserStatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    
                    var contact = _contacts.FirstOrDefault(c => c.UserId == e.UserId);
                    if (contact != null)
                    {
                        contact.Status = e.Status;
                        contact.StatusText = e.StatusText;
                        
                        if (e.UserId == _currentContactId)
                        {
                            ChatContactStatus.Text = GetStatusDisplayText(e.Status, e.StatusText);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Message}");
                }
            });
        }
        //завершает работу приложения при закрытии
        protected override async void OnClosed(EventArgs e)
        {
            try
            {
                await _presenceService?.SetStatusAsync("offline", "не в сети");
                _realtimeListener?.Dispose();
                _presenceService?.Dispose();
                
                SaveNotificationSettings();
                
                _notificationManager?.Dispose();
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
        //сворачивает приложение в трей 
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_notificationSettings.IsEnabled)
            {
                e.Cancel = true;
                Hide();
                
                // Показываем уведомление о том, что приложение свернуто в трей
                _notificationManager?.ShowNotification(
                    "Messenger",
                    "Приложение свернуто в системный трей",
                    null
                );
                
                return;
            }
            
            base.OnClosing(e);
        }
// помечает сообщение как прочитанное 
        private async Task MarkMessageAsReadAsync(string messageId)
        {
            try
            {
                var message = _messages.FirstOrDefault(m => m.MessageId == messageId);
                if (message != null && !message.IsMyMessage && !message.IsRead)
                {
                    message.IsRead = true;
                
                    if (_chatService != null)
                    {
                        await _chatService.MarkMessageAsReadAsync(messageId);
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
//инициализирует таймеры 
        private void InitializeTimers()
        {
            _typingTimer = new DispatcherTimer();
            _typingTimer.Interval = TimeSpan.FromSeconds(3);
            _typingTimer.Tick += TypingTimer_Tick;

            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(30);
            _statusTimer.Tick += StatusTimer_Tick;
            _statusTimer.Start();
        }
//подписывает окно на события сервиса чата 
        private void SubscribeToEvents()
        {
            _chatService.NewMessageReceived += ChatService_NewMessageReceived;
            _chatService.MessageStatusUpdated += ChatService_MessageStatusUpdated;
            _chatService.TypingIndicatorReceived += ChatService_TypingIndicatorReceived;
            _chatService.ContactStatusChanged += ChatService_ContactStatusChanged;
        }
        // обработчик нажатия на три точки 
        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var contextMenu = new ContextMenu();
            
            var notificationSettingsItem = new MenuItem { Header = "Настройки уведомлений", FontSize = 14 };
            notificationSettingsItem.Click += (s, args) => ShowNotificationSettings();
            var settingsItem = new MenuItem { Header = "Настройки профиля", FontSize = 14 };
            var logoutItem = new MenuItem { Header = "Выйти", FontSize = 14 };

            settingsItem.Click += (s, args) => UserProfileButton_Click(sender, e);
                    logoutItem.Click += (s, args) => Close();
                    
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(notificationSettingsItem);
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(logoutItem);

            contextMenu.IsOpen = true;
        }
        // открывает окно настроек уведомлений 
        private void ShowNotificationSettings()
        {
            var settingsWindow = new NotificationSettingsWindow(_notificationSettings);
            settingsWindow.Owner = this;
            settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    
            if (settingsWindow.ShowDialog() == true)
            {
                _notificationSettings = settingsWindow.Settings;
                _notificationManager?.UpdateSettings(_notificationSettings);
                SaveNotificationSettings();
            }
        }
//обработчик нажатия на профиль 
        private async void UserProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var profile = await _profileService.GetProfileAsync();

                var profileWindow = new ProfileWindow(_userId, _idToken, profile);
                profileWindow.Owner = this;
                profileWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                profileWindow.ProfileUpdated += async (s, updatedProfile) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        UserNameText.Text = updatedProfile.DisplayName;
                        UserStatusText.Text = updatedProfile.StatusText ?? "онлайн";
                    });

                    // Обновляем аватарку 
                    if (!string.IsNullOrEmpty(updatedProfile.AvatarBase64))
                    {
                        var avatar = ConvertBase64ToBitmap(updatedProfile.AvatarBase64);
                        if (avatar != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                UserAvatarImage.Source = avatar;
                                ApplyAvatarAnimation(UserAvatarImage);
                            });
                        }
                    }
                };

                profileWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия профиля: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
// обработчик нового сообщения от чат-сервиса 
        private void ChatService_NewMessageReceived(object sender, MessageEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var message = e.Message;
                    
                    if ((message.SenderId == _currentContactId && message.ReceiverId == _userId) ||
                        (message.ReceiverId == _currentContactId && message.SenderId == _userId))
                    {
                        AddDateSeparatorIfNeeded(message.Timestamp);
                        
                        message.IsMyMessage = message.SenderId == _userId;
                        _messages.Add(message);
                        
                        ScrollToBottom();
                        
                        if (!message.IsMyMessage && !message.IsRead)
                        {
                            _ = _chatService.MarkMessagesAsReadAsync(_currentContactId);
                        }
                    }
                    else
                    {
                        var contact = _contacts.FirstOrDefault(c => c.UserId == message.SenderId);
                        if (contact != null)
                        {
                            contact.UnreadCount++;
                            contact.LastMessage = message.Text.Length > 50 
                                ? message.Text.Substring(0, 47) + "..." 
                                : message.Text;
                            contact.Time = FormatTime(message.Timestamp);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Message}");
                }
            });
        }
// обновляет статус сообщения в UI 
        private void ChatService_MessageStatusUpdated(object sender, MessageStatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var message = _messages.FirstOrDefault(m => m.MessageId == e.MessageId);
                if (message != null)
                {
                    message.Status = e.Status;
                }
            });
        }
// обрабатывает индикатор набора текста 
        private void ChatService_TypingIndicatorReceived(object sender, TypingIndicatorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.UserId == _currentContactId)
                {
                    if (e.IsTyping)
                    {
                        TypingIndicator.Visibility = Visibility.Visible;
                        TypingText.Text = $"{_currentContactName} печатает...";
                    }
                    else
                    {
                        TypingIndicator.Visibility = Visibility.Collapsed;
                    }
                }
            });
        }
// обрабатывает изменение статуса контакта 
        private void ChatService_ContactStatusChanged(object sender, ContactStatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var contact = _contacts.FirstOrDefault(c => c.UserId == e.UserId);
                if (contact != null)
                {
                    contact.Status = e.Status;
                    contact.StatusText = e.StatusText;
                    
                    if (e.UserId == _currentContactId)
                    {
                        ChatContactStatus.Text = GetStatusDisplayText(e.Status, e.StatusText);
                    }
                }
            });
        }
//отображает сохраненные данные пользователя 
        private async Task LoadSavedUserDataAsync()
        {
            try
            {
                var profile = await _profileService.GetProfileAsync();
                
                Dispatcher.Invoke(() => 
                {
                    if (!string.IsNullOrEmpty(profile?.DisplayName))
                    {
                        UserNameText.Text = profile.DisplayName;
                    }
                    else
                    {
                        UserNameText.Text = _userEmail;
                    }

                    if (!string.IsNullOrEmpty(profile?.StatusText))
                    {
                        UserStatusText.Text = profile.StatusText;
                    }
                    else
                    {
                        UserStatusText.Text = "онлайн";
                    }
                });

                await LoadAvatarAsync();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    UserNameText.Text = _userEmail;
                    UserStatusText.Text = "онлайн";
                });
                SetDefaultAvatar();
            }
        }
        // загружает аватарку пользователя 
        private async Task LoadAvatarAsync()
        {
            try
            {
                SetAvatarPlaceholder();

                var base64Avatar = await _profileService.GetAvatarAsync(_userId);
        
                if (!string.IsNullOrEmpty(base64Avatar))
                {
                    var avatar = ConvertBase64ToBitmap(base64Avatar);
                    if (avatar != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UserAvatarImage.Source = avatar;
                            ApplyAvatarAnimation(UserAvatarImage);
                        });
                        return;
                    }
                }

                SetDefaultAvatar();
            }
            catch (Exception ex)
            {
                SetDefaultAvatar();
            }
        }
// загружает список контактов 
        private async Task LoadContactsAsync()
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;

                var contacts = await _chatService.GetContactsAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    _contacts.Clear();
                    _allContacts = new List<Contact>();

                    foreach (var contact in contacts)
                    {
                        _contacts.Add(contact);
                        _allContacts.Add(contact);
                    }

                    LoadingIndicator.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                });
            }
        }
        //конвертирует сообщение из формата fiebase в объект message 
        private Message ConvertFirebaseMessageToLocal(FirebaseMessage firebaseMsg, string messageId)
        {
            try
            {
                if (firebaseMsg == null)
                {
                    return null;
                }
                
                DateTime timestamp = DateTime.UtcNow;
                
                if (firebaseMsg.timestamp != null)
                {
                    try
                    {
                        string timestampStr = firebaseMsg.timestamp.ToString();
                        
                        if (DateTime.TryParse(timestampStr, out var parsedTime))
                        {
                            timestamp = parsedTime;
                        }
                        else if (long.TryParse(timestampStr, out var milliseconds))
                        {
                            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex.Message}");
                    }
                }
                
                var message = new Message
                {
                    MessageId = messageId ?? firebaseMsg.messageId ?? Guid.NewGuid().ToString(),
                    SenderId = firebaseMsg.senderId,
                    ReceiverId = firebaseMsg.receiverId,
                    Text = firebaseMsg.text ?? "",
                    Timestamp = timestamp,
                    IsMyMessage = firebaseMsg.senderId == _userId,
                    Status = Enum.TryParse<MessageStatus>(firebaseMsg.status, out var status) ? status : MessageStatus.Sent,
                    HasAttachment = firebaseMsg.hasAttachment,
                    FileName = firebaseMsg.fileName,
                    FileData = firebaseMsg.fileData,
                    FileSize = firebaseMsg.fileSize ?? 0,
                    IsEdited = firebaseMsg.isEdited,
                    IsDeleted = firebaseMsg.isDeleted,
                    IsRead = firebaseMsg.isRead
                };
                return message;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
//делает серый плейсхолдер аватрки, пока она грузится 
        private void SetAvatarPlaceholder()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var drawingVisual = new DrawingVisual();
                    using (var drawingContext = drawingVisual.RenderOpen())
                    {
                        var brush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                        drawingContext.DrawEllipse(brush, null, new Point(22.5, 22.5), 22.5, 22.5);
                    }

                    var placeholder = new RenderTargetBitmap(45, 45, 96, 96, PixelFormats.Pbgra32);
                    placeholder.Render(drawingVisual);
                    placeholder.Freeze();

                    UserAvatarImage.Source = placeholder;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//создает аватарку по умолчанию 
        private void SetDefaultAvatar()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var firstChar = !string.IsNullOrEmpty(_userEmail) ? _userEmail[0].ToString().ToUpper() : "?";
                    
                    var drawingVisual = new DrawingVisual();

                    using (var drawingContext = drawingVisual.RenderOpen())
                    {
                        int hash = 0;
                        foreach (char c in _userEmail)
                        {
                            hash = (hash * 31) + c;
                        }

                        var colors = new[]
                        {
                            Color.FromRgb(220, 76, 76),
                            Color.FromRgb(76, 175, 80),
                            Color.FromRgb(100, 149, 237),
                            Color.FromRgb(255, 152, 0),
                            Color.FromRgb(156, 39, 176),
                            Color.FromRgb(0, 150, 136)
                        };

                        var color = colors[Math.Abs(hash) % colors.Length];
                        var brush = new SolidColorBrush(color);

                        drawingContext.DrawEllipse(brush, null, new Point(22.5, 22.5), 22.5, 22.5);

                        var formattedText = new FormattedText(
                            firstChar,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Arial"),
                            18,
                            Brushes.White,
                            96);

                        drawingContext.DrawText(formattedText,
                            new Point(22.5 - formattedText.Width / 2,
                                22.5 - formattedText.Height / 2));
                    }

                    var bmp = new RenderTargetBitmap(45, 45, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(drawingVisual);
                    bmp.Freeze();

                    UserAvatarImage.Source = bmp;
                    ApplyAvatarAnimation(UserAvatarImage);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//конвертирует base64 строку в изображение 
        private BitmapImage ConvertBase64ToBitmap(string base64String, int decodeWidth = 45)
        {
            try
            {
                if (string.IsNullOrEmpty(base64String))
                    return null;

                var base64Data = base64String;
                
                if (base64String.Contains(","))
                {
                    base64Data = base64String.Split(',')[1];
                }

                var bytes = Convert.FromBase64String(base64Data);

                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();

                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.DecodePixelHeight = decodeWidth;
                bitmap.EndInit();
                
                if (bitmap.CanFreeze)
                {
                    bitmap.Freeze();
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
                return null;
            }
        }
// анимация плавного появления аватарки 
        private void ApplyAvatarAnimation(Image image)
        {
            if (image == null) return;

            try
            {
                Dispatcher.Invoke(() =>
                {
                    var fadeIn = new DoubleAnimation
                    {
                        From = image.Opacity,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    };

                    image.BeginAnimation(Image.OpacityProperty, fadeIn);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//открывает чат с выбранным контактом 
        private async void ContactsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ContactsList.SelectedItem is Contact selectedContact)
            {
                await OpenChatAsync(selectedContact.UserId, selectedContact.Name);
            }
        }

        private async void ContactItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is Contact contact)
            {
                await OpenChatAsync(contact.UserId, contact.Name);
            }
        }
        //загружает недавние сообщения чата 
        private async Task LoadRecentMessagesAsync(string otherUserId)
        {
            try
            {
                var chatId = GetChatId(_userId, otherUserId);
                var messages = await GetAllMessagesForChatAsync(chatId);
                
                Dispatcher.Invoke(() =>
                {
                    if (messages.Any())
                    {
                        DisplayMessages(messages);
                        
                    }

                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
// загружает до 50 сообщений из истории чата 
        private async Task<List<Message>> GetAllMessagesForChatAsync(string chatId)
        {
            try
            {
                var chatUrl = $"{FirebaseBaseUrl}/chats/{chatId}/messages.json?auth={_idToken}&orderBy=\"timestamp\"&limitToLast=50";
                
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await client.GetAsync(chatUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var messagesDict = JsonSerializer.Deserialize<Dictionary<string, FirebaseMessage>>(json);
                        if (messagesDict != null)
                        {
                            var messages = new List<Message>();
                            
                            foreach (var msg in messagesDict.OrderBy(m => m.Value.timestamp?.ToString() ?? ""))
                            {
                                var message = ConvertFirebaseMessageToLocal(msg.Value, msg.Key);
                                if (message != null)
                                {
                                    message.IsMyMessage = message.SenderId == _userId;
                                    messages.Add(message);
                                }
                            }
                            
                            return messages;
                        }
                    }
                }
                
                return new List<Message>();
            }
            catch (Exception ex)
            {
                return new List<Message>();
            }
        }
        //отображает список сообщений в UI
        private void DisplayMessages(List<Message> messages)
        {
            DateTime lastDate = DateTime.MinValue;

            foreach (var message in messages.OrderBy(m => m.Timestamp))
            {
                if (_messages.Any(m => m.MessageId == message.MessageId))
                    continue;
                    
                var messageDate = message.Timestamp.Date;
                if (messageDate != lastDate.Date)
                {
                    _messages.Add(new Message
                    {
                        IsDateSeparator = true,
                        Date = FormatDate(messageDate)
                    });
                    lastDate = messageDate;
                }

                _messages.Add(message);
            }

            ScrollToBottom();
        }
        
        //создает уникальный индентификатор чата 
        private string GetChatId(string user1, string user2)
        {
            var ids = new[] { user1, user2 }.OrderBy(id => id).ToArray();
            return $"{ids[0]}_{ids[1]}";
        }
// добавляет разделитель даты в чат 
        private void AddDateSeparatorIfNeeded(DateTime timestamp)
        {
            if (!_messages.Any(m => !m.IsDateSeparator))
            {
                _messages.Add(new Message
                {
                    IsDateSeparator = true,
                    Date = FormatDate(timestamp.Date)
                });
            }
            else
            {
                var lastMessageDate = _messages.Last(m => !m.IsDateSeparator).Timestamp.Date;
                if (timestamp.Date != lastMessageDate.Date)
                {
                    _messages.Add(new Message
                    {
                        IsDateSeparator = true,
                        Date = FormatDate(timestamp.Date)
                    });
                }
            }
        }
// обработчик кнопки отправки сообщения 
        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                await SendUniversalMessageAsync(MessageTextBox.Text.Trim());
            }
        }
//отправляет сообщение при нажатии на enter 
        private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                if (!string.IsNullOrWhiteSpace(MessageTextBox.Text))
                {
                    await SendUniversalMessageAsync(MessageTextBox.Text.Trim());
                }
            }
        }
//включает/выключает кнопку отправки 
        private async void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SendMessageButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageTextBox.Text);

            if (!string.IsNullOrWhiteSpace(MessageTextBox.Text) && !string.IsNullOrEmpty(_currentContactId))
            {
                _lastTypingTime = DateTime.UtcNow;

                if (!_typingTimer.IsEnabled)
                {
                    await _chatService.SendTypingIndicatorAsync(_currentContactId, true);
                    _typingTimer.Start();
                }
            }
        }
//отправлет индикатор, что юзер перестал печатать 
        private async void TypingTimer_Tick(object sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastTypingTime).TotalSeconds >= 3)
            {
                _typingTimer.Stop();
                if (!string.IsNullOrEmpty(_currentContactId))
                {
                    await _chatService.SendTypingIndicatorAsync(_currentContactId, false);
                }
            }
        }
//каждые 30 секунд обновляет статусы всех контактов 
        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                foreach (var contact in _contacts)
                {
                    try
                    {
                        var status = await _presenceService.GetUserStatusAsync(contact.UserId);
                        Dispatcher.Invoke(() =>
                        {
                            contact.Status = status.status;
                            contact.StatusText = status.statusText;

                            if (contact.UserId == _currentContactId)
                            {
                                ChatContactStatus.Text = GetStatusDisplayText(status.status, status.statusText);
                            }
                        });
                    }
                    catch
                    {

                    }


                }
            });
        }
//обновляет аватарочку 
        public void UpdateAvatar(string base64Avatar)
        {
            if (string.IsNullOrEmpty(base64Avatar)) return;
        
            try
            {
                var avatar = ConvertBase64ToBitmap(base64Avatar, 45);
                if (avatar != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _cachedAvatar = avatar;
                        UserAvatarImage.Source = avatar;
                        ApplyAvatarAnimation(UserAvatarImage);
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
        //обновляет профиль 
        public void UpdateProfileInfo(string displayName, string statusText)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(displayName))
                {
                    UserNameText.Text = displayName;
                }
                
                if (!string.IsNullOrEmpty(statusText))
                {
                    UserStatusText.Text = statusText;
                }
            });
        }
        //обновляет последнее сообщение  от контакта 
        private void UpdateContactLastMessage(string contactId, string message, DateTime timestamp)
        {
            var contact = _contacts.FirstOrDefault(c => c.UserId == contactId);
            if (contact != null)
            {
                contact.LastMessage = message.Length > 50 ? message.Substring(0, 47) + "..." : message;
                contact.Time = FormatTime(timestamp);
            }
        }
//прокручивает чат в самый конец 
        private void ScrollToBottom()
        {
            Dispatcher.BeginInvoke(() =>
            {
                MessagesScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
//ищет контакт в реальном времени 
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (SearchTextBox == null) return;
        
                var searchText = SearchTextBox.Text?.ToLower() ?? "";
        
                if (string.IsNullOrWhiteSpace(searchText) || searchText == "поиск контактов...")
                {
                    if (ContactsList != null)
                    {
                        ContactsList.ItemsSource = _contacts ?? new ObservableCollection<Contact>();
                    }
                    return;
                }
        
                if (_allContacts == null) return;
        
                var filtered = _allContacts.Where(c => 
                    (c?.Name?.ToLower() ?? "").Contains(searchText) || 
                    (c?.LastMessage?.ToLower() ?? "").Contains(searchText)).ToList();
        
                if (ContactsList != null)
                {
                    ContactsList.ItemsSource = filtered;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//загружает предыдущие сообщения 
        private async void MessagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset == 0 && _messages.Count > 0 && !_isLoadingMore && 
                !string.IsNullOrEmpty(_currentContactId))
            {
                _isLoadingMore = true;
                await LoadMoreMessagesAsync();
                _isLoadingMore = false;
            }
        }
//загружает более старые сообщения 
        private async Task LoadMoreMessagesAsync()
        {
            try
            {
                var oldestMessage = _messages.FirstOrDefault(m => !m.IsDateSeparator);
                if (oldestMessage != null)
                {
                    var moreMessages = await _chatService.GetMoreMessagesAsync(_currentContactId, 
                        oldestMessage.Timestamp, 20);
                    
                    if (moreMessages.Any())
                    {
                        var newMessages = new List<Message>();
                        DateTime lastDate = DateTime.MinValue;
                        
                        foreach (var message in moreMessages.OrderBy(m => m.Timestamp))
                        {
                            var messageDate = message.Timestamp.Date;
                            if (messageDate != lastDate.Date)
                            {
                                newMessages.Add(new Message
                                {
                                    IsDateSeparator = true,
                                    Date = FormatDate(messageDate)
                                });
                                lastDate = messageDate;
                            }
                            
                            newMessages.Add(message);
                        }
                        
                        for (int i = newMessages.Count - 1; i >= 0; i--)
                        {
                            _messages.Insert(0, newMessages[i]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//обработчик нажатия на стрелочку обратно 
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _currentContactId = null;
            _currentContactName = null;
            
            NoChatSelectedPanel.Visibility = Visibility.Visible;
            ChatHeader.Visibility = Visibility.Collapsed;
            MessageInputPanel.Visibility = Visibility.Collapsed;
            
            _messages.Clear();
            MessageTextBox.Clear();
            TypingIndicator.Visibility = Visibility.Collapsed;
        }
        //обработчик нажатия на скрепку 
        private async void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Все файлы (*.*)|*.*|" +
                         "Изображения (*.jpg;*.jpeg;*.png;*.gif;*.bmp)|*.jpg;*.jpeg;*.png;*.gif;*.bmp|" +
                         "Видео (*.mp4;*.avi;*.mov)|*.mp4;*.avi;*.mov|" +
                         "Документы (*.pdf;*.doc;*.docx;*.txt)|*.pdf;*.doc;*.docx;*.txt|" +
                         "Аудио (*.mp3;*.wav)|*.mp3;*.wav",
                Title = "Выберите файл",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var fileInfo = new FileInfo(openFileDialog.FileName);
                
                if (fileInfo.Length > 15 * 1024 * 1024)
                {
                    MessageBox.Show("Файл слишком большой. Максимум 15MB", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                await SendUniversalMessageAsync($"[Файл: {fileInfo.Name}]", fileInfo);
            }
        }
        //устанавливает иконку для файла 
        private string GetFileIcon(string fileType)
        {
            return fileType switch
            {
                "image" => "🖼️",
                "video" => "🎬",
                "audio" => "🎵",
                "document" => "📄",
                _ => "📎"
            };
        }
//обработчик кнопки"скачать на файле" 
        private async void DownloadFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Message message && message.HasAttachment)
            {
                try
                {
                    var saveFileDialog = new SaveFileDialog
                    {
                        FileName = message.FileName,
                        Filter = $"Файлы (*{Path.GetExtension(message.FileName)})|*{Path.GetExtension(message.FileName)}|Все файлы (*.*)|*.*",
                        Title = "Сохранить файл"
                    };

                    if (saveFileDialog.ShowDialog() == true)
                    {
                        var progressWindow = new UploadProgressWindow(message.FileName);
                        progressWindow.Title = "Скачивание файла";
                        progressWindow.Owner = this;
                        progressWindow.Show();
                        
                        var fileBytes = Convert.FromBase64String(message.FileData);
                        
                        await File.WriteAllBytesAsync(saveFileDialog.FileName, fileBytes);
                        
                        progressWindow.Close();
                        
                        MessageBox.Show($"Файл сохранен:\n{saveFileDialog.FileName}", "Успешно",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения файла: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
//универсальный метод для отправки сообщений всех типов 
private async Task SendUniversalMessageAsync(string text, FileInfo fileInfo = null)
{
    if (string.IsNullOrEmpty(_currentContactId))
    {
        MessageBox.Show("Сначала выберите чат", "Ошибка", 
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    try
    {
        
        var messageId = Guid.NewGuid().ToString();
        var chatId = GetChatId(_userId, _currentContactId);
        
        var message = new Message
        {
            MessageId = messageId,
            SenderId = _userId,
            ReceiverId = _currentContactId,
            Text = text,
            Timestamp = DateTime.UtcNow,
            IsMyMessage = true,
            Status = MessageStatus.Sending,
            HasAttachment = fileInfo != null
        };

        if (fileInfo != null)
        {
            message.FileName = fileInfo.Name;
            message.FileSize = fileInfo.Length;
            
            var fileBytes = await File.ReadAllBytesAsync(fileInfo.FullName);
            message.FileData = Convert.ToBase64String(fileBytes);
            message.HasAttachment = true;
            message.Text = $"[Файл: {fileInfo.Name}]";
        }

        Dispatcher.Invoke(() =>
        {
            AddDateSeparatorIfNeeded(message.Timestamp);
            _messages.Add(message);
            ScrollToBottom();
            MessageTextBox.Clear();
            SendMessageButton.IsEnabled = false;
        });

        // Сохраняем сообщение в Firebase
        var success = await SaveMessageToFirebaseAsync(message, chatId);
        
        if (success)
        {
            message.Status = MessageStatus.Sent;
            UpdateContactLastMessage(_currentContactId, message.Text, message.Timestamp);
            
            
            // Принудительно обновляем чат у собеседника 
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                await SendDeliveryNotificationAsync(messageId);
            });
        }
        else
        {
            message.Status = MessageStatus.Error;
            MessageBox.Show("Не удалось доставить сообщение", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
//сохранение сообщения в fierbase 
private async Task<bool> SaveMessageToFirebaseAsync(Message message, string chatId)
{
    try
    {
        var firebaseMessage = new
        {
            messageId = message.MessageId,
            senderId = message.SenderId,
            receiverId = message.ReceiverId,
            text = message.Text,
            timestamp = message.Timestamp.ToString("o"),
            status = "sent",
            isRead = false,
            isEdited = false,
            isDeleted = false,
            hasAttachment = message.HasAttachment,
            fileName = message.FileName,
            fileData = message.FileData,
            fileSize = message.FileSize
        };

        var json = JsonSerializer.Serialize(firebaseMessage);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        
        // Сохраняем в общий чат
        var chatUrl = $"{FirebaseBaseUrl}/chats/{chatId}/messages/{message.MessageId}.json";
        var chatResponse = await client.PutAsync(chatUrl, content);
        
        
        if (chatResponse.IsSuccessStatusCode)
        {
            // Обновляем информацию о последнем сообщении в чате
            await UpdateChatInfoAsync(chatId, message.Text);
            
            // Сохраняем в инбокс получателя
            var receiverInboxUrl = $"{FirebaseBaseUrl}/userMessages/{message.ReceiverId}/{message.MessageId}.json";
            var receiverResponse = await client.PutAsync(receiverInboxUrl, content);
            
            return true;
        }
        
        return false;
    }
    catch (Exception ex)
    {
        return false;
    }
}
//обновляет инфу о чате 
private async Task UpdateChatInfoAsync(string chatId, string lastMessage)
{
    try
    {
        var chatInfo = new
        {
            lastMessage = lastMessage,
            lastMessageTime = DateTime.UtcNow.ToString("o"),
            lastMessageSenderId = _userId,
            updatedAt = DateTime.UtcNow.ToString("o")
        };
        
        var json = JsonSerializer.Serialize(chatInfo);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var client = new HttpClient();
        
        // Обновляем информацию о чате
        var chatUrl = $"{FirebaseBaseUrl}/chats/{chatId}/lastMessage.json";
        await client.PutAsync(chatUrl, new StringContent($"\"{lastMessage}\"", Encoding.UTF8, "application/json"));
        
        var timeUrl = $"{FirebaseBaseUrl}/chats/{chatId}/lastMessageTime.json";
        await client.PutAsync(timeUrl, new StringContent($"\"{DateTime.UtcNow.ToString("o")}\"", Encoding.UTF8, "application/json"));

    }
    catch (Exception ex)
    {
        Console.WriteLine($"{ex.Message}");
    }
}

        //обработчик действия с сообщением 
        private void ShowMessageContextMenu(Message message, FrameworkElement target)
        {
            if (message.IsDateSeparator) return;
            
            var contextMenu = new ContextMenu();
            
            if (!message.IsDeleted)
            {
                var copyItem = new MenuItem { Header = "Копировать текст", Tag = message };
                copyItem.Click += (s, e) => CopyMessageText(message);
                contextMenu.Items.Add(copyItem);
            }
            
            if (!message.IsDeleted)
            {
                var replyItem = new MenuItem { Header = "↩Ответить", Tag = message };
                replyItem.Click += (s, e) => ReplyToMessage(message);
                contextMenu.Items.Add(replyItem);
            }
            
            if (!message.IsDeleted)
            {
                var forwardItem = new MenuItem { Header = "↪Переслать", Tag = message };
                forwardItem.Click += (s, e) => ForwardMessage(message);
                contextMenu.Items.Add(forwardItem);
            }
            
            if (message.IsMyMessage && !message.IsDeleted)
            {
                contextMenu.Items.Add(new Separator());
                
                if (message.CanBeEdited)
                {
                    var editItem = new MenuItem { Header = " Редактировать", Tag = message };
                    editItem.Click += (s, e) => EditMessage(message);
                    contextMenu.Items.Add(editItem);
                }
                
                var deleteSubMenu = new MenuItem { Header = "Удалить" };
                
                var deleteForMe = new MenuItem { Header = "Удалить для меня", Tag = message };
                deleteForMe.Click += (s, e) => DeleteMessage(message, false);
                
                var deleteForEveryone = new MenuItem { Header = "Удалить для всех", Tag = message };
                deleteForEveryone.Click += (s, e) => DeleteMessage(message, true);
                
                deleteSubMenu.Items.Add(deleteForMe);
                deleteSubMenu.Items.Add(deleteForEveryone);
                contextMenu.Items.Add(deleteSubMenu);
            }
            
            contextMenu.PlacementTarget = target;
            contextMenu.IsOpen = true;
        }
//редактирование сообщения 
        private async void EditMessage(Message message)
        {
            if (string.IsNullOrEmpty(message.MessageId) || !message.CanBeEdited)
                return;
            
            var editWindow = new EditMessageWindow(message.Text);
            editWindow.Owner = this;
            
            if (editWindow.ShowDialog() == true)
            {
                var newText = editWindow.EditedText;
                if (!string.IsNullOrWhiteSpace(newText) && newText != message.Text)
                {
                    try
                    {
                        message.Text = newText;
                        message.IsEdited = true;
                        
                        ShowNotification("Сообщение отредактировано");
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var success = await _enhancedChatService.EditMessageAsync(
                                    message.MessageId, 
                                    newText);
                                
                                if (!success)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        message.Text = editWindow.OriginalText;
                                        message.IsEdited = false;
                                        ShowNotification("Не удалось отредактировать сообщение на сервере");
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    message.Text = editWindow.OriginalText;
                                    message.IsEdited = false;
                                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                                        MessageBoxButton.OK, MessageBoxImage.Error);
                                });
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
//удаление сообщения 
        private async void DeleteMessage(Message message, bool deleteForEveryone)
        {
            if (string.IsNullOrEmpty(message.MessageId))
                return;
            
            var messageText = deleteForEveryone 
                ? "Удалить сообщение для всех участников чата?" 
                : "Удалить сообщение только для вас?";
            
            var result = MessageBox.Show(messageText, "Подтверждение удаления",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (deleteForEveryone)
                    {
                        _messages.Remove(message);
                        ShowNotification("Сообщение удалено для всех");
                    }
                    else
                    {
                        message.IsDeleted = true;
                        ShowNotification("Сообщение удалено для вас");
                    }
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var success = await _enhancedChatService.DeleteMessageAsync(
                                message.MessageId, 
                                deleteForEveryone);
                            
                            if (!success)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    if (deleteForEveryone)
                                    {
                                        _messages.Add(message);
                                    }
                                    else
                                    {
                                        message.IsDeleted = false;
                                    }
                                    ShowNotification("Не удалось удалить сообщение на сервере");
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (deleteForEveryone)
                                {
                                    _messages.Add(message);
                                }
                                else
                                {
                                    message.IsDeleted = false;
                                }
                                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
//обновляет текст редактированного сообщения в UI 
        private void EnhancedChatService_MessageEdited(object sender, MessageEditedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var message = _messages.FirstOrDefault(m => m.MessageId == e.MessageId);
                if (message != null)
                {
                    message.Text = e.NewText;
                    message.IsEdited = true;
                }
            });
        }
//обновляет UI после удаления сообщения 
        private void EnhancedChatService_MessageDeleted(object sender, MessageDeletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var message = _messages.FirstOrDefault(m => m.MessageId == e.MessageId);
                if (message != null)
                {
                    if (e.DeleteForEveryone)
                    {
                        _messages.Remove(message);
                    }
                    else if (e.DeleterId == _userId)
                    {
                        message.IsDeleted = true;
                    }
                }
            });
        }
//обработчик нажатия правой кнопкой мыши 
        private void MessageContainer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Border border && border.DataContext is Message message)
                {
                    ShowMessageContextMenu(message, border);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//создает инициалы имени 
        private string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "??";

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            }
            else if (parts.Length == 1 && parts[0].Length >= 2)
            {
                return parts[0].Substring(0, 2).ToUpper();
            }
            else
            {
                return name.Length >= 2 ? name.Substring(0, 2).ToUpper() : name.ToUpper();
            }
        }
//получает статус 
        private string GetStatusDisplayText(string status, string statusText)
        {
            var displayText = status switch
            {
                "online" => "онлайн",
                "away" => "отошел",
                _ => "не в сети"
            };

            if (!string.IsNullOrEmpty(statusText))
            {
                displayText += $" · {statusText}";
            }

            return displayText;
        }
//формат времени 
        private string FormatTime(DateTime timestamp)
        {
            var now = DateTime.Now;
            var time = timestamp.ToLocalTime();

            if (time.Date == now.Date)
            {
                return time.ToString("HH:mm");
            }
            else if (time.Date == now.Date.AddDays(-1))
            {
                return "вчера";
            }
            else if (time.Date > now.Date.AddDays(-7))
            {
                var dayName = time.ToString("dddd");
                return dayName.Substring(0, 3);
            }
            else
            {
                return time.ToString("dd.MM.yy");
            }
        }
//формат даты 
        private string FormatDate(DateTime date)
        {
            var now = DateTime.Now;

            if (date.Date == now.Date)
            {
                return "Сегодня";
            }
            else if (date.Date == now.Date.AddDays(-1))
            {
                return "Вчера";
            }
            else if (date.Date > now.Date.AddDays(-7))
            {
                return date.ToString("dddd");
            }
            else
            {
                return date.ToString("dd MMMM yyyy");
            }
        }
        //копирование текста сообщения 
        private void CopyMessageText(Message message)
        {
            try
            {
                if (!string.IsNullOrEmpty(message.Text))
                {
                    Clipboard.SetText(message.Text);
                    ShowNotification("Текст скопирован в буфер обмена");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//пересылка сообщения 
        private void ReplyToMessage(Message message)
        {
            var quote = $"> {message.Text}\n\n";
            MessageTextBox.Text = quote + MessageTextBox.Text;
            MessageTextBox.CaretIndex = quote.Length;
            MessageTextBox.Focus();
            
            _replyToMessageId = message.MessageId;
        }
//ответ на сообщение 
        private async void ForwardMessage(Message message)
        {
            var contactsWindow = new SelectContactWindow(_contacts);
            contactsWindow.Owner = this;
            
            if (contactsWindow.ShowDialog() == true)
            {
                var selectedContact = contactsWindow.SelectedContact;
                if (selectedContact != null && !string.IsNullOrEmpty(message.Text))
                {
                    try
                    {
                        await _chatService.SendMessageAsync(selectedContact.UserId, 
                            $"Пересланное сообщение: {message.Text}");
                        
                        ShowNotification($"Сообщение переслано {selectedContact.Name}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка пересылки: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ShowNotification(string message){
            //тут будет показ уведомления 
        }
// обабатывает сообщения и отправляет их в UI 
        private void EnhancedChatService_NewMessageReceived(object sender, EnhancedMessageEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var message = new Message
                    {
                        MessageId = e.MessageId,
                        SenderId = e.SenderId,
                        ReceiverId = e.ReceiverId,
                        Text = e.Text,
                        Timestamp = e.Timestamp,
                        IsMyMessage = e.SenderId == _userId
                    };
                    
                    if ((e.SenderId == _currentContactId && e.ReceiverId == _userId) ||
                        (e.ReceiverId == _currentContactId && e.SenderId == _userId))
                    {
                        AddDateSeparatorIfNeeded(message.Timestamp);
                        _messages.Add(message);
                        ScrollToBottom();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" {ex.Message}");
                }
            });
        }
//отправляет уведомление о доставке 
        private async Task SendDeliveryNotificationAsync(string messageId)
        {
            try
            {
                var receipt = new
                {
                    type = "delivery_receipt",
                    messageId = messageId,
                    senderId = _userId,
                    receiverId = _currentContactId,
                    status = "delivered",
                    timestamp = DateTime.UtcNow.ToString("o")
                };
                
                var receiptId = $"{messageId}_delivered_{Guid.NewGuid().ToString().Substring(0, 8)}";
                var url = $"{FirebaseBaseUrl}/messageReceipts/{receiptId}.json?auth={_idToken}";
                
                await SaveToFirebaseAsync(url, receipt);
                Console.WriteLine($" {messageId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }

        //сохранение в fierbase 
        private async Task<bool> SaveToFirebaseAsync(string url, object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                using var client = new HttpClient();
                var response = await client.PutAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    return true;
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }
//обработчик закрытия окна 
        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (_presenceService != null)
                {
                    await _presenceService.SetStatusAsync("offline", "не в сети");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
//обработчик клика по аватарке 
        private void UserAvatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            UserProfileButton_Click(sender, null);
        }
//обработчик клика на три точки 
        private void MenuButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MenuButton_Click(sender, null);
        }
//обработчик поиска контактов 
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var searchWindow = new SearchWindow(_userId, _idToken, _chatService);
                searchWindow.Owner = this;
                searchWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                searchWindow.UserSelected += (s, user) =>
                {
                    _ = OpenChatAsync(user.UserId, user.DisplayName);
                };

                searchWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия поиска: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


//клик на три точки 
private void ChatMenuButton_Click(object sender, RoutedEventArgs e)
{
    if (string.IsNullOrEmpty(_currentContactId))
        return;

    var contextMenu = new ContextMenu();
    
    var menuItem1 = new MenuItem { Header = "Информация о чате", FontSize = 14 };
    var menuItem2 = new MenuItem { Header = "Очистить историю", FontSize = 14 };

    var chatId = GetChatId(_userId, _currentContactId);
    var isMuted = _notificationManager?.IsChatMuted(chatId) ?? false;
    var muteItem = new MenuItem 
    { 
        Header = isMuted ? " Включить уведомления" : " Отключить уведомления", 
        FontSize = 14 
    };
    muteItem.Click += (s, args) =>
    {
        bool newMuteState = !isMuted;
        _notificationManager?.MuteChat(chatId, newMuteState);
        
        // Сохраняем настройки
        SaveNotificationSettings();
        
        ShowNotification(newMuteState ? 
            "Уведомления для этого чата отключены" : 
            "Уведомления для этого чата включены");
    };
    
    var menuItem3 = new MenuItem { Header = "Удалить чат", FontSize = 14 };
    var menuItem4 = new MenuItem { Header = "Заблокировать", FontSize = 14 };
    var menuItem5 = new MenuItem { Header = "Пожаловаться", FontSize = 14 };
    
    menuItem2.Click += async (s, args) =>
    {
        var result = MessageBox.Show("Вы уверены, что хотите очистить историю чата?", "Подтверждение", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            await _chatService.ClearChatHistoryAsync(_currentContactId);
            _messages.Clear();
        }
    };
    
    menuItem3.Click += async (s, args) =>
    {
        var result = MessageBox.Show("Вы уверены, что хотите удалить чат?", "Подтверждение", 
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            await _chatService.DeleteChatAsync(_currentContactId);
            BackButton_Click(sender, e);
            
            var contact = _contacts.FirstOrDefault(c => c.UserId == _currentContactId);
            if (contact != null)
            {
                _contacts.Remove(contact);
                _allContacts.Remove(contact);
            }
        }
    };
    
    contextMenu.Items.Add(menuItem1);
    contextMenu.Items.Add(muteItem);
    contextMenu.Items.Add(new Separator());
    contextMenu.Items.Add(menuItem2);
    contextMenu.Items.Add(menuItem3);
    contextMenu.Items.Add(new Separator());
    contextMenu.Items.Add(menuItem4);
    contextMenu.Items.Add(menuItem5);
    
    contextMenu.IsOpen = true;
}


        private void FilePreviewPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            FilePreviewPanel.Visibility = Visibility.Collapsed;
            _fileToSend = null;
        }
//обработчик кнопки "назад" 
        private void CancelFileButton_Click(object sender, RoutedEventArgs e)
        {
            FilePreviewPanel.Visibility = Visibility.Collapsed;
            _fileToSend = null;
        }
//обработчик отправки файла 
        private async void SendFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fileToSend != null && !string.IsNullOrEmpty(_currentContactId))
            {
                try
                {
                    var fileBytes = await File.ReadAllBytesAsync(_fileToSend.FullName);
                    var base64String = Convert.ToBase64String(fileBytes);

                    var message = new Message
                    {
                        SenderId = _userId,
                        ReceiverId = _currentContactId,
                        Text = $"[Файл: {_fileToSend.Name}]",
                        FileName = _fileToSend.Name,
                        FileData = base64String,
                        FileSize = _fileToSend.Length,
                        Timestamp = DateTime.UtcNow,
                        IsMyMessage = true,
                        Status = MessageStatus.Sending,
                        HasAttachment = true
                    };

                    AddDateSeparatorIfNeeded(message.Timestamp);
                    
                    _messages.Add(message);

                    FilePreviewPanel.Visibility = Visibility.Collapsed;
                    
                    ScrollToBottom();

                    var messageId = await _chatService.SendFileMessageAsync(_currentContactId, 
                        _fileToSend.Name, base64String);

                    if (!string.IsNullOrEmpty(messageId))
                    {
                        message.MessageId = messageId;
                        message.Status = MessageStatus.Sent;
                        UpdateContactLastMessage(_currentContactId, message.Text, message.Timestamp);
                    }
                    else
                    {
                        message.Status = MessageStatus.Error;
                    }

                    _fileToSend = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка отправки файла: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
//конвертеры для xaml
    public class DateTimeToStringConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                var now = DateTime.Now;
                var localTime = dateTime.ToLocalTime();
                
                if (localTime.Date == now.Date)
                {
                    return localTime.ToString("HH:mm");
                }
                else if (localTime.Date == now.Date.AddDays(-1))
                {
                    return "вчера";
                }
                else if (localTime.Date > now.Date.AddDays(-7))
                {
                    var dayName = localTime.ToString("dddd");
                    return dayName.Substring(0, 3);
                }
                else
                {
                    return localTime.ToString("dd.MM.yy");
                }
            }
            return string.Empty;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MessageStyleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? "MyMessageStyle" : "TheirMessageStyle";
            }
            return "TheirMessageStyle";
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IntToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MessageTextColorConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? Brushes.White : Brushes.Black;
            }
            return Brushes.Black;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MessageTimeColorConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? Brushes.LightBlue : Brushes.Gray;
            }
            return Brushes.Gray;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MessageBackgroundConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? new SolidColorBrush(Color.FromRgb(0, 136, 204)) : Brushes.White;
            }
            return Brushes.White;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToItalicConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
                return FontStyles.Italic;
            return FontStyles.Normal;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FileIconBackgroundConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) 
                                   : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            }
            return new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DownloadButtonBorderConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? Brushes.White : Brushes.Gray;
            }
            return Brushes.Gray;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DownloadButtonTextConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? Brushes.White : Brushes.Black;
            }
            return Brushes.Black;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FileIconConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string fileName)
            {
                if (string.IsNullOrEmpty(fileName)) return "📎";
                
                var extension = System.IO.Path.GetExtension(fileName).ToLower();
                return extension switch
                {
                    ".pdf" => "📕",
                    ".doc" or ".docx" => "📝",
                    ".xls" or ".xlsx" => "📊",
                    ".ppt" or ".pptx" => "📽️",
                    ".txt" => "📄",
                    ".zip" or ".rar" or ".7z" => "📦",
                    ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "🖼️",
                    ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" => "🎬",
                    ".mp3" or ".wav" or ".ogg" or ".flac" => "🎵",
                    _ => "📎"
                };
            }
            return "📎";
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FileSizeConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is long fileSize)
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                int order = 0;
                double len = fileSize;

                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }

                return $"{len:0.##} {sizes[order]}";
            }
            return "";
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FileBackgroundConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isMyMessage)
            {
                return isMyMessage ? 
                    new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) : 
                    new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            }
            return new SolidColorBrush(Colors.Transparent);
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
