using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MessengerApp
{
    public class FirebaseRealtimeListener : IDisposable
    {
        private readonly string _userId;
        private readonly string _idToken;
        private readonly HttpClient _httpClient;
        private DispatcherTimer _pollingTimer;
        private bool _isDisposed = false;
        private bool _isListening = false;
        private bool _isCheckingMessages = false;
        private object _checkLock = new object();
        
        // Кэши для отслеживания изменений
        private Dictionary<string, Message> _lastMessagesCache = new Dictionary<string, Message>();
        private Dictionary<string, PresenceStatus> _lastStatusCache = new Dictionary<string, PresenceStatus>();
        private List<string> _trackedChats = new List<string>(); // Список отслеживаемых чатов
        
        private const string FirebaseBaseUrl = "https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app";
        
        public event EventHandler<Message> NewMessageReceived;
        public event EventHandler<UserStatusEventArgs> UserStatusChanged;
        public event EventHandler<Exception> ErrorOccurred;
        
        public FirebaseRealtimeListener(string userId, string idToken)
        {
            _userId = userId;
            _idToken = idToken;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(40);
        }
        
        public async Task StartListening(int pollingIntervalSeconds = 1)
        {
            if (_isListening) return;
            
            try
            {
                _isListening = true;
                
                // Создаем таймер для быстрого опроса
                _pollingTimer = new DispatcherTimer();
                _pollingTimer.Interval = TimeSpan.FromSeconds(pollingIntervalSeconds);
                _pollingTimer.Tick += async (s, e) => await PollForUpdatesAsync();
                _pollingTimer.Start();
                
            }
            catch (Exception ex)
            {
                _isListening = false;
                ErrorOccurred?.Invoke(this, ex);

            }
        }
        
        private async Task PollForUpdatesAsync()
        {
            try
            {
                // Если есть отслеживаемые чаты, проверяем их
                if (_trackedChats.Count > 0)
                {
                    await CheckAllTrackedChatsAsync();
                }
                
                // Всегда проверяем статусы
                await CheckForStatusChangesAsync();
            }
            catch (Exception ex)
            {
            }
        }
        
        private async Task CheckAllTrackedChatsAsync()
        {
            if (_isCheckingMessages) return;
            
            lock (_checkLock)
            {
                if (_isCheckingMessages) return;
                _isCheckingMessages = true;
            }
            
            try
            {
                // Проверяем каждый активный чат пользователя
                foreach (var chatId in _trackedChats.ToList()) // ToList для копирования
                {
                    await CheckChatForNewMessagesAsync(chatId);
                }
            }
            catch (Exception ex)
            {
            }
            finally
            {
                lock (_checkLock)
                {
                    _isCheckingMessages = false;
                }
            }
        }
        
        private async Task CheckChatForNewMessagesAsync(string chatId)
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/chats/{chatId}/messages.json?auth={_idToken}";
                
                Console.WriteLine($"🔍 Проверяю чат {chatId}");
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (!string.IsNullOrWhiteSpace(json) && json != "null")
                    {
                        var messages = JsonSerializer.Deserialize<Dictionary<string, FirebaseMessage>>(json);
                        
                        if (messages != null && messages.Count > 0)
                        {
                            
                            // Сортируем по времени 
                            var sortedMessages = messages
                                .OrderBy(m => 
                                {
                                    if (m.Value.timestamp is string timestampStr)
                                        return timestampStr;
                                    return m.Value.timestamp?.ToString() ?? "";
                                })
                                .ToList();
                            
                            foreach (var msg in sortedMessages)
                            {
                                // Проверяем, новое ли это сообщение
                                if (!_lastMessagesCache.ContainsKey(msg.Key))
                                {
                                    var message = ConvertToMessage(msg.Value, msg.Key);
                                    if (message != null)
                                    {
                                        // Добавляем в кэш
                                        _lastMessagesCache[msg.Key] = message;
                                        
                                        // Определяем тип сообщения
                                        bool isIncoming = message.ReceiverId == _userId && message.SenderId != _userId;
                                        bool isOutgoing = message.SenderId == _userId && message.ReceiverId != _userId;
                                        
                                        // ВАЖНО: Извлекаем участников из chatId для проверки
                                        var participants = ExtractParticipantsFromChatId(chatId);
                                        bool isForThisChat = participants.Contains(message.SenderId) && 
                                                            participants.Contains(message.ReceiverId);
                                        
                                        if (isForThisChat)
                                        {
                                            
                                            // Уведомляем UI о новом сообщении
                                            NewMessageReceived?.Invoke(this, message);
                                        }
                                    }
                                }
                            }
                        }

                    }
                }
                else
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        var altUrl = $"{FirebaseBaseUrl}/chats/{chatId}/messages.json";
                        
                        var altResponse = await _httpClient.GetAsync(altUrl);
                        if (altResponse.IsSuccessStatusCode)
                        {
                            var altJson = await altResponse.Content.ReadAsStringAsync();
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                
            }
        }
        
        private List<string> ExtractParticipantsFromChatId(string chatId)
        {
            var participants = new List<string>();
            
            try
            {
               
                var parts = chatId.Split('_');
                
                if (parts.Length >= 2)
                {
                   
                    for (int i = 0; i < Math.Min(2, parts.Length); i++)
                    {
                        if (!string.IsNullOrEmpty(parts[i]))
                        {
                            participants.Add(parts[i]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
               
            }
            
            return participants;
        }
        
        private Message ConvertToMessage(FirebaseMessage firebaseMessage, string messageId)
        {
            try
            {
                DateTime timestamp = DateTime.UtcNow;
                
                if (firebaseMessage.timestamp != null)
                {
                    string timestampStr = firebaseMessage.timestamp.ToString();
                    
                    if (DateTime.TryParse(timestampStr, out var parsedTime))
                    {
                        timestamp = parsedTime;
                    }
                    else if (long.TryParse(timestampStr, out var milliseconds))
                    {
                        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
                    }
                }
                
                // Конвертируем строковый статус из Firebase в MessageStatus
                MessageStatus status = MessageStatus.Sent;
                if (!string.IsNullOrEmpty(firebaseMessage.status))
                {
                    status = firebaseMessage.status.ToLower() switch
                    {
                        "sending" => MessageStatus.Sending,
                        "sent" => MessageStatus.Sent,
                        "delivered" => MessageStatus.Delivered,
                        "read" => MessageStatus.Read,
                        "error" => MessageStatus.Error,
                        "failed" => MessageStatus.Failed,
                        _ => MessageStatus.Sent
                    };
                }
                
                var message = new Message
                {
                    MessageId = messageId,
                    SenderId = firebaseMessage.senderId,
                    ReceiverId = firebaseMessage.receiverId,
                    Text = firebaseMessage.text,
                    Timestamp = timestamp,
                    IsMyMessage = firebaseMessage.senderId == _userId,
                    Status = status,
                    IsRead = firebaseMessage.isRead,
                    IsEdited = firebaseMessage.isEdited,
                    IsDeleted = firebaseMessage.isDeleted,
                    HasAttachment = firebaseMessage.hasAttachment,
                    FileName = firebaseMessage.fileName,
                    FileData = firebaseMessage.fileData,
                    FileSize = firebaseMessage.fileSize ?? 0
                };
                
                return message;
            }
            catch (Exception ex){
                return null;
            }
        }
        
        private async Task CheckForStatusChangesAsync()
        {
            try
            {
                var statusesUrl = $"{FirebaseBaseUrl}/presence.json";
                var response = await _httpClient.GetAsync(statusesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        var currentStatuses = JsonSerializer.Deserialize<Dictionary<string, PresenceStatus>>(json);
                        if (currentStatuses != null)
                        {
                            foreach (var status in currentStatuses)
                            {
                                if (status.Key == _userId) continue;
                                
                                // Проверяем, изменился ли статус
                                if (_lastStatusCache.TryGetValue(status.Key, out var oldStatus))
                                {
                                    if (oldStatus.status != status.Value.status || 
                                        oldStatus.statusText != status.Value.statusText)
                                    {
                                        _lastStatusCache[status.Key] = status.Value;
                                        
                                        UserStatusChanged?.Invoke(this, new UserStatusEventArgs
                                        {
                                            UserId = status.Key,
                                            Status = status.Value.status,
                                            StatusText = status.Value.statusText
                                        });
                                    }
                                }
                                else
                                {
                                    _lastStatusCache[status.Key] = status.Value;
                                    
                                    UserStatusChanged?.Invoke(this, new UserStatusEventArgs
                                    {
                                        UserId = status.Key,
                                        Status = status.Value.status,
                                        StatusText = status.Value.statusText
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
        
        // Метод для добавления чата в отслеживание
        public void AddChatToMonitor(string chatId)
        {
            if (!_trackedChats.Contains(chatId))
            {
                _trackedChats.Add(chatId);
                
                // Немедленно проверяем этот чат
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    await CheckChatForNewMessagesAsync(chatId);
                });
            }
        }
        
        // Метод для удаления чата из отслеживания
        public void RemoveChatFromMonitor(string chatId)
        {
            if (_trackedChats.Contains(chatId))
            {
                _trackedChats.Remove(chatId);
            }
        }
        
        // Метод для принудительной проверки чата
        public async Task ForceCheckChatAsync(string chatId)
        {
            await CheckChatForNewMessagesAsync(chatId);
        }
        
        public void StopListening()
        {
            try
            {
                _isListening = false;
                _pollingTimer?.Stop();
                _pollingTimer = null;
                
            }
            catch (Exception ex)
            {
                
            }
        }
        
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                StopListening();
                _httpClient?.Dispose();
                _lastMessagesCache.Clear();
                _lastStatusCache.Clear();
                _trackedChats.Clear();
            }
        }
    }
    
    // Вспомогательные классы
    public partial class FirebaseMessage
    {
        public string messageId { get; set; }
        public string senderId { get; set; }
        public string receiverId { get; set; }
        public string text { get; set; }
        public object timestamp { get; set; }
        public string status { get; set; } = "sent";
        public bool isRead { get; set; }
        public bool isEdited { get; set; }
        public bool isDeleted { get; set; }
        public string fileName { get; set; }
        public string fileData { get; set; }
        public long? fileSize { get; set; }
        public bool hasAttachment { get; set; }
    }
    
    public class UserStatusEventArgs : EventArgs
    {
        public string UserId { get; set; }
        public string Status { get; set; }
        public string StatusText { get; set; }
    }
}