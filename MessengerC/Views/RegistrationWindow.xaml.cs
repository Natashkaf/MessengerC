using System;
using System.Windows;
using System.Windows.Controls;

namespace MessengerApp
{
    public partial class RegistrationWindow : Window
    {
        public event EventHandler<RegistrationCompletedEventArgs>? RegistrationCompleted;

        public RegistrationWindow()
        {
            InitializeComponent();
        }

private async void OnRegisterClick(object sender, RoutedEventArgs e)
{
    if (!ValidateInput())
        return;

    try
    {
        RegisterButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        StatusText.Text = "Проверка...";

        var auth = new FirebaseRestAuth();
        var email = EmailBox.Text.Trim();
        
        //Существует ли уже неподтвержденный аккаунт
        var existingUser = await auth.GetUnverifiedUserAsync(email);
        
        if (existingUser != null)
        {
            // Аккаунт существует и не подтвержден
            var result = MessageBox.Show(
                "Аккаунт с этим email уже существует, но email не подтвержден.\n\n" +
                "Выберите действие:\n" +
                "1. Войти в существующий аккаунт\n" +
                "2. Отправить письмо для подтверждения повторно\n" +
                "3. Удалить старый аккаунт и создать новый",
                "Аккаунт уже существует",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) // "Войти"
            {
                // Показываем окно входа для этого email
                RegistrationCompleted?.Invoke(this, new RegistrationCompletedEventArgs
                {
                    Email = email,
                    RequiresPassword = true // Нужен будет пароль
                });
                Close();
                return;
            }
            else if (result == MessageBoxResult.No) // "Отправить письмо"
            {
                try
                {
                    // Пытаемся войти, чтобы получить idToken для отправки письма
                    var signInResponse = await auth.SignInAsync(email, PasswordBox.Password);
                    await auth.ResendEmailVerificationAsync(signInResponse.IdToken);
                    
                    MessageBox.Show("Письмо для подтверждения отправлено повторно!\n\n" +
                                  "Пожалуйста, проверьте вашу почту.",
                                  "Письмо отправлено",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
                catch (FirebaseAuthException ex)
                {
                    MessageBox.Show($"Не удалось отправить письмо: {ex.Message}\n\n" +
                                  "Возможно, вы ввели неправильный пароль для существующего аккаунта.",
                                  "Ошибка",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
                Close();
                return;
            }
            else if (result == MessageBoxResult.Cancel) // "Удалить и создать новый"
            {
                try
                {
                    await auth.DeleteUnverifiedAccountAsync(email, PasswordBox.Password);
                    MessageBox.Show("Старый аккаунт удален. Теперь можно создать новый.",
                                  "Аккаунт удален",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                    
                    // Продолжаем регистрацию ниже
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось удалить аккаунт: {ex.Message}",
                                  "Ошибка",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                return; // Пользователь закрыл окно
            }
        }

        // Регистрация нового пользователя
        StatusText.Text = "Регистрация...";
        
        var response = await auth.CreateUserAsync(
            email: email,
            password: PasswordBox.Password,
            displayName: NameBox.Text.Trim()
        );


        
        MessageBox.Show(
            "Регистрация успешна!\n\n" +
            "На ваш email отправлено письмо для подтверждения.\n" +
            "Пожалуйста, проверьте вашу почту и перейдите по ссылке.\n\n" +
            "После подтверждения все функции будут доступны.",
            "Успешно",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        // Обновляем профиль
        if (!string.IsNullOrEmpty(NameBox.Text.Trim()))
        {
            try
            {
                await auth.UpdateProfileAsync(
                    response.IdToken,
                    NameBox.Text.Trim(),
                    null
                );
            }
            catch { /*ваще пофек на ошибки  */ }
        }

        // Автоматический вход после регистрации
        RegistrationCompleted?.Invoke(this, new RegistrationCompletedEventArgs
        {
            Email = response.Email,
            UserId = response.LocalId,
            IdToken = response.IdToken,
            DisplayName = NameBox.Text.Trim()
        });

        Close();
    }
    catch (FirebaseAuthException ex)
    {
        if (ex.FirebaseErrorCode == "EMAIL_EXISTS")
        {
            ShowError("Пользователь с таким email уже существует и подтвержден.\nПопробуйте войти или используйте другой email.");
        }
        else
        {
            ShowError(ex.Message);
        }
    }
    catch (Exception ex)
    {
        ShowError($"Ошибка регистрации: {ex.Message}");
    }
    finally
    {
        RegisterButton.IsEnabled = true;
        ProgressBar.Visibility = Visibility.Collapsed;
    }
}

        private bool ValidateInput()
        {
            // Очистка предыдущих ошибок
            EmailError.Text = "";
            PasswordError.Text = "";
            ConfirmPasswordError.Text = "";
            NameError.Text = "";

            bool isValid = true;

            // Проверка email
            if (string.IsNullOrWhiteSpace(EmailBox.Text))
            {
                EmailError.Text = "Введите email";
                isValid = false;
            }
            else if (!IsValidEmail(EmailBox.Text))
            {
                EmailError.Text = "Неверный формат email";
                isValid = false;
            }

            // Проверка пароля
            if (string.IsNullOrWhiteSpace(PasswordBox.Password))
            {
                PasswordError.Text = "Введите пароль";
                isValid = false;
            }
            else if (PasswordBox.Password.Length < 6)
            {
                PasswordError.Text = "Пароль должен содержать минимум 6 символов";
                isValid = false;
            }

            // Проверка подтверждения пароля
            if (string.IsNullOrWhiteSpace(ConfirmPasswordBox.Password))
            {
                ConfirmPasswordError.Text = "Подтвердите пароль";
                isValid = false;
            }
            else if (PasswordBox.Password != ConfirmPasswordBox.Password)
            {
                ConfirmPasswordError.Text = "Пароли не совпадают";
                isValid = false;
            }

            // Проверка имени (необязательно)
            if (!string.IsNullOrWhiteSpace(NameBox.Text) && NameBox.Text.Length < 2)
            {
                NameError.Text = "Имя должно содержать минимум 2 символа";
                isValid = false;
            }

            return isValid;
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private void ShowError(string message)
        {
            StatusText.Text = message;
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordStrength();
        }

        private void UpdatePasswordStrength()
        {
            var password = PasswordBox.Password;
            
            if (string.IsNullOrEmpty(password))
            {
                PasswordStrengthText.Text = "";
                return;
            }

            // Простая проверка сложности пароля
            bool hasLower = false, hasUpper = false, hasDigit = false, hasSpecial = false;
            
            foreach (char c in password)
            {
                if (char.IsLower(c)) hasLower = true;
                else if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsLetterOrDigit(c)) hasSpecial = true;
            }

            int score = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            
            PasswordStrengthText.Text = score switch
            {
                1 => "Слабый",
                2 => "Средний",
                3 => "Хороший",
                4 => "Отличный",
                _ => "Очень слабый"
            };
        }
    }

    public class RegistrationCompletedEventArgs : EventArgs
    {
        public string Email { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string IdToken { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public bool RequiresPassword { get; set; } = false;
    }
}