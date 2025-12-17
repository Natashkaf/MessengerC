using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MessengerApp
{
    public class ProfileRestService
    {
        private readonly string _userId;
        private readonly string _idToken;
        private readonly HttpClient _httpClient = new();
        private const string FirebaseDatabaseUrl = "https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app";

        public ProfileRestService(string userId, string idToken)
        {
            _userId = userId;
            _idToken = idToken;
        }

        public async Task<UserProfile> GetProfileAsync()
        {
            try
            {
                var url = $"{FirebaseDatabaseUrl}/profiles/{_userId}.json";
                var response = await _httpClient.GetAsync(url);
                var responseText = await response.Content.ReadAsStringAsync();

                UserProfile profile = null;
            
                if (response.IsSuccessStatusCode && responseText != "null")
                {
                    profile = JsonSerializer.Deserialize<UserProfile>(responseText);
                    profile.UserId = _userId;
                }
                else
                {
                    // Создаем новый профиль если не существует
                    profile = await CreateDefaultProfileAsync();
                }
            
                // Загружаем аватар
                var avatarBase64 = await GetAvatarAsync(_userId);
                if (!string.IsNullOrEmpty(avatarBase64))
                {
                    profile.AvatarBase64 = avatarBase64;
                }
            
                return profile;
            }
            catch (Exception ex)
            {
                return await CreateDefaultProfileAsync();
            }
        }

        public async Task<string> GetAvatarAsync(string userId)
        {
            try
            {
                var url = $"{FirebaseDatabaseUrl}/avatars/{userId}.json";
                var response = await _httpClient.GetAsync(url);
                var responseText = await response.Content.ReadAsStringAsync();
        
                if (response.IsSuccessStatusCode && responseText != "null")
                {
                    using var doc = JsonDocument.Parse(responseText);
                    if (doc.RootElement.TryGetProperty("base64", out var base64Element))
                    {
                        return base64Element.GetString();
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<bool> SaveAvatarAsync(string base64Image)
        {
            try
            {
                var url = $"{FirebaseDatabaseUrl}/avatars/{_userId}.json?auth={_idToken}";
        
                var payload = new 
                { 
                    base64 = base64Image, 
                    updatedAt = DateTime.UtcNow.ToString("o") 
                };
        
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
        
                var response = await _httpClient.PutAsync(url, content);
        
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task<bool> SaveProfileAsync(UserProfile profile)
        {
            try
            {
                // имя, описание и статус
                var profileData = new 
                {
                    userId = profile.UserId,
                    displayName = profile.DisplayName,
                    bio = profile.Bio,
                    statusText = profile.StatusText,
                    updatedAt = DateTime.UtcNow.ToString("o")
                };
            
                var url = $"{FirebaseDatabaseUrl}/profiles/{profile.UserId}.json?auth={_idToken}";
                var json = JsonSerializer.Serialize(profileData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
            
                var response = await _httpClient.PutAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private async Task<UserProfile> CreateDefaultProfileAsync()
        {
            var profile = new UserProfile
            {
                UserId = _userId,
                DisplayName = "Новый пользователь",
                Bio = "Привет! Я новый пользователь этого крутого мессенджера",
                StatusText = "Доступен",
                UpdatedAt = DateTime.UtcNow
            };

            await SaveProfileAsync(profile);
            return profile;
        }

        // Получение публичного профиля другого пользователя
        public async Task<UserProfile> GetPublicProfileAsync(string otherUserId)
        {
            try
            {
                var url = $"{FirebaseDatabaseUrl}/profiles/{otherUserId}.json";
                var response = await _httpClient.GetAsync(url);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode && responseText != "null")
                {
                    var profile = JsonSerializer.Deserialize<UserProfile>(responseText);
                    
                    // Загружаем аватар
                    var avatar = await GetAvatarAsync(otherUserId);
                    if (!string.IsNullOrEmpty(avatar))
                    {
                        profile.AvatarBase64 = avatar;
                    }
                    
                    return profile;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }
}