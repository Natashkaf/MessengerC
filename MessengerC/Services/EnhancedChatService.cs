using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace MessengerApp
{
    public class EnhancedChatService : IDisposable
    {
        private readonly string _userId;
        private readonly string _idToken;
        private HttpClient _httpClient;
        private bool _disposed = false;
        private ChatService _chatService;
        
        public event EventHandler<EnhancedMessageEventArgs> NewMessageReceived;
        public event EventHandler<MessageStatusEventArgs> MessageStatusUpdated;
        public event EventHandler<TypingIndicatorEventArgs> TypingIndicatorReceived;
        public event EventHandler<MessageEditedEventArgs> MessageEdited;
        public event EventHandler<MessageDeletedEventArgs> MessageDeleted;
        
        public EnhancedChatService(string userId, string idToken)
        {
            _userId = userId;
            _idToken = idToken;
            _httpClient = new HttpClient();
            _chatService = new ChatService(userId, idToken);
        }
        
        public async Task<string> SendEnhancedMessageAsync(string receiverId, string text, 
            string fileUrl = null, string fileType = null, long? fileSize = null,
            string replyToMessageId = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(fileUrl))
                {
                    // Отправляем файл
                    var bytes = await File.ReadAllBytesAsync(fileUrl);
                    var base64Data = Convert.ToBase64String(bytes);
                    
                    return await _chatService.SendFileMessageAsync(
                        receiverId, 
                        Path.GetFileName(fileUrl) ?? "file", 
                        base64Data);
                }
                else
                {
                    // Отправляем текстовое сообщение
                    return await _chatService.SendMessageAsync(receiverId, text);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return null;
            }
        }
        
        // Редактировать сообщение
        public async Task<bool> EditMessageAsync(string messageId, string newText)
        {
            try
            {
                // Обновляем в Firebase
                var firebaseUrl = $"https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app/messages/all/{messageId}/text.json?auth={_idToken}";
                var content = new StringContent($"\"{newText}\"", Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync(firebaseUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    // Отмечаем как отредактированное
                    var editUrl = $"https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app/messages/all/{messageId}/isEdited.json?auth={_idToken}";
                    await _httpClient.PutAsync(editUrl, new StringContent("true", Encoding.UTF8, "application/json"));
                    
                    var editTimeUrl = $"https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app/messages/all/{messageId}/editedAt.json?auth={_idToken}";
                    await _httpClient.PutAsync(editTimeUrl, new StringContent($"\"{DateTime.UtcNow:o}\"", Encoding.UTF8, "application/json"));
                    
                    // Вызываем событие редактирования
                    MessageEdited?.Invoke(this, new MessageEditedEventArgs
                    {
                        MessageId = messageId,
                        NewText = newText,
                        EditorId = _userId
                    });
                    
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return false;
            }
        }
        
        // Удалить сообщение
        public async Task<bool> DeleteMessageAsync(string messageId, bool deleteForEveryone = false)
        {
            try
            {
                if (deleteForEveryone)
                {
                    // Удаляем для всех
                    var url = $"https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app/messages/all/{messageId}.json?auth={_idToken}";
                    var response = await _httpClient.DeleteAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        MessageDeleted?.Invoke(this, new MessageDeletedEventArgs
                        {
                            MessageId = messageId,
                            DeleterId = _userId,
                            DeleteForEveryone = true
                        });
                    }
                    
                    return response.IsSuccessStatusCode;
                }
                else
                {
                    // Удаляем только для себя
                    var url = $"https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app/messages/all/{messageId}/isDeleted.json?auth={_idToken}";
                    var response = await _httpClient.PutAsync(url, new StringContent("true", Encoding.UTF8, "application/json"));
                    
                    if (response.IsSuccessStatusCode)
                    {
                        MessageDeleted?.Invoke(this, new MessageDeletedEventArgs
                        {
                            MessageId = messageId,
                            DeleterId = _userId,
                            DeleteForEveryone = false
                        });
                    }
                    
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return false;
            }
        }
        
        // Загрузить медиа файл
        public async Task<string> UploadMediaAsync(string filePath, string fileName)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(filePath);
                var base64Data = Convert.ToBase64String(bytes);
                
                // Сохраняем в Firebase Realtime Database
                var fileId = Guid.NewGuid().ToString();
                var url = $"https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app/files/{fileId}.json?auth={_idToken}";
                
                var fileData = new
                {
                    fileName = fileName,
                    fileData = base64Data,
                    fileSize = bytes.Length,
                    uploadedBy = _userId,
                    uploadedAt = DateTime.UtcNow.ToString("o"),
                    contentType = GetContentType(filePath)
                };
                
                var json = JsonSerializer.Serialize(fileData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    return fileId; 
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
                return null;
            }
        }
        
        private string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }
        
        // Получить файл по ID
        public async Task<byte[]> GetFileAsync(string fileId)
        {
            try
            {
                var url = $"https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app/files/{fileId}/fileData.json";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var base64Data = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(base64Data) && base64Data != "null")
                    {
                        base64Data = base64Data.Trim('"');
                        return Convert.FromBase64String(base64Data);
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return null;
            }
        }
        
        // Получить непрочитанные сообщения
        public async Task<List<Message>> GetUnreadMessagesAsync()
        {
            try
            {
                return await _chatService.GetUnreadMessagesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return new List<Message>();
            }
        }
        
        // Поиск по сообщениям
        public async Task<List<Message>> SearchMessagesAsync(string query, string chatId = null)
        {
            try
            {
                var allMessages = await _chatService.GetAllMessagesAsync();
                var queryLower = query.ToLowerInvariant();
                
                return allMessages
                    .Where(m => m.Text?.ToLower().Contains(queryLower) == true)
                    .Where(m => chatId == null || m.ChatId == chatId)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
                return new List<Message>();
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
                    _httpClient?.Dispose();
                    _chatService?.Dispose();
                }
                
                _disposed = true;
            }
        }
    }
}