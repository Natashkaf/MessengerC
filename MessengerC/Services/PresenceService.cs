using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MessengerApp
{
    public class PresenceService : IDisposable
    {
        private readonly string _userId;
        private readonly string _idToken;
        private readonly HttpClient _httpClient;
        private DispatcherTimer _keepAliveTimer;
        private bool _isDisposed = false;
        
        private const string FirebaseBaseUrl = "https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app";
        
        public PresenceService(string userId, string idToken)
        {
            _userId = userId;
            _idToken = idToken;
            _httpClient = new HttpClient();
        }
        
        public async Task StartTracking()
        {
            try
            {
                // Устанавливаем статус "online"
                await SetStatusAsync("online", "в сети");
                
                // Таймер для поддержания статуса "online" 
                _keepAliveTimer = new DispatcherTimer();
                _keepAliveTimer.Interval = TimeSpan.FromSeconds(30);
                _keepAliveTimer.Tick += async (s, e) => 
                {
                    try
                    {
                        await UpdateLastSeenAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex.Message}");
                    }
                };
                _keepAliveTimer.Start();
                
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        public async Task SetStatusAsync(string status, string statusText)
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/presence/{_userId}.json?auth={_idToken}";
                
                var presenceData = new 
                {
                    status = status,
                    statusText = statusText,
                    lastSeen = DateTime.UtcNow.ToString("o")
                };
                
                var json = JsonSerializer.Serialize(presenceData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PutAsync(url, content);
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        public async Task UpdateLastSeenAsync()
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/presence/{_userId}/lastSeen.json?auth={_idToken}";
                var lastSeenJson = JsonSerializer.Serialize(DateTime.UtcNow.ToString("o"));
                var content = new StringContent(lastSeenJson, Encoding.UTF8, "application/json");
                
                await _httpClient.PutAsync(url, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }
        
        public async Task<PresenceStatus> GetUserStatusAsync(string userId)
        {
            try
            {
                var url = $"{FirebaseBaseUrl}/presence/{userId}.json";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    if (json != "null" && !string.IsNullOrEmpty(json))
                    {
                        return JsonSerializer.Deserialize<PresenceStatus>(json);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{userId}: {ex.Message}");
            }
            
            return new PresenceStatus 
            { 
                status = "offline", 
                statusText = "не в сети",
                lastSeen = DateTime.UtcNow.AddDays(-1).ToString("o")
            };
        }
        
        public async Task StopTracking()
        {
            try
            {
                _keepAliveTimer?.Stop();
                await SetStatusAsync("offline", "не в сети");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                StopTracking().Wait();
                _httpClient?.Dispose();
                _keepAliveTimer = null;
            }
        }
    }
    
    public partial class PresenceStatus
    {
        public string status { get; set; } = "offline";
        public string statusText { get; set; } = "не в сети";
        public string lastSeen { get; set; } = DateTime.UtcNow.ToString("o");
    }
}