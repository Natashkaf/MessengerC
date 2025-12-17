using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MessengerApp
{
    public class FirebaseRestAuth
    {
        private const string ApiKey = "AIzaSyBgSMK1w7m3tvXSlNzXnQU27cSHtQP9zEE";
        private const string FirebaseAuthUrl = "https://identitytoolkit.googleapis.com/v1/accounts";

        private readonly HttpClient _httpClient = new();

        // Регистрация нового пользователя 
        public async Task<AuthResponse> CreateUserAsync(string email, string password, string? displayName = null)
        {
            var url = $"{FirebaseAuthUrl}:signUp?key={ApiKey}";
            
            var payload = new
            {
                email,
                password,
                returnSecureToken = true,
                displayName
            };

            return await PostAsync<AuthResponse>(url, payload);
        }

        // Вход по email + пароль 
        public async Task<AuthResponse> SignInAsync(string email, string password)
        {
            var url = $"{FirebaseAuthUrl}:signInWithPassword?key={ApiKey}";
            
            var payload = new
            {
                email,
                password,
                returnSecureToken = true
            };

            return await PostAsync<AuthResponse>(url, payload);
        }

        //  Вход через Google ID Token 
        public async Task<AuthResponse> SignInWithGoogleAsync(string idToken)
        {
            var url = $"{FirebaseAuthUrl}:signInWithIdp?key={ApiKey}";
            
            var payload = new
            {
                postBody = $"id_token={Uri.EscapeDataString(idToken)}&providerId=google.com",
                requestUri = "http://localhost",
                returnIdpCredential = true,
                returnSecureToken = true
            };

            return await PostAsync<AuthResponse>(url, payload);
        }

        // Получение информации о пользователе по токену
        public async Task<FirebaseUser?> GetUserInfoAsync(string idToken)
        {
            try
            {
                var url = $"{FirebaseAuthUrl}:lookup?key={ApiKey}";
                
                var payload = new
                {
                    idToken
                };

                var response = await PostAsync<FirebaseUserInfo>(url, payload);
                return response.Users?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        //  Получение информации о пользователе по email 
        public async Task<FirebaseUser?> GetUserInfoByEmailAsync(string email)
        {
            try
            {
                var url = $"{FirebaseAuthUrl}:lookup?key={ApiKey}";
                
                var payload = new
                {
                    email
                };

                var response = await PostAsync<FirebaseUserInfo>(url, payload);
                return response.Users?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        // Получение неподтвержденного пользователя по email 
        public async Task<FirebaseUser?> GetUnverifiedUserAsync(string email)
        {
            try
            {
                var user = await GetUserInfoByEmailAsync(email);
                // Возвращаем пользователя только если он не подтвержден
                return user != null && !user.EmailVerified ? user : null;
            }
            catch
            {
                return null;
            }
        }

        // Обновление профиля пользователя 
        public async Task<UpdateProfileResponse> UpdateProfileAsync(string idToken, string? displayName, string? photoUrl)
        {
            var url = $"{FirebaseAuthUrl}:update?key={ApiKey}";
            
            var payload = new UpdateProfileRequest
            {
                IdToken = idToken,
                DisplayName = displayName,
                PhotoUrl = photoUrl,
                ReturnSecureToken = true
            };

            return await PostAsync<UpdateProfileResponse>(url, payload);
        }

        //  Отправка письма для подтверждения email 
        public async Task SendEmailVerificationAsync(string idToken)
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={ApiKey}";
            
            var payload = new
            {
                requestType = "VERIFY_EMAIL",
                idToken = idToken
            };

            await PostAsync<object>(url, payload);
        }

        //Повторная отправка письма для подтверждения email 
        public async Task ResendEmailVerificationAsync(string idToken)
        {
            await SendEmailVerificationAsync(idToken);
        }

        //  Отправка письма для сброса пароля 
        public async Task SendPasswordResetEmailAsync(string email)
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={ApiKey}";
            
            var payload = new
            {
                requestType = "PASSWORD_RESET",
                email = email
            };

            await PostAsync<object>(url, payload);
        }

        //  Обновление токена 
        public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
        {
            var url = $"https://securetoken.googleapis.com/v1/token?key={ApiKey}";
            
            var payload = new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken
            };

            return await PostAsync<AuthResponse>(url, payload);
        }

        // Удаление аккаунта 
        public async Task DeleteAccountAsync(string idToken)
        {
            var url = $"{FirebaseAuthUrl}:delete?key={ApiKey}";
            
            var payload = new
            {
                idToken
            };

            await PostAsync<object>(url, payload);
        }

        // Удаление неподтвержденного аккаунта 
        public async Task DeleteUnverifiedAccountAsync(string email, string password)
        {
            try
            {
                // Сначала пытаемся войти
                var signInResponse = await SignInAsync(email, password);
                
                // Проверяем, подтвержден ли email
                var userInfo = await GetUserInfoAsync(signInResponse.IdToken);
                if (userInfo != null && userInfo.EmailVerified)
                {
                    throw new FirebaseAuthException("Аккаунт уже подтвержден. Нельзя удалить подтвержденный аккаунт.");
                }
                
                // Если email не подтвержден - удаляем аккаунт
                await DeleteAccountAsync(signInResponse.IdToken);
            }
            catch (FirebaseAuthException ex) when (ex.FirebaseErrorCode == "INVALID_PASSWORD" || ex.Message.Contains("INVALID_PASSWORD"))
            {
                throw new FirebaseAuthException("Неверный пароль для существующего аккаунта", "INVALID_PASSWORD");
            }
            catch (Exception ex)
            {
                throw new FirebaseAuthException($"Не удалось удалить существующий аккаунт: {ex.Message}");
            }
        }

        // Проверка, подтвержден ли email 
        public async Task<bool> CheckEmailVerifiedAsync(string idToken)
        {
            var user = await GetUserInfoAsync(idToken);
            return user?.EmailVerified ?? false;
        }

        // Изменение email 
        public async Task<AuthResponse> ChangeEmailAsync(string idToken, string newEmail)
        {
            var url = $"{FirebaseAuthUrl}:update?key={ApiKey}";
            
            var payload = new
            {
                idToken = idToken,
                email = newEmail,
                returnSecureToken = true
            };

            return await PostAsync<AuthResponse>(url, payload);
        }

        // Изменение пароля 
        public async Task<AuthResponse> ChangePasswordAsync(string idToken, string newPassword)
        {
            var url = $"{FirebaseAuthUrl}:update?key={ApiKey}";
            
            var payload = new
            {
                idToken = idToken,
                password = newPassword,
                returnSecureToken = true
            };

            return await PostAsync<AuthResponse>(url, payload);
        }

        private async Task<T> PostAsync<T>(string url, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var error = JsonSerializer.Deserialize<FirebaseError>(responseText);
                    var errorMessage = error?.Error?.Message ?? "Unknown error";
                    
                    // Более понятные сообщения об ошибках
                    var userFriendlyMessage = GetUserFriendlyErrorMessage(errorMessage);
                    throw new FirebaseAuthException(userFriendlyMessage, errorMessage);
                }
                catch (JsonException)
                {
                    throw new FirebaseAuthException($"Firebase Auth Error: {responseText}");
                }
            }

            return JsonSerializer.Deserialize<T>(responseText)!;
        }

        private string GetUserFriendlyErrorMessage(string firebaseError)
        {
            return firebaseError switch
            {
                "EMAIL_NOT_FOUND" => "Пользователь с таким email не найден",
                "INVALID_PASSWORD" => "Неверный пароль",
                "USER_DISABLED" => "Аккаунт заблокирован",
                "EMAIL_EXISTS" => "Пользователь с таким email уже существует",
                "OPERATION_NOT_ALLOWED" => "Регистрация отключена",
                "TOO_MANY_ATTEMPTS_TRY_LATER" => "Слишком много попыток. Попробуйте позже",
                "WEAK_PASSWORD : Password should be at least 6 characters" => "Пароль должен содержать минимум 6 символов",
                "INVALID_ID_TOKEN" => "Неверный токен авторизации",
                "TOKEN_EXPIRED" => "Срок действия токена истек",
                "USER_NOT_FOUND" => "Пользователь не найден",
                "INVALID_REFRESH_TOKEN" => "Неверный refresh token",
                "INVALID_GRANT_TYPE" => "Неверный тип авторизации",
                "MISSING_REFRESH_TOKEN" => "Отсутствует refresh token",
                "EXPIRED_OOB_CODE" => "Срок действия кода подтверждения истек",
                "INVALID_OOB_CODE" => "Неверный код подтверждения",
                "CREDENTIAL_TOO_OLD_LOGIN_AGAIN" => "Требуется повторный вход",
                _ => firebaseError
            };
        }
    }

    // Исключение для Firebase Auth
    public class FirebaseAuthException : Exception
    {
        public string FirebaseErrorCode { get; }

        public FirebaseAuthException(string message, string firebaseErrorCode = "") 
            : base(message)
        {
            FirebaseErrorCode = firebaseErrorCode;
        }
    }

    // Модель ответа 
    public class AuthResponse
    {
        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = string.Empty;
        
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
        
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
        
        [JsonPropertyName("localId")]
        public string LocalId { get; set; } = string.Empty;
        
        [JsonPropertyName("registered")]
        public bool Registered { get; set; }
        
        [JsonPropertyName("expiresIn")]
        public string ExpiresIn { get; set; } = string.Empty;
        
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
        
        [JsonPropertyName("photoUrl")]
        public string? PhotoUrl { get; set; }
    }

    // Запрос на обновление профиля 
    public class UpdateProfileRequest
    {
        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("photoUrl")]
        public string? PhotoUrl { get; set; }

        [JsonPropertyName("deleteAttribute")]
        public List<string>? DeleteAttribute { get; set; }

        [JsonPropertyName("returnSecureToken")]
        public bool ReturnSecureToken { get; set; } = true;
    }

    // Ответ на обновление профиля 
    public class UpdateProfileResponse
    {
        [JsonPropertyName("localId")]
        public string LocalId { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("photoUrl")]
        public string? PhotoUrl { get; set; }

        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresIn")]
        public string ExpiresIn { get; set; } = string.Empty;
    }
}