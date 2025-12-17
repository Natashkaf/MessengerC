using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

namespace MessengerApp
{
    public class ChatService : IDisposable
    {
        private readonly string _userId;
        private readonly string _idToken;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, IDisposable> _subscriptions;
        private CancellationTokenSource _cts;
        private bool _disposed = false;
        
        private const string FirebaseBaseUrl = "https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app";
        
        // События для обновления UI в реальном времени
        public event EventHandler<MessageEventArgs> NewMessageReceived;
        public event EventHandler<MessageStatusEventArgs> MessageStatusUpdated;
        public event EventHandler<TypingIndicatorEventArgs> TypingIndicatorReceived;
        public event EventHandler<ContactStatusEventArgs> ContactStatusChanged;
        
        public ChatService(string userId, string idToken)
        {
            _userId = userId;
            _idToken = idToken;
            _httpClient = new HttpClient();
            _subscriptions = new Dictionary<string, IDisposable>();
            _cts = new CancellationTokenSource();
        }
        public async Task<bool> MarkMessageAsReadAsync(string messageId)
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/api/messages/{messageId}/read";
        
                var content = new StringContent("", Encoding.UTF8, "application/json");
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _idToken);
        
                var response = await _httpClient.PutAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        
        public async Task StartRealtimeListeners()
        {
            try
            {

                await ListenForNewMessages();

                await ListenForMessageStatusUpdates();

                await ListenForTypingIndicators();

                await ListenForContactStatusChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"О{ex.Message}");
            }
        }
        
        private async Task ListenForNewMessages()
        {
            try
            {
                // Получаем все чаты пользователя
                var chats = await GetUserChatsAsync();
                
                foreach (var chat in chats)
                {
                    await StartMessageListenerForChat(chat.ChatId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
        
        private async Task StartMessageListenerForChat(string chatId)
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/messages/{chatId}.json?auth={_idToken}&watch=true";
                
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
                
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                
                var stream = await response.Content.ReadAsStreamAsync();
                
                // Асинхронно читаем поток
                var subscription = Task.Run(async () =>
                {
                    using var reader = new System.IO.StreamReader(stream);
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) continue;
                        
                        if (line.StartsWith("data: "))
                        {
                            var data = line.Substring(6);
                            if (data != "null")
                            {
                                await ProcessMessageUpdate(chatId, data);
                            }
                        }
                    }
                });
                
                // Сохраняем подписку
                _subscriptions[$"messages_{chatId}"] = new DisposableAction(() => 
                {
                    _cts?.Cancel();
                    response?.Dispose();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {ex.Message}");
            }
        }
        
        private async Task ProcessMessageUpdate(string chatId, string jsonData)
        {
            try
            {
                var data = JsonDocument.Parse(jsonData).RootElement;
                
                if (data.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in data.EnumerateObject())
                    {
                        var message = JsonSerializer.Deserialize<Message>(property.Value.GetRawText());
                        if (message != null && message.SenderId != _userId)
                        {
                            NewMessageReceived?.Invoke(this, new MessageEventArgs
                            {
                                Message = message
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        private async Task ListenForMessageStatusUpdates()
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/messageStatus/{_userId}.json?auth={_idToken}&watch=true";
                await StartRealtimeListener(url, "status", (json) =>
                {
                    var statusUpdate = JsonSerializer.Deserialize<MessageStatusUpdate>(json);
                    if (statusUpdate != null)
                    {
                        MessageStatusUpdated?.Invoke(this, new MessageStatusEventArgs
                        {
                            MessageId = statusUpdate.MessageId,
                            Status = statusUpdate.Status
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
        
        private async Task ListenForTypingIndicators()
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/typingIndicators/{_userId}.json?auth={_idToken}&watch=true";
                await StartRealtimeListener(url, "typing", (json) =>
                {
                    var typingIndicator = JsonSerializer.Deserialize<TypingIndicator>(json);
                    if (typingIndicator != null)
                    {
                        TypingIndicatorReceived?.Invoke(this, new TypingIndicatorEventArgs
                        {
                            UserId = typingIndicator.UserId,
                            IsTyping = typingIndicator.IsTyping
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
        
        private async Task ListenForContactStatusChanges()
        {
            try
            {
                var contacts = await GetAllContactsAsync();
                
                foreach (var contact in contacts)
                {
                    var url = $"{FirebaseBaseUrl}/presence/{contact.UserId}.json?watch=true";
                    await StartRealtimeListener(url, $"presence_{contact.UserId}", (json) =>
                    {
                        var presence = JsonSerializer.Deserialize<PresenceStatus>(json);
                        if (presence != null)
                        {
                            ContactStatusChanged?.Invoke(this, new ContactStatusEventArgs
                            {
                                UserId = contact.UserId,
                                Status = presence.Status,
                                StatusText = presence.StatusText
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        private async Task StartRealtimeListener(string url, string subscriptionKey, Action<string> dataHandler)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
                
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                
                var stream = await response.Content.ReadAsStreamAsync();
                
                var subscription = Task.Run(async () =>
                {
                    using var reader = new System.IO.StreamReader(stream);
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) continue;
                        
                        if (line.StartsWith("data: "))
                        {
                            var data = line.Substring(6);
                            if (data != "null")
                            {
                                dataHandler(data);
                            }
                        }
                    }
                });
                
                _subscriptions[subscriptionKey] = new DisposableAction(() => 
                {
                    response?.Dispose();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        public async Task<List<Message>> GetAllMessagesAsync()
{
    try
    {
        var url = $"{FirebaseBaseUrl}/messages/all.json";
        var response = await _httpClient.GetAsync(url);
        
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            if (json != "null")
            {
                var messages = new List<Message>();
                
                using var doc = JsonDocument.Parse(json);
                foreach (var element in doc.RootElement.EnumerateObject())
                {
                    var messageJson = element.Value.GetRawText();
                    var message = JsonSerializer.Deserialize<Message>(messageJson);
                    if (message != null)
                    {
                        messages.Add(message);
                    }
                }
                
                return messages;
            }
        }
        
        return new List<Message>();
    }
    catch
    {
        return new List<Message>();
    }
}

// Получить непрочитанные сообщения
public async Task<List<Message>> GetUnreadMessagesAsync()
{
    try
    {
        var url = $"{FirebaseBaseUrl}/messages/all.json?orderBy=\"receiverId\"&equalTo=\"{_userId}\"";
        var response = await _httpClient.GetAsync(url);
        
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            if (json != "null")
            {
                var messages = new List<Message>();
                
                using var doc = JsonDocument.Parse(json);
                foreach (var element in doc.RootElement.EnumerateObject())
                {
                    var messageJson = element.Value.GetRawText();
                    var message = JsonSerializer.Deserialize<Message>(messageJson);
                    
                    if (message != null && !message.IsRead && message.ReceiverId == _userId)
                    {
                        messages.Add(message);
                    }
                }
                
                return messages;
            }
        }
        
        return new List<Message>();
    }
    catch
    {
        return new List<Message>();
    }
}

        
        // Получить или создать ID чата
        public async Task<string> GetOrCreateChatIdAsync(string otherUserId)
        {
            return await GetOrCreateChatAsync(otherUserId);
        }
        
        // Получить или создать диалог
        public async Task<string> GetOrCreateChatAsync(string otherUserId)
        {
            try
            {
                var chatId = GenerateChatId(_userId, otherUserId);
                
                var chatUrl = $"{FirebaseBaseUrl}/chats/{chatId}.json?auth={_idToken}";
                var response = await _httpClient.GetAsync(chatUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (content != "null")
                    {
                        return chatId;
                    }
                }
                
                // Создаем новый чат
                var chatData = new 
                {
                    chatId = chatId,
                    participant1Id = _userId,
                    participant2Id = otherUserId,
                    created = DateTime.UtcNow.ToString("o"),
                    lastMessage = "",
                    lastMessageTime = DateTime.UtcNow.ToString("o")
                };
                
                var json = JsonSerializer.Serialize(chatData);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                
                var putResponse = await _httpClient.PutAsync(chatUrl, httpContent);
                
                return putResponse.IsSuccessStatusCode ? chatId : null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        
        // Отправить сообщение
        public async Task<string> SendMessageAsync(string receiverId, string text)
        {
            try
            {
                var chatId = GenerateChatId(_userId, receiverId);
                var messageId = Guid.NewGuid().ToString();
                var messageUrl = $"{FirebaseBaseUrl}/messages/{chatId}/{messageId}.json?auth={_idToken}";
                
                var message = new Message
                {
                    MessageId = messageId,
                    SenderId = _userId,
                    ReceiverId = receiverId,
                    ChatId = chatId,
                    Text = text,
                    Timestamp = DateTime.UtcNow,
                    Status = MessageStatus.Sent
                };
                
                var json = JsonSerializer.Serialize(message, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync(messageUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    // Обновляем последнее сообщение в чате
                    await UpdateLastMessageAsync(chatId, text);
                    
                    // Отправляем уведомление о новом сообщении
                    await SendMessageNotification(receiverId, messageId, text);
                    
                    return messageId;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        
        // Отправить сообщение с файлом
        public async Task<string> SendFileMessageAsync(string receiverId, string fileName, string fileData)
        {
            try
            {
                var chatId = GenerateChatId(_userId, receiverId);
                var messageId = Guid.NewGuid().ToString();
                var messageUrl = $"{FirebaseBaseUrl}/messages/{chatId}/{messageId}.json?auth={_idToken}";
                
                var message = new Message
                {
                    MessageId = messageId,
                    SenderId = _userId,
                    ReceiverId = receiverId,
                    ChatId = chatId,
                    Text = $"[Файл: {fileName}]",
                    FileName = fileName,
                    FileData = fileData,
                    FileSize = fileData.Length * 3 / 4, 
                    HasAttachment = true,
                    Timestamp = DateTime.UtcNow,
                    Status = MessageStatus.Sent
                };
                
                var json = JsonSerializer.Serialize(message, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync(messageUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    await UpdateLastMessageAsync(chatId, $"[Файл: {fileName}]");
                    await SendMessageNotification(receiverId, messageId, $"[Файл: {fileName}]");
                    return messageId;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        
        // Получить историю сообщений
        public async Task<List<Message>> GetMessagesAsync(string otherUserId, int limit = 50)
        {
            try
            {
                var chatId = GenerateChatId(_userId, otherUserId);
                var messagesUrl = $"{FirebaseBaseUrl}/messages/{chatId}.json?orderBy=\"timestamp\"&limitToLast={limit}";
                var response = await _httpClient.GetAsync(messagesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        var messages = new List<Message>();
                        
                        using var doc = JsonDocument.Parse(json);
                        foreach (var element in doc.RootElement.EnumerateObject())
                        {
                            var messageJson = element.Value.GetRawText();
                            var message = JsonSerializer.Deserialize<Message>(messageJson);
                            
                            if (message != null && !message.IsDeleted)
                            {
                                // Определяем, мое ли это сообщение
                                message.IsMyMessage = message.SenderId == _userId;
                                messages.Add(message);
                            }
                        }
                        
                        return messages.OrderBy(m => m.Timestamp).ToList();
                    }
                }
                
                return new List<Message>();
            }
            catch (Exception ex)
            {
                return new List<Message>();
            }
        }
        
        // Загрузить больше сообщений
        public async Task<List<Message>> GetMoreMessagesAsync(string otherUserId, DateTime beforeTime, int limit = 20)
        {
            try
            {
                var chatId = GenerateChatId(_userId, otherUserId);
                var timestamp = ((DateTimeOffset)beforeTime).ToUnixTimeSeconds();
                var messagesUrl = $"{FirebaseBaseUrl}/messages/{chatId}.json?orderBy=\"timestamp\"&endAt={timestamp}&limitToLast={limit + 1}";
                
                var response = await _httpClient.GetAsync(messagesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        var messages = new List<Message>();
                        
                        using var doc = JsonDocument.Parse(json);
                        foreach (var element in doc.RootElement.EnumerateObject())
                        {
                            var messageJson = element.Value.GetRawText();
                            var message = JsonSerializer.Deserialize<Message>(messageJson);
                            
                            if (message != null && !message.IsDeleted && message.Timestamp < beforeTime)
                            {
                                message.IsMyMessage = message.SenderId == _userId;
                                messages.Add(message);
                            }
                        }
                        
                        return messages.OrderBy(m => m.Timestamp).ToList();
                    }
                }
                
                return new List<Message>();
            }
            catch (Exception ex)
            {
                return new List<Message>();
            }
        }
        
        // Получить последнее сообщение
        public async Task<Message> GetLastMessageAsync(string otherUserId)
        {
            try
            {
                var messages = await GetMessagesAsync(otherUserId, 1);
                return messages.LastOrDefault();
            }
            catch
            {
                return null;
            }
        }
        
        public async Task<List<Contact>> GetContactsAsync()
        {
            try
            {
                var contacts = new List<Contact>();
                
                // Получаем всех пользователей (кроме текущего)
                var profilesUrl = $"{FirebaseBaseUrl}/profiles.json";
                var response = await _httpClient.GetAsync(profilesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        using var doc = JsonDocument.Parse(json);
                        foreach (var element in doc.RootElement.EnumerateObject())
                        {
                            var userId = element.Name;
                            
                            // Пропускаем текущего пользователя
                            if (userId == _userId)
                                continue;
                            
                            var profileJson = element.Value.GetRawText();
                            var profile = JsonSerializer.Deserialize<UserProfile>(profileJson);
                            
                            if (profile != null)
                            {
                                var contact = new Contact
                                {
                                    UserId = userId,
                                    Name = profile.DisplayName ?? "Неизвестный",
                                    Initials = GetInitials(profile.DisplayName ?? "??"),
                                    Status = "offline",
                                    StatusText = "не в сети"
                                };
                                
                                // Загружаем последнее сообщение
                                var lastMessage = await GetLastMessageAsync(userId);
                                if (lastMessage != null)
                                {
                                    contact.LastMessage = lastMessage.Text;
                                    contact.Time = FormatTime(lastMessage.Timestamp);
                                    
                                    // Считаем непрочитанные сообщения
                                    if (lastMessage.SenderId == userId && !lastMessage.IsRead)
                                    {
                                        contact.UnreadCount = await GetUnreadCountAsync(userId);
                                    }
                                }
                                else
                                {
                                    contact.LastMessage = "Нет сообщений";
                                    contact.Time = "";
                                }
                                
                                // Загружаем статус
                                var status = await GetUserStatusAsync(userId);
                                if (status != null)
                                {
                                    contact.Status = status.Status ?? "offline";
                                    contact.StatusText = status.StatusText ?? "не в сети";
                                }
                                
                                contacts.Add(contact);
                            }
                        }
                    }
                }
                
                return contacts.OrderByDescending(c => 
                    _contactsLastMessageTime.GetValueOrDefault(c.UserId, DateTime.MinValue))
                    .ToList();
            }
            catch (Exception ex)
            {
                return new List<Contact>();
            }
        }
        
        // Получить всех контактов (упрощенная версия для подписки на статусы)
        private async Task<List<Contact>> GetAllContactsAsync()
        {
            try
            {
                var contacts = new List<Contact>();
                var profilesUrl = $"{FirebaseBaseUrl}/profiles.json";
                var response = await _httpClient.GetAsync(profilesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        using var doc = JsonDocument.Parse(json);
                        foreach (var element in doc.RootElement.EnumerateObject())
                        {
                            var userId = element.Name;
                            if (userId == _userId) continue;
                            
                            contacts.Add(new Contact { 
                                UserId = userId,
                                Name = "Пользователь"
                            });
                        }
                    }
                }
                
                return contacts;
            }
            catch
            {
                return new List<Contact>();
            }
        }
        
        // Получить количество непрочитанных сообщений
        private async Task<int> GetUnreadCountAsync(string senderId)
        {
            try
            {
                var chatId = GenerateChatId(_userId, senderId);
                var messagesUrl = $"{FirebaseBaseUrl}/messages/{chatId}.json?orderBy=\"senderId\"&equalTo=\"{senderId}\"";
                var response = await _httpClient.GetAsync(messagesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        int unreadCount = 0;
                        
                        using var doc = JsonDocument.Parse(json);
                        foreach (var element in doc.RootElement.EnumerateObject())
                        {
                            var messageJson = element.Value.GetRawText();
                            var message = JsonSerializer.Deserialize<Message>(messageJson);
                            
                            if (message != null && !message.IsRead)
                            {
                                unreadCount++;
                            }
                        }
                        
                        return unreadCount;
                    }
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        // Кэш для времени последних сообщений
        private Dictionary<string, DateTime> _contactsLastMessageTime = new Dictionary<string, DateTime>();
        
        
        // Отправить индикатор набора текста
        public async Task SendTypingIndicatorAsync(string receiverId, bool isTyping)
        {
            try
            {
                var typingUrl = $"{FirebaseBaseUrl}/typingIndicators/{receiverId}/{_userId}.json?auth={_idToken}";
                
                var typingIndicator = new TypingIndicator
                {
                    UserId = _userId,
                    IsTyping = isTyping,
                    Timestamp = DateTime.UtcNow
                };
                
                var json = JsonSerializer.Serialize(typingIndicator);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                if (isTyping)
                {
                    await _httpClient.PutAsync(typingUrl, content);
                }
                else
                {
                    await _httpClient.DeleteAsync(typingUrl);
                }
            }
            catch (Exception ex)
            {
            }
        }
        
        // Получить статус пользователя
        public async Task<PresenceStatus> GetUserStatusAsync(string userId)
        {
            try
            {
                var statusUrl = $"{FirebaseBaseUrl}/presence/{userId}.json";
                var response = await _httpClient.GetAsync(statusUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        return JsonSerializer.Deserialize<PresenceStatus>(json);
                    }
                }
                
                return new PresenceStatus { 
                    Status = "offline", 
                    StatusText = "не в сети",
                    LastSeen = DateTime.UtcNow
                };
            }
            catch
            {
                return new PresenceStatus { 
                    Status = "offline", 
                    StatusText = "не в сети",
                    LastSeen = DateTime.UtcNow
                };
            }
        }
        
        // Пометить сообщения как прочитанные
        public async Task MarkMessagesAsReadAsync(string senderId)
        {
            try
            {
                var chatId = GenerateChatId(_userId, senderId);
                var messagesUrl = $"{FirebaseBaseUrl}/messages/{chatId}.json?orderBy=\"senderId\"&equalTo=\"{senderId}\"";
                var response = await _httpClient.GetAsync(messagesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        using var doc = JsonDocument.Parse(json);
                        foreach (var element in doc.RootElement.EnumerateObject())
                        {
                            var messageId = element.Name;
                            var messageUrl = $"{FirebaseBaseUrl}/messages/{chatId}/{messageId}/isRead.json?auth={_idToken}";
                            
                            var readContent = new StringContent("true", Encoding.UTF8, "application/json");
                            await _httpClient.PutAsync(messageUrl, readContent);
                            
                            // Обновляем статус сообщения
                            var statusUrl = $"{FirebaseBaseUrl}/messages/{chatId}/{messageId}/status.json?auth={_idToken}";
                            var statusContent = new StringContent("\"Read\"", Encoding.UTF8, "application/json");
                            await _httpClient.PutAsync(statusUrl, statusContent);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        // Отправить уведомление о новом сообщении
        private async Task SendMessageNotification(string receiverId, string messageId, string text)
        {
            try
            {
                var notificationUrl = $"{FirebaseBaseUrl}/notifications/{receiverId}/{messageId}.json?auth={_idToken}";
                
                var notification = new
                {
                    fromUserId = _userId,
                    messageId = messageId,
                    text = text,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    isRead = false
                };
                
                var json = JsonSerializer.Serialize(notification);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                await _httpClient.PutAsync(notificationUrl, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }

        
        // Очистить историю чата
        public async Task ClearChatHistoryAsync(string otherUserId)
        {
            try
            {
                var chatId = GenerateChatId(_userId, otherUserId);
                var messagesUrl = $"{FirebaseBaseUrl}/messages/{chatId}.json?auth={_idToken}";
                await _httpClient.DeleteAsync(messagesUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
        
        // Удалить чат
        public async Task DeleteChatAsync(string otherUserId)
        {
            try
            {
                var chatId = GenerateChatId(_userId, otherUserId);
                
                // Удаляем чат из списка
                var chatUrl = $"{FirebaseBaseUrl}/chats/{chatId}.json?auth={_idToken}";
                await _httpClient.DeleteAsync(chatUrl);
                
                // Удаляем все сообщения
                await ClearChatHistoryAsync(otherUserId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        
        
        // Получить список чатов пользователя
        public async Task<List<Chat>> GetUserChatsAsync()
        {
            try
            {
                var chats = new List<Chat>();
                
                // Получаем все чаты где пользователь участник (как participant1)
                var chatsUrl1 = $"{FirebaseBaseUrl}/chats.json?orderBy=\"participant1Id\"&equalTo=\"{_userId}\"";
                var response1 = await _httpClient.GetAsync(chatsUrl1);
                
                if (response1.IsSuccessStatusCode)
                {
                    var json = await response1.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        await ParseChatsFromJson(json, chats);
                    }
                }
                
                // Также проверяем где пользователь participant2
                var chatsUrl2 = $"{FirebaseBaseUrl}/chats.json?orderBy=\"participant2Id\"&equalTo=\"{_userId}\"";
                var response2 = await _httpClient.GetAsync(chatsUrl2);
                
                if (response2.IsSuccessStatusCode)
                {
                    var json = await response2.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        await ParseChatsFromJson(json, chats);
                    }
                }
                
                // Удаляем дубликаты
                chats = chats
                    .GroupBy(c => c.ChatId)
                    .Select(g => g.First())
                    .ToList();
                
                // Загружаем информацию о собеседниках
                await LoadChatParticipantsInfo(chats);
                
                return chats.OrderByDescending(c => c.LastMessageTime).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return new List<Chat>();
            }
        }
        
        // Поиск пользователей
        public async Task<List<UserProfile>> SearchUsersAsync(string query)
        {
            try
            {
                var users = new List<UserProfile>();
                
                if (string.IsNullOrWhiteSpace(query))
                    return users;
                
                var queryLower = query.ToLowerInvariant();
                var profilesUrl = $"{FirebaseBaseUrl}/profiles.json";
                var response = await _httpClient.GetAsync(profilesUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null")
                    {
                        using var doc = JsonDocument.Parse(json);
                        foreach (var element in doc.RootElement.EnumerateObject())
                        {
                            var userId = element.Name;
                            
                            // Пропускаем текущего пользователя
                            if (userId == _userId)
                                continue;
                            
                            var profileJson = element.Value.GetRawText();
                            var profile = JsonSerializer.Deserialize<UserProfile>(profileJson);
                            
                            if (profile != null)
                            {
                                // Проверяем совпадение по имени
                                if (!string.IsNullOrEmpty(profile.DisplayName) && 
                                    profile.DisplayName.ToLowerInvariant().Contains(queryLower))
                                {
                                    profile.UserId = userId;
                                    users.Add(profile);
                                }
                            }
                        }
                    }
                }
                
                return users;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return new List<UserProfile>();
            }
        }
        
        private async Task ParseChatsFromJson(string json, List<Chat> chats)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var element in doc.RootElement.EnumerateObject())
                {
                    var chatJson = element.Value.GetRawText();
                    var chat = JsonSerializer.Deserialize<Chat>(chatJson);
                    
                    if (chat != null)
                    {
                        chat.ChatId = element.Name;
                        
                        if (chat.Participant1Id == _userId || chat.Participant2Id == _userId)
                        {
                            if (!chats.Any(c => c.ChatId == chat.ChatId))
                            {
                                chats.Add(chat);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        private async Task LoadChatParticipantsInfo(List<Chat> chats)
        {
            foreach (var chat in chats)
            {
                try
                {
                    var otherUserId = chat.Participant1Id == _userId 
                        ? chat.Participant2Id 
                        : chat.Participant1Id;
                    
                    if (string.IsNullOrEmpty(otherUserId))
                        continue;
                    
                    // Загружаем профиль собеседника
                    var profileUrl = $"{FirebaseBaseUrl}/profiles/{otherUserId}.json";
                    var response = await _httpClient.GetAsync(profileUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        if (json != "null")
                        {
                            var profile = JsonSerializer.Deserialize<UserProfile>(json);
                            if (profile != null)
                            {
                                chat.OtherUserName = profile.DisplayName;
                                chat.OtherUserAvatar = profile.AvatarBase64;
                            }
                        }
                    }
                    
                    // Загружаем статус
                    var status = await GetUserStatusAsync(otherUserId);
                    if (status != null)
                    {
                        chat.OtherUserStatus = status.Status;
                        chat.OtherUserStatusText = status.StatusText;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" {ex.Message}");
                    chat.OtherUserName = "Неизвестный";
                }
            }
        }
        
        private string GenerateChatId(string userId1, string userId2)
        {
            var ids = new[] { userId1, userId2 }.OrderBy(id => id).ToArray();
            return $"{ids[0]}_{ids[1]}";
        }
        
        private async Task UpdateLastMessageAsync(string chatId, string lastMessage)
        {
            try
            {
                var chatUrl = $"{FirebaseBaseUrl}/chats/{chatId}.json?auth={_idToken}";
                
                var updateData = new 
                {
                    lastMessage = lastMessage,
                    lastMessageTime = DateTime.UtcNow.ToString("o")
                };
                
                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                await _httpClient.PatchAsync(chatUrl, content);
                
                // Обновляем кэш времени последнего сообщения
                var otherUserId = chatId.Replace(_userId + "_", "").Replace("_" + _userId, "");
                _contactsLastMessageTime[otherUserId] = DateTime.UtcNow;
            }
            catch { }
        }
        
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
                return dayName.Length > 3 ? dayName.Substring(0, 3) : dayName;
            }
            else
            {
                return time.ToString("dd.MM.yy");
            }
        }
        
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _httpClient?.Dispose();
                    
                    // Отписываемся от всех подписок
                    foreach (var subscription in _subscriptions.Values)
                    {
                        subscription?.Dispose();
                    }
                    _subscriptions.Clear();
                }
        
                _disposed = true;
            }
        }
        
        private class DisposableAction : IDisposable
        {
            private readonly Action _disposeAction;
            
            public DisposableAction(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }
            
            public void Dispose()
            {
                _disposeAction?.Invoke();
            }
        }
    }
}