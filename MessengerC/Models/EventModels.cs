
using System;

namespace MessengerApp
{
    public class TypingIndicator
    {
        public string UserId { get; set; }
        public bool IsTyping { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    public partial class PresenceStatus
    {
        public string Status { get; set; }
        public string StatusText { get; set; }
        public DateTime LastSeen { get; set; }
    }
    
    public class MessageStatusUpdate
    {
        public string MessageId { get; set; }
        public MessageStatus Status { get; set; }
    }
    
    // Классы событий
    public class MessageEventArgs : EventArgs
    {
        public Message Message { get; set; }
    }
    
    public class MessageStatusEventArgs : EventArgs
    {
        public string MessageId { get; set; }
        public MessageStatus Status { get; set; }
    }
    
    public class TypingIndicatorEventArgs : EventArgs
    {
        public string UserId { get; set; }
        public bool IsTyping { get; set; }
    }
    
    public class ContactStatusEventArgs : EventArgs
    {
        public string UserId { get; set; }
        public string Status { get; set; }
        public string StatusText { get; set; }
    }
}