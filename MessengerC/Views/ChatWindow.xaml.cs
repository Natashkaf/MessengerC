//окно чата 
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic;

namespace MessengerApp
{
    public partial class ChatWindow : Window
    {
        private readonly string _userId;
        private readonly string _idToken;
        private readonly string _otherUserId;
        private readonly string _otherUserName;
        
        private ChatService _chatService;
        private ObservableCollection<Message> _messages;
        private DispatcherTimer _typingTimer;
        private DateTime _lastTypingTime;
        
        public ChatWindow(string userId, string idToken, string otherUserId, string otherUserName)
        {
            InitializeComponent();
            
            _userId = userId;
            _idToken = idToken;
            _otherUserId = otherUserId;
            _otherUserName = otherUserName;
            
            _chatService = new ChatService(_userId, _idToken);
            _messages = new ObservableCollection<Message>();
            MessagesList.ItemsSource = _messages;
            
            Title = $"Чат с {otherUserName}";
            OtherUserNameText.Text = otherUserName;
            
            InitializeTypingTimer();
            
            // Подписка на события
            SubscribeToEvents();
            
            // Загружаем или создаем чат
            _ = InitializeChatAsync();
        }
        //создаем и настраиваем таймер 
        private void InitializeTypingTimer()
        {
            _typingTimer = new DispatcherTimer();
            _typingTimer.Interval = TimeSpan.FromSeconds(3);
            _typingTimer.Tick += TypingTimer_Tick;
        }
        // подписываемся на события 
        private void SubscribeToEvents()
        {
            _chatService.NewMessageReceived += ChatService_NewMessageReceived;
            _chatService.TypingIndicatorReceived += ChatService_TypingIndicatorReceived;
        }
        // обработчик получения нового сообщения 
        private void ChatService_NewMessageReceived(object sender, MessageEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var message = e.Message;
                    // проверка, что сообщение именно для этого чата 
                    if ((message.SenderId == _otherUserId && message.ReceiverId == _userId) ||
                        (message.SenderId == _userId && message.ReceiverId == _otherUserId))
                    {
                        message.IsMyMessage = message.SenderId == _userId;
                        _messages.Add(message);
                        // автоматический скролл к последнему сообщению
                        ScrollToBottom();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("{ex.Message}");
                }
            });
        }
        // получение индикатора набора текста 
        private void ChatService_TypingIndicatorReceived(object sender, TypingIndicatorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.UserId == _otherUserId)
                {
                    if (e.IsTyping)
                    {
                        TypingIndicator.Visibility = Visibility.Visible;
                        TypingText.Text = $"{_otherUserName} печатает...";
                    }
                    else
                    {
                        TypingIndicator.Visibility = Visibility.Collapsed;
                    }
                }
            });
        }
        // инициализирует чат 
        private async Task InitializeChatAsync()
        {
            try
            {
                
                var chatId = await _chatService.GetOrCreateChatIdAsync(_otherUserId);
                
                if (!string.IsNullOrEmpty(chatId))
                {
                    await LoadMessagesAsync();
                }
                else
                {
                    MessageBox.Show("Ошибка создания чата", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации чата: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        //загружает историю сообщений с сервера 
        private async Task LoadMessagesAsync()
        {
            try
            {
                var messages = await _chatService.GetMessagesAsync(_otherUserId);
                
                _messages.Clear();
                
                foreach (var message in messages.OrderBy(m => m.Timestamp))
                {
                    message.IsMyMessage = message.SenderId == _userId;
                    _messages.Add(message);
                }
                
                
                ScrollToBottom();
                
                await _chatService.MarkMessagesAsReadAsync(_otherUserId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки сообщений: {ex.Message}");
            }
        }
        // прокручивает историю сообщений в самый конец 
        private void ScrollToBottom()
        {
            if (MessagesScrollViewer != null)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    MessagesScrollViewer.ScrollToEnd();
                }, DispatcherPriority.Background);
            }
        }
        // обработчик нажатия кнопки "отправить" 
        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }
        // обработчик нажания на enter 
        private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }
        // отправляет текстовое сообщение 
        private async Task SendMessageAsync()
        {
            var text = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_otherUserId))
                return;
            
            try
            {
                
                var tempMessage = new Message
                {
                    SenderId = _userId,
                    ReceiverId = _otherUserId,
                    Text = text,
                    Timestamp = DateTime.UtcNow,
                    IsMyMessage = true,
                    Status = MessageStatus.Sending
                };
                
                _messages.Add(tempMessage);
                MessageTextBox.Clear();
                SendButton.IsEnabled = false;
                
                // Прокручиваем вниз
                ScrollToBottom();
                
                // Отправляем через сервис
                var messageId = await _chatService.SendMessageAsync(_otherUserId, text);
                
                // Обновляем статус сообщения
                if (!string.IsNullOrEmpty(messageId))
                {
                    tempMessage.MessageId = messageId;
                    tempMessage.Status = MessageStatus.Sent;
                }
                else
                {
                    tempMessage.Status = MessageStatus.Error;
                }
                
                // Отправляем индикатор окончания набора
                await _chatService.SendTypingIndicatorAsync(_otherUserId, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        //отслеживает ввод текста 
        private async void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Включаем кнопку отправки, если есть текст
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageTextBox.Text);
            
            // Отправляем индикатор набора текста
            if (!string.IsNullOrWhiteSpace(MessageTextBox.Text))
            {
                _lastTypingTime = DateTime.UtcNow;
                
                if (!_typingTimer.IsEnabled)
                {
                    await _chatService.SendTypingIndicatorAsync(_otherUserId, true);
                    _typingTimer.Start();
                }
            }
        }
        //отправляет индикатор, что пользователь перестал печатать 
        private async void TypingTimer_Tick(object sender, EventArgs e)
        {
            // Если прошло больше 3 секунд с последнего набора, отправляем "перестал печатать"
            if ((DateTime.UtcNow - _lastTypingTime).TotalSeconds >= 3)
            {
                _typingTimer.Stop();
                await _chatService.SendTypingIndicatorAsync(_otherUserId, false);
            }
        }
        
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        // обработчик нажатия на три точки 
        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var contextMenu = new ContextMenu();
            
            var menuItem1 = new MenuItem { Header = "Информация о чате", FontSize = 14 };
            var menuItem2 = new MenuItem { Header = "Очистить историю", FontSize = 14 };
            var menuItem3 = new MenuItem { Header = "Удалить чат", FontSize = 14 };
            
            menuItem2.Click += async (s, args) =>
            {
                var result = MessageBox.Show("Вы уверены, что хотите очистить историю чата?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    await _chatService.ClearChatHistoryAsync(_otherUserId);
                    _messages.Clear();
                }
            };
            
            menuItem3.Click += async (s, args) =>
            {
                var result = MessageBox.Show("Вы уверены, что хотите удалить чат?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    await _chatService.DeleteChatAsync(_otherUserId);
                    Close();
                }
            };
            
            contextMenu.Items.Add(menuItem1);
            contextMenu.Items.Add(menuItem2);
            contextMenu.Items.Add(menuItem3);
            
            contextMenu.IsOpen = true;
        }
        
        protected override void OnClosed(EventArgs e)
        {
            // Отписываемся от событий
            _chatService.NewMessageReceived -= ChatService_NewMessageReceived;
            _chatService.TypingIndicatorReceived -= ChatService_TypingIndicatorReceived;
            
            // Останавливаем таймер
            _typingTimer?.Stop();
            
            // Освобождаем ресурсы
            _chatService?.Dispose();
            
            base.OnClosed(e);
        }
    }
}