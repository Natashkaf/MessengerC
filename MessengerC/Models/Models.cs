using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MessengerApp
{
    
    public class FirebaseUserInfo
    {
        [JsonPropertyName("users")]
        public FirebaseUser[] Users { get; set; } = Array.Empty<FirebaseUser>();
    }

    public class FirebaseUser
    {
        [JsonPropertyName("localId")]
        public string LocalId { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("emailVerified")]
        public bool EmailVerified { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("photoUrl")]
        public string? PhotoUrl { get; set; }

        [JsonPropertyName("disabled")]
        public bool Disabled { get; set; }

        [JsonPropertyName("lastLoginAt")]
        public string LastLoginAt { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = string.Empty;
    }

    public class FirebaseError
    {
        [JsonPropertyName("error")]
        public FirebaseErrorDetail Error { get; set; } = new();
    }

    public class FirebaseErrorDetail
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("errors")]
        public List<FirebaseErrorDetail> Errors { get; set; } = new();
    }

    
public enum PhoneVisibility
{
    Everyone,
    Contacts,
    Nobody
}

// Модель для смены пароля/email
public class AccountUpdateRequest
{
    [JsonPropertyName("currentPassword")]
    public string CurrentPassword { get; set; } = string.Empty;

    [JsonPropertyName("newPassword")]
    public string? NewPassword { get; set; }

    [JsonPropertyName("newEmail")]
    public string? NewEmail { get; set; }
}





}