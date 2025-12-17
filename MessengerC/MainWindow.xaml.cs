
using System;
using System.Windows;
using System.Windows.Input;

namespace MessengerApp
{
    public partial class MainWindow : Window
    {
        private bool _isSigningIn = false;
        private bool _isInitialized = false;

        public MainWindow()
        {
            InitializeComponent();
            StatusText.Text = "";
            _isInitialized = true;
            UpdateSignInButtonState();
        }

        private void OnInputChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            UpdateSignInButtonState();
        }

        private void UpdateSignInButtonState()
        {
            // Проверяем, что кнопка уже инициализирована
            if (SignInButton == null) return;
            
            bool hasEmail = !string.IsNullOrWhiteSpace(EmailBox.Text);
            bool hasPassword = !string.IsNullOrWhiteSpace(PasswordBox.Password);
            SignInButton.IsEnabled = hasEmail && hasPassword && !_isSigningIn;
        }

        private async void OnSignInClick(object sender, RoutedEventArgs e)
        {
            if (_isSigningIn) return;

            try
            {
                _isSigningIn = true;
                SetLoadingState(true);
                StatusText.Text = "Вход...";
                UpdateSignInButtonState(); // Обновляем состояние кнопки

                var auth = new FirebaseRestAuth();
                var response = await auth.SignInAsync(EmailBox.Text.Trim(), PasswordBox.Password);

                // Получаем информацию о пользователе
                var userInfo = await auth.GetUserInfoAsync(response.IdToken);
                
                if (userInfo == null)
                {
                    MessageBox.Show("Не удалось получить информацию о пользователе", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Проверяем, подтвержден ли email
                if (!userInfo.EmailVerified)
                {
                    var result = MessageBox.Show(
                        "Ваш email не подтвержден.\n\n" +
                        "Хотите отправить письмо для подтверждения?",
                        "Подтверждение email",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        await auth.SendPasswordResetEmailAsync(userInfo.Email);
                        MessageBox.Show("Письмо для подтверждения отправлено на ваш email.", 
                            "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                // Успешный вход
                OpenMessengerWindow(userInfo.Email, userInfo.LocalId, response.IdToken, userInfo.DisplayName);
            }
            catch (FirebaseAuthException ex)
            {
                ShowError(ex.Message);
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка входа: {ex.Message}");
            }
            finally
            {
                _isSigningIn = false;
                SetLoadingState(false);
                UpdateSignInButtonState(); // Обновляем состояние кнопки
            }
        }

        private void OnSignUpClick(object sender, RoutedEventArgs e)
        {
            var registrationWindow = new RegistrationWindow();
            registrationWindow.Owner = this;
            registrationWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            registrationWindow.RegistrationCompleted += OnRegistrationCompleted;
            registrationWindow.ShowDialog();
        }

private async void OnGoogleSignInClick(object sender, RoutedEventArgs e)
{
    if (_isSigningIn) return;

    try
    {
        _isSigningIn = true;
        SetLoadingState(true);
        StatusText.Text = "Авторизация Google...";

        //Получаем токен от Google
        string googleIdToken = null;
        GoogleAuthWindow googleWindow = null;
        
        try
        {
           
            googleWindow = new GoogleAuthWindow();
            googleWindow.Owner = this;
            googleWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            
          
            googleWindow.Show();
            
           
            googleIdToken = await googleWindow.GetIdTokenAsync();
        }
        finally
        {
            googleWindow?.Close();
        }

        if (string.IsNullOrEmpty(googleIdToken))
        {
           
            StatusText.Text = "Авторизация отменена";
            return;
        }
        
        //  Авторизация в Firebase
        var auth = new FirebaseRestAuth();
        var response = await auth.SignInWithGoogleAsync(googleIdToken);

        if (response == null)
        {
            throw new Exception("Firebase не вернул ответ");
        }

        if (string.IsNullOrEmpty(response.IdToken))
        {
            throw new Exception("Firebase не вернул токен");
        }

        //  Получаем информацию о пользователе
        var userInfo = await auth.GetUserInfoAsync(response.IdToken);

        if (userInfo == null)
        {
            throw new Exception("Не удалось получить информацию о пользователе");
        }
        
        if (string.IsNullOrEmpty(userInfo.Email))
        {
            throw new Exception("Email пользователя не получен");
        }

        if (string.IsNullOrEmpty(userInfo.LocalId))
        {
            throw new Exception("ID пользователя не получен");
        }

        // Открываем окно мессенджера
        OpenMessengerWindow(
            userInfo.Email, 
            userInfo.LocalId, 
            response.IdToken, 
            userInfo.DisplayName);
    }
    catch (TaskCanceledException)
    {

        StatusText.Text = "Авторизация отменена";
    }
    catch (Exception ex)
    {
        
        ShowError($"Ошибка входа через Google: {ex.Message}");
    }
    finally
    {
        _isSigningIn = false;
        SetLoadingState(false);
    }
}
        private async void OnForgotPasswordClick(object sender, MouseButtonEventArgs e)
        {
            var email = EmailBox.Text.Trim();
            
            if (string.IsNullOrEmpty(email))
            {
                MessageBox.Show("Введите ваш email для восстановления пароля", 
                    "Восстановление пароля", MessageBoxButton.OK, MessageBoxImage.Information);
                EmailBox.Focus();
                return;
            }

            try
            {
                var auth = new FirebaseRestAuth();
                await auth.SendPasswordResetEmailAsync(email);
                
                MessageBox.Show($"Письмо для восстановления пароля отправлено на {email}\n\n" +
                              "Пожалуйста, проверьте вашу почту и следуйте инструкциям в письме.",
                              "Письмо отправлено", 
                              MessageBoxButton.OK, 
                              MessageBoxImage.Information);
            }
            catch (FirebaseAuthException ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки письма: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenMessengerWindow(string email, string userId, string idToken, string? displayName = null)
        {
            try
            {

                if (string.IsNullOrEmpty(email))
                {
                    throw new ArgumentNullException(nameof(email));
                }
        
                if (string.IsNullOrEmpty(userId))
                {
                    throw new ArgumentNullException(nameof(userId));
                }
        
                if (string.IsNullOrEmpty(idToken))
                {
                    throw new ArgumentNullException(nameof(idToken));
                }

               
                var messengerWindow = new MessengerWindow(email, userId, idToken);
                
                messengerWindow.Show();
                
                this.Close();
            }
            catch (Exception ex)
            {

        
                MessageBox.Show($"Ошибка открытия окна мессенджера: {ex.Message}", 
                    "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        
                // Не закрываем окно входа при ошибке
                _isSigningIn = false;
                SetLoadingState(false);
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            if (ProgressBar != null)
                ProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            
            if (StatusText != null)
                StatusText.Text = isLoading ? StatusText.Text : "";
            
            UpdateSignInButtonState();
        }

        private void ShowError(string message)
        {
            if (StatusText != null)
                StatusText.Text = message;
            
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        //  обработчик загрузки окна для дополнительной инициализации
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSignInButtonState();
        }
        private void OnRegistrationCompleted(object? sender, RegistrationCompletedEventArgs e)
        {
            if (e.RequiresPassword)
            {
                // Показываем сообщение, что нужно войти
                MessageBox.Show($"Аккаунт с email {e.Email} уже существует.\n\n" +
                                "Пожалуйста, войдите с вашим паролем.",
                    "Вход в существующий аккаунт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
        
                // Автоматически заполняем поле email
                EmailBox.Text = e.Email;
                PasswordBox.Focus();
            }
            else
            {
                // Обычная регистрация - автоматический вход
                TryAutoLoginAfterRegistration(e);
            }
        }

        private async void TryAutoLoginAfterRegistration(RegistrationCompletedEventArgs e)
        {
            try
            {
                SetLoadingState(true);
                StatusText.Text = "Автоматический вход...";

                var auth = new FirebaseRestAuth();
                var response = await auth.SignInAsync(e.Email, PasswordBox.Password);

                OpenMessengerWindow(e.Email, e.UserId, response.IdToken, e.DisplayName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Автоматический вход не удался: {ex.Message}\nПопробуйте войти вручную.", 
                    "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetLoadingState(false);
            }
        }
    }
}