using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Shapes;
using System.IO;
using Path = System.Windows.Shapes.Path;

namespace MessengerApp
{
    public enum MessageStatus
    {
        Sending,
        Sent,
        Delivered,
        Read,
        Error,
        Failed
    }

    public class Message : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _text = string.Empty;
        private bool _isDeleted = false;
        private bool _isEdited = false;
        private MessageStatus _status = MessageStatus.Sending;
        
        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        
        // Отправитель и получатель
        public string SenderId { get; set; } = string.Empty;
        public string ReceiverId { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        
        // Контент сообщения
        public string Text 
        { 
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }
        
        // Для файлов
        public string FileName { get; set; }
        public string FileData { get; set; }
        public long? FileSize { get; set; }
        public bool HasAttachment { get; set; }
        
        // Статус
        public MessageStatus Status 
        { 
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusIcon));
                }
            }
        }
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; }
        
        public bool IsDeleted 
        { 
            get => _isDeleted;
            set
            {
                if (_isDeleted != value)
                {
                    _isDeleted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }
        
        // Для UI
        [JsonIgnore]
        public bool IsMyMessage { get; set; }
        
        [JsonIgnore]
        public bool IsDateSeparator { get; set; }
        
        [JsonIgnore]
        public string Date { get; set; }
        
        [JsonIgnore]
        public bool IsMessage => !IsDateSeparator;
        
        [JsonIgnore]
        public string Time => FormatTime(Timestamp);
        
        [JsonIgnore]
        public string StatusIcon => GetStatusIcon();

        public bool IsEdited 
        { 
            get => _isEdited;
            set
            {
                if (_isEdited != value)
                {
                    _isEdited = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        public bool CanBeEdited 
        { 
            get 
            { 
                return IsMyMessage && !IsDeleted && (DateTime.UtcNow - Timestamp).TotalHours < 48; 
            } 
        }

        [JsonIgnore]
        public string DisplayText => IsDeleted ? "Сообщение удалено" : Text;

        private string GetStatusIcon()
        {
            return Status switch
            {
                MessageStatus.Sent => "✓",
                MessageStatus.Delivered => "✓✓",
                MessageStatus.Read => "✓✓",
                MessageStatus.Error => "✗",
                MessageStatus.Failed => "✗",
                _ => "..."
            };
        }

        private string FormatTime(DateTime timestamp)
        {
            var now = DateTime.Now;
            var time = timestamp.ToLocalTime();
            
            if (time.Date == now.Date)
                return time.ToString("HH:mm");
            else if (time.Date == now.Date.AddDays(-1))
                return "вчера " + time.ToString("HH:mm");
            else if (time.Year == now.Year)
                return time.ToString("dd.MM HH:mm");
            else
                return time.ToString("dd.MM.yy HH:mm");
        }
        [JsonIgnore]
        public string FileIcon
        {
            get
            {
                if (!HasAttachment || string.IsNullOrEmpty(FileName)) 
                    return string.Empty;
        
                var extension = System.IO.Path.GetExtension(FileName).ToLower();
                return extension switch
                {
                    ".pdf" => "📕",
                    ".doc" or ".docx" => "📝",
                    ".xls" or ".xlsx" => "📊",
                    ".ppt" or ".pptx" => "📽️",
                    ".txt" => "📄",
                    ".zip" or ".rar" or ".7z" => "📦",
                    ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "🖼️",
                    ".mp4" or ".avi" or ".mov" or ".mkv" => "🎬",
                    ".mp3" or ".wav" or ".ogg" => "🎵",
                    _ => "📎"
                };
            }
        }

        [JsonIgnore]
        public string FileSizeText
        {
            get
            {
                if (!FileSize.HasValue) return "";
        
                string[] sizes = { "B", "KB", "MB", "GB" };
                int order = 0;
                double len = FileSize.Value;

                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }

                return $"{len:0.##} {sizes[order]}";
            }
        }
    }
    
    public class Contact : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _name = string.Empty;
        private string _lastMessage = string.Empty;
        private string _time = string.Empty;
        private string _status = "offline";
        private string _statusText = "не в сети";
        private int _unreadCount = 0;
        
        public string UserId { get; set; } = string.Empty;
        
        public string Name 
        { 
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string LastMessage 
        { 
            get => _lastMessage;
            set
            {
                if (_lastMessage != value)
                {
                    _lastMessage = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Time 
        { 
            get => _time;
            set
            {
                if (_time != value)
                {
                    _time = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Status 
        { 
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }
        
        public string StatusText 
        { 
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Initials { get; set; } = string.Empty;
        
        public int UnreadCount 
        { 
            get => _unreadCount;
            set
            {
                if (_unreadCount != value)
                {
                    _unreadCount = value;
                    OnPropertyChanged();
                }
            }
        }
        
        [JsonIgnore]
        public string StatusColor => Status switch
        {
            "online" => "#4CAF50",
            "away" => "#FF9800",
            _ => "#9E9E9E"
        };
    }

    public class Chat
    {
        public string ChatId { get; set; }
        public string Participant1Id { get; set; }
        public string Participant2Id { get; set; }
        public DateTime Created { get; set; }
        public string LastMessage { get; set; }
        public DateTime LastMessageTime { get; set; }
        
        // Информация о собеседнике
        public string OtherUserName { get; set; }
        public string OtherUserAvatar { get; set; }
        public string OtherUserStatus { get; set; }
        public string OtherUserStatusText { get; set; }
    }
    public partial class DeliveryReceipt
    {
        public string Type { get; set; } = "delivery_receipt";
        public string MessageId { get; set; }
        public string SenderId { get; set; }   
        public string ReceiverId { get; set; }  
        public string Status { get; set; }      
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}