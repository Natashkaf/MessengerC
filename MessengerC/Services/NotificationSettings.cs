public class NotificationSettings
{
    public bool IsEnabled { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool ShowBanner { get; set; } = true;
    public bool SmartNotifications { get; set; } = true;
    public bool ShowPreview { get; set; } = true;
    public bool Vibrate { get; set; } = false;
    public string SoundName { get; set; } = "default";
    public Dictionary<string, bool> MutedChats { get; set; } = new Dictionary<string, bool>();
}