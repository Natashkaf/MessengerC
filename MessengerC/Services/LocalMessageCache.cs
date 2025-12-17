using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MessengerApp
{
    public class LocalMessageCache
    {
        private readonly string _userId;
        private readonly string _cacheDirectory;
        
        // Кэш сообщений в памяти: chatId -> List<Message>
        private readonly Dictionary<string, List<Message>> _messageCache = new();

        public LocalMessageCache(string userId)
        {
            _userId = userId;
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MessengerApp",
                "Cache",
                userId
            );
            
            Directory.CreateDirectory(_cacheDirectory);
        }

        // Добавить сообщение в кэш
        public void AddMessage(string chatId, Message message)
        {
            if (!_messageCache.ContainsKey(chatId))
            {
                _messageCache[chatId] = new List<Message>();
            }
            
            // Проверяем, нет ли уже такого сообщения
            var existingMessage = _messageCache[chatId]
                .FirstOrDefault(m => m.MessageId == message.MessageId);
            
            if (existingMessage == null)
            {
                _messageCache[chatId].Add(message);
                
                // Автосохранение при добавлении 10 сообщений
                if (_messageCache[chatId].Count % 10 == 0)
                {
                    _ = SaveChatToDiskAsync(chatId);
                }
            }
        }

        // Получить сообщения из кэша
        public List<Message> GetMessages(string chatId)
        {
            if (_messageCache.TryGetValue(chatId, out var messages))
            {
                return messages.OrderBy(m => m.Timestamp).ToList();
            }
            
            return new List<Message>();
        }

        // Очистить кэш чата
        public void ClearChatCache(string chatId)
        {
            _messageCache.Remove(chatId);
            DeleteCacheFile(chatId);
        }

        // Загрузить все кэшированные чаты
        public async Task LoadAllCachesAsync()
        {
            try
            {
                var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
                
                foreach (var file in cacheFiles)
                {
                    var chatId = Path.GetFileNameWithoutExtension(file);
                    await LoadChatFromDiskAsync(chatId);
                }
                
                
            }
            catch (Exception ex)
            {
               
            }
        }

        // Сохранить кэш на диск
        private async Task SaveChatToDiskAsync(string chatId)
        {
            try
            {
                if (_messageCache.TryGetValue(chatId, out var messages))
                {
                    var cacheFile = GetCacheFilePath(chatId);
                    var cacheData = new CacheData
                    {
                        LastUpdate = DateTime.UtcNow,
                        Messages = messages
                    };
                    
                    var json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    
                    await File.WriteAllTextAsync(cacheFile, json);
                    
                    
                }
            }
            catch (Exception ex)
            {
                
            }
        }

        // Загрузить кэш с диска
        private async Task LoadChatFromDiskAsync(string chatId)
        {
            try
            {
                var cacheFile = GetCacheFilePath(chatId);
                
                if (File.Exists(cacheFile))
                {
                    var json = await File.ReadAllTextAsync(cacheFile);
                    var cacheData = JsonSerializer.Deserialize<CacheData>(json);
                    
                    if (cacheData?.Messages != null)
                    {
                        _messageCache[chatId] = cacheData.Messages;
                        Console.WriteLine($"💾 Кэш чата {chatId} загружен с диска ({cacheData.Messages.Count} сообщений)");
                    }
                }
            }
            catch (Exception ex)
            {
                
            }
        }

        // Удалить файл кэша
        private void DeleteCacheFile(string chatId)
        {
            try
            {
                var cacheFile = GetCacheFilePath(chatId);
                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                    Console.WriteLine($"🗑️ Файл кэша {chatId} удален");
                }
            }
            catch (Exception ex)
            {
               
            }
        }

        // Путь к файлу кэша
        private string GetCacheFilePath(string chatId)
        {
            return Path.Combine(_cacheDirectory, $"{chatId}.json");
        }

        // Получить все кэшированные чаты
        public Dictionary<string, List<Message>> GetAllCachedChats()
        {
            return new Dictionary<string, List<Message>>(_messageCache);
        }

        // Синхронизировать кэш с сервером
        public async Task SyncWithServerAsync(ChatHistoryService historyService)
        {
            try
            {
                
                
                foreach (var chat in _messageCache)
                {
                    if (chat.Value.Any())
                    {
                        await historyService.SaveChatHistoryAsync(chat.Key, chat.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                
            }
        }
    }

    // Класс для хранения данных кэша
    public class CacheData
    {
        public DateTime LastUpdate { get; set; }
        public List<Message> Messages { get; set; }
    }
}