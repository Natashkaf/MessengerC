// окно для изменения email
using System;
using System.Windows;
using System.Windows.Controls;

namespace MessengerApp
{
    public partial class ChangeEmailWindow : Window
    {
        // токен аунтентификации fierbase 
        private readonly string _idToken;

        public ChangeEmailWindow(string idToken)
        {
            InitializeComponent();
            _idToken = idToken;
        }
// асинхронный обработчик кнопки изменения email 
        private async void ChangeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;

            try
            {
                SetLoading(true);
                
                var auth = new FirebaseRestAuth();
                await auth.ChangeEmailAsync(_idToken, NewEmailBox.Text.Trim());
                
                MessageBox.Show(
                    "Email успешно изменен!\n\n" +
                    "На новый email отправлено письмо для подтверждения.",
                    "Успех", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
                
                DialogResult = true;
                Close();
            }
            // обработчик ошибок в fierbase 
            catch (FirebaseAuthException ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // обработчик остальных ошибок 
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения email: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // гарантированное выполнение 
            finally
            {
                SetLoading(false);
            }
        }
// проверка введенных данных на корректность 
        private bool ValidateInput()
        {
            // проверка пароля(что он не пустой)
            if (string.IsNullOrEmpty(PasswordBox.Password))
            {
                MessageBox.Show("Введите ваш пароль для подтверждения", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PasswordBox.Focus();
                return false;
            }
// проверка email (что не пустой)
            if (string.IsNullOrEmpty(NewEmailBox.Text))
            {
                MessageBox.Show("Введите новый email", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NewEmailBox.Focus();
                return false;
            }
            // проверка, что email имеет корректный формат через метод IsValidEmail

            if (!IsValidEmail(NewEmailBox.Text))
            {
                MessageBox.Show("Введите корректный email", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NewEmailBox.Focus();
                return false;
            }

            return true;
        }
// проверка корретности email с использованием встроенных функций .NET
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                // дополнительная проверка 
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
// усправляет состоянием загрузки UI 
        private void SetLoading(bool isLoading)
        {
            ChangeButton.IsEnabled = !isLoading;
            CancelButton.IsEnabled = !isLoading;
            
            if (isLoading)
            {
                ChangeButton.Content = "Изменение...";
            }
            else
            {
                ChangeButton.Content = "Изменить email";
            }
        }
// обработчик кнопки "отмена"
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}