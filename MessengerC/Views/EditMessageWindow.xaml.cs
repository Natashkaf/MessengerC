// окно редактирования сообщения 
using System.Windows;

namespace MessengerApp
{
    public partial class EditMessageWindow : Window
    {
        public string EditedText { get; private set; }
        public string OriginalText { get; private set; }
        
        public EditMessageWindow(string originalText)
        {
            InitializeComponent();
            MessageTextBox.Text = originalText;
            MessageTextBox.SelectAll();
            MessageTextBox.Focus();
        }
        // обработчик нажатия на кнопку "сохранить"
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            EditedText = MessageTextBox.Text.Trim();
            DialogResult = true;
            Close();
        }
        // обработчик нажатия на кнопку "отмена"
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}