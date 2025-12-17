// окно для изменения пароля 
using System;
using System.Windows;
using System.Windows.Controls;

namespace MessengerApp
{
    public partial class ChangePasswordWindow : Window
    {
        private readonly string _idToken;

        public ChangePasswordWindow(string idToken)
        {
            InitializeComponent();
            _idToken = idToken;
        }
// обработчик кнопки "изменить пароль"
        private async void ChangeButton_Click(object sender, RoutedEventArgs e)
        {
            // если ввод некорректен - выходим 
            if (!ValidateInput()) return;

            try
            {
                SetLoading(true);
                // создаем клиент fierbase 
                var auth = new FirebaseRestAuth();
                // асинхронный вызов API 
                await auth.ChangePasswordAsync(_idToken, NewPasswordBox.Password);
                
                MessageBox.Show("Пароль успешно изменен!", "Успех", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                DialogResult = true;
                Close();
            }
            // обработка ошибок fierbase 
            catch (FirebaseAuthException ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // обработка общих ошибок 
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения пароля: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetLoading(false);
            }
        }
//проверяет, что пароль корректен 
        private bool ValidateInput()
        {
            // проверка, что поле "текущий пароль" не пустое 
            if (string.IsNullOrEmpty(CurrentPasswordBox.Password))
            {
                MessageBox.Show("Введите текущий пароль", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CurrentPasswordBox.Focus();
                return false;
            }
// проверка, что поле"введите новый пароль" не пустое 
            if (string.IsNullOrEmpty(NewPasswordBox.Password))
            {
                MessageBox.Show("Введите новый пароль", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NewPasswordBox.Focus();
                return false;
            }
// проверка длины нового пароля 
            if (NewPasswordBox.Password.Length < 6)
            {
                MessageBox.Show("Новый пароль должен содержать минимум 6 символов", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NewPasswordBox.Focus();
                return false;
            }
// проверка, что новый пароль и его подтверждение одинаковые 
            if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
            {
                MessageBox.Show("Пароли не совпадают", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ConfirmPasswordBox.Focus();
                return false;
            }

            return true;
        }
//управление состоянием UI 
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
                ChangeButton.Content = "Изменить пароль";
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