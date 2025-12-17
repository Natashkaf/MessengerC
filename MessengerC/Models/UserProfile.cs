// Models/UserProfile.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MessengerApp
{
    public enum UserStatus
    {
        Online,
        Away,
        DoNotDisturb,
        Invisible,
        Offline
    }

    public enum LastSeenVisibility
    {
        Everyone,
        Contacts,
        Nobody
    }

    public enum ProfilePhotoVisibility
    {
        Everyone,
        Contacts,
        Nobody
    }

    public enum GroupInviteVisibility
    {
        Everyone,
        Contacts,
        Nobody
    }

    // Модель профиля пользователя
    public class UserProfile
    {
        public string UserId { get; set; }
        public string DisplayName { get; set; }     
        public string Bio { get; set; }             
        public string StatusText { get; set; }      
        public string AvatarBase64 { get; set; }   
        public DateTime UpdatedAt { get; set; }   
       
    }

    // Настройки приватности
    public class PrivacySettings
    {
        public LastSeenVisibility LastSeenVisible { get; set; } = LastSeenVisibility.Everyone;
        public ProfilePhotoVisibility ProfilePhotoVisible { get; set; } = ProfilePhotoVisibility.Everyone;
        public GroupInviteVisibility InviteToGroups { get; set; } = GroupInviteVisibility.Contacts;
        public bool ForwardMessagesVisible { get; set; } = true;
    }

    // Настройки уведомлений
    public partial class NotificationSettings
    {
        public bool EnableNotifications { get; set; } = true;
        public bool EnableSounds { get; set; } = true;
        public bool EnableVibrations { get; set; } = true;
        public bool ShowPreview { get; set; } = true;
        public bool IgnoreMentions { get; set; } = false;
        public string NotificationSound { get; set; } = "default";
        public DateTime? MuteUntil { get; set; }
    }

    // Настройки аккаунта
    public class AccountSettings
    {
        public bool EmailNotifications { get; set; } = true;
        public bool TwoFactorAuth { get; set; } = false;
        public bool LoginNotifications { get; set; } = true;
        public int SessionTimeout { get; set; } = 60;
    }
}