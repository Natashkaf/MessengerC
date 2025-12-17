using System.Windows;

namespace MessengerApp
{
    public partial class UploadProgressWindow : Window
    {
        public UploadProgressWindow(string fileName)
        {
            InitializeComponent();
            
            FileNameTextBlock.Text = $"Отправка: {System.IO.Path.GetFileName(fileName)}";
        }
        
        public void UpdateProgress(int percentage)
        {
            Dispatcher.Invoke(() =>
            {
                UploadProgressBar.Value = percentage;
                ProgressTextBlock.Text = $"{percentage}%";
            });
        }
    }
}