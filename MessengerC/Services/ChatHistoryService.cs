using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MessengerApp
{
    public class ChatHistoryService
    {
        private readonly string _userId;
        private readonly string _idToken;
        private const string FirebaseBaseUrl = "https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app";

        public ChatHistoryService(string userId, string idToken)
        {
            _userId = userId;
            _idToken = idToken;
        }

        // Сохраняем локальную историю на сервер
        public async Task SaveChatHistoryAsync(string chatId, List<Message> messages)
        {
            try
            {
                // Группируем сообщения по дате для оптимизации
                var historyData = new
                {
                    lastSync = DateTime.UtcNow.ToString("o"),
                    messages = messages.Select(m => new
                    {
                        messageId = m.MessageId,
                        senderId = m.SenderId,
                        receiverId = m.ReceiverId,
                        text = m.Text,
                        timestamp = m.Timestamp.ToString("o"),
                        isMyMessage = m.IsMyMessage,
                        status = m.Status.ToString(),
                        hasAttachment = m.HasAttachment,
                        fileName = m.FileName,
                        fileSize = m.FileSize,
                        isEdited = m.IsEdited,
                        isDeleted = m.IsDeleted,
                        isRead = m.IsRead
                    }).ToList()
                };

                var url = $"{FirebaseBaseUrl}/userChatHistory/{_userId}/{chatId}.json?auth={_idToken}";
                var json = JsonSerializer.Serialize(historyData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var client = new HttpClient();
                var response = await client.PutAsync(url, content);
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }

        // Загружаем историю с сервера
        public async Task<List<Message>> LoadChatHistoryAsync(string chatId)
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/userChatHistory/{_userId}/{chatId}.json?auth={_idToken}";
                
                using var client = new HttpClient();
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var historyData = JsonSerializer.Deserialize<FirebaseChatHistory>(json);
                        
                        if (historyData?.Messages != null)
                        {
                            var messages = new List<Message>();
                            
                            foreach (var msg in historyData.Messages)
                            {
                                messages.Add(new Message
                                {
                                    MessageId = msg.MessageId,
                                    SenderId = msg.SenderId,
                                    ReceiverId = msg.ReceiverId,
                                    Text = msg.Text,
                                    Timestamp = DateTime.Parse(msg.Timestamp),
                                    IsMyMessage = msg.IsMyMessage,
                                    Status = Enum.Parse<MessageStatus>(msg.Status),
                                    HasAttachment = msg.HasAttachment,
                                    FileName = msg.FileName,
                                    FileSize = msg.FileSize,
                                    IsEdited = msg.IsEdited,
                                    IsDeleted = msg.IsDeleted,
                                    IsRead = msg.IsRead
                                });
                            }
                            
                            return messages.OrderBy(m => m.Timestamp).ToList();
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

        // Удаляем историю чата
        public async Task DeleteChatHistoryAsync(string chatId)
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/userChatHistory/{_userId}/{chatId}.json?auth={_idToken}";
                
                using var client = new HttpClient();
                await client.DeleteAsync(url);
                
            }
            catch (Exception ex)
            {
            }
        }

        // Получаем список всех сохраненных чатов
        public async Task<List<string>> GetSavedChatIdsAsync()
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/userChatHistory/{_userId}.json?auth={_idToken}&shallow=true";
                
                using var client = new HttpClient();
                var response = await client.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    
                    if (!string.IsNullOrEmpty(json) && json != "null")
                    {
                        var chatIds = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        return chatIds?.Keys.ToList() ?? new List<string>();
                    }
                }
                
                return new List<string>();
            }
            catch (Exception ex)
            {
                return new List<string>();
            }
        }

        // Сохраняем локальные сообщения при выходе
        public async Task BackupAllChatsAsync(Dictionary<string, List<Message>> chatMessages)
        {
            try
            {
                
                foreach (var chat in chatMessages)
                {
                    if (chat.Value.Any())
                    {
                        await SaveChatHistoryAsync(chat.Key, chat.Value);
                        await Task.Delay(100); // Небольшая задержка чтобы не перегружать сервер
                    }
                }
                
            }
            catch (Exception ex)
            {
            }
        }
    }

    // Классы для десериализации
    public class FirebaseChatHistory
    {
        public string LastSync { get; set; }
        public List<FirebaseMessage> Messages { get; set; }
    }

    public partial class FirebaseMessage
    {
        public string MessageId { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Text { get; set; }
        public string Timestamp { get; set; }
        public bool IsMyMessage { get; set; }
        public string Status { get; set; }
        public bool HasAttachment { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public bool IsEdited { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsRead { get; set; }
    }
}