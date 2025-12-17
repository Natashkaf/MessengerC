using System.Windows.Media;

namespace MessengerApp
{
    public class EnhancedMessage : Message
    {
        public string FileUrl { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; } 
        public long FileSize { get; set; }
        public string ThumbnailUrl { get; set; }
        public bool IsPhoto { get; set; }
        public bool HasAttachment { get; set; }
        public bool HasText { get; set; }
        public bool IsText { get; set; }
        public ImageSource ImageSource { get; set; }
        public bool CanBeEdited { get; set; }
        public DateTime? EditedAt { get; set; }
        public string ReplyToMessageId { get; set; }
        public EnhancedMessage RepliedMessage { get; set; }
        public bool IsEdited { get; set; }

        public EnhancedMessage()
        {
            HasText = !string.IsNullOrEmpty(Text);
            IsText = !HasAttachment && HasText;
        }
    }
    
    public class EnhancedMessageEventArgs : EventArgs
    {
        public string MessageId { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Text { get; set; }
        public string FileUrl { get; set; }
        public string FileType { get; set; }
        public DateTime Timestamp { get; set; }
        public string ThumbnailUrl { get; set; }
    }

    public class MessageEditedEventArgs : EventArgs
    {
        public string MessageId { get; set; }
        public string NewText { get; set; }
        public DateTime EditedAt { get; set; }
        public string EditorId { get; set; }
    }

    public class MessageDeletedEventArgs : EventArgs
    {
        public string MessageId { get; set; }
        public bool DeleteForEveryone { get; set; }
        public string DeleterId { get; set; }
    }
}