using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Linq;

namespace MessengerApp
{
    public partial class ProfileWindow : Window
    {
        public event EventHandler<UserProfile> ProfileUpdated;
        
        private readonly string _userId;
        private readonly string _idToken;
        private UserProfile _userProfile;
        private bool _isModified = false;
        private string _tempAvatarBase64 = null;
        private const string FIREBASE_URL = "https://messenger-cff09-default-rtdb.europe-west1.firebasedatabase.app";

        public ProfileWindow(string userId, string idToken, UserProfile profile = null)
        {
            InitializeComponent();
            
            _userId = userId;
            _idToken = idToken;
            _userProfile = profile ?? new UserProfile { UserId = userId };
            
            Loaded += ProfileWindow_Loaded;
        }

        private async void ProfileWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadProfileAsync();
        }

        private async Task LoadProfileAsync()
        {
            try
            {
                SetLoading(true);
                StatusText.Text = "Загрузка профиля...";

                // Загружаем профиль из Firebase
                var profileUrl = $"{FIREBASE_URL}/profiles/{_userId}.json";
                
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(profileUrl);
                var responseText = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode && responseText != "null")
                {
                    var savedProfile = JsonSerializer.Deserialize<UserProfile>(responseText);
                    if (savedProfile != null)
                    {
                        // Обновляем все поля профиля из сохраненных данных
                        _userProfile.UserId = _userId;
                        _userProfile.DisplayName = savedProfile.DisplayName ?? "Новый пользователь";
                        _userProfile.Bio = savedProfile.Bio ?? "";
                        _userProfile.StatusText = savedProfile.StatusText ?? "Доступен";
                        _userProfile.UpdatedAt = savedProfile.UpdatedAt;

                    }
                }
                else
                {
                    // Если профиля нет в Firebase, используем переданный или создаем новый
                    if (string.IsNullOrEmpty(_userProfile.DisplayName))
                    {
                        _userProfile.DisplayName = "Новый пользователь";
                    }
                    if (string.IsNullOrEmpty(_userProfile.Bio))
                    {
                        _userProfile.Bio = "";
                    }
                    if (string.IsNullOrEmpty(_userProfile.StatusText))
                    {
                        _userProfile.StatusText = "Доступен";
                    }
                    
                }
                
                // Загружаем аватар
                await LoadAvatarAsync();
                
                // Обновляем UI
                UpdateUI();
                
                SetLoading(false);
                StatusText.Text = "Профиль загружен";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки профиля: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetLoading(false);
            }
        }

        private async Task LoadAvatarAsync()
        {
            try
            {
                var avatarUrl = $"{FIREBASE_URL}/avatars/{_userId}.json";
                
                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(avatarUrl);
                var responseText = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode && responseText != "null")
                {
                    using var doc = JsonDocument.Parse(responseText);
                    if (doc.RootElement.TryGetProperty("base64", out var base64Element))
                    {
                        var base64String = base64Element.GetString();
                        if (!string.IsNullOrEmpty(base64String))
                        {
                            _tempAvatarBase64 = base64String;
                            _userProfile.AvatarBase64 = base64String;
                            
                            var bitmap = ConvertBase64ToBitmap(base64String);
                            if (bitmap != null)
                            {
                                AvatarImage.Source = bitmap;
                                return;
                            }
                        }
                    }
                }
                
                // Если аватара нет, показываем дефолтный
                AvatarImage.Source = CreateDefaultAvatar();
            }
            catch (Exception ex)
            {
                AvatarImage.Source = CreateDefaultAvatar();
            }
        }

        private void UpdateUI()
        {
            try
            {
                // Обновляем текстовые поля
                DisplayNameBox.Text = _userProfile.DisplayName ?? "";
                BioTextBox.Text = _userProfile.Bio ?? "";
                
                // Счетчик символов
                BioCounter.Text = $"{BioTextBox.Text.Length}/500";
                
                // Статус из комбобокса
                if (!string.IsNullOrEmpty(_userProfile.StatusText))
                {
                    var matchingItem = StatusComboBox.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(item => item.Tag?.ToString() == _userProfile.StatusText);
                    
                    if (matchingItem != null)
                    {
                        StatusComboBox.SelectedItem = matchingItem;
                    }
                    else
                    {
                        // Если статус не найден, выбираем первый доступный
                        StatusComboBox.SelectedIndex = 0;
                    }
                }
                else
                {
                    StatusComboBox.SelectedIndex = 0;
                }
                
                // Инициализируем аватар если еще не загружен
                if (AvatarImage.Source == null)
                {
                    AvatarImage.Source = CreateDefaultAvatar();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;

            try
            {
                SetLoading(true);
                StatusText.Text = "Сохранение...";

                // 1. Обновляем данные профиля из полей
                _userProfile.DisplayName = DisplayNameBox.Text.Trim();
                _userProfile.Bio = BioTextBox.Text.Trim();
                
                // Статус из комбобокса
                if (StatusComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string statusTag)
                {
                    _userProfile.StatusText = statusTag;
                }
                else
                {
                    _userProfile.StatusText = "Доступен";
                }
                
                // Время обновления
                _userProfile.UpdatedAt = DateTime.UtcNow;

                bool allSaved = true;

                // 2. Сохраняем профиль (имя, описание и статус) В ПЕРВУЮ ОЧЕРЕДЬ
                var profileSaved = await SaveProfileToFirebaseAsync();
                
                if (!profileSaved)
                {
                    allSaved = false;
                    MessageBox.Show("Не удалось сохранить профиль", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // 3. Сохраняем аватар если он был изменен
                if (allSaved && _tempAvatarBase64 != null && _tempAvatarBase64 != _userProfile.AvatarBase64)
                {
                    var avatarSaved = await SaveAvatarToFirebaseAsync(_tempAvatarBase64);
                    
                    if (avatarSaved)
                    {
                        _userProfile.AvatarBase64 = _tempAvatarBase64;
                    }
                }

                if (allSaved)
                {
                    // Обновляем аватар в главном окне
                    var mainWindow = Application.Current.Windows
                        .OfType<MessengerWindow>()
                        .FirstOrDefault();
                    
                    if (mainWindow != null && _tempAvatarBase64 != null)
                    {
                        mainWindow.UpdateAvatar(_tempAvatarBase64);
                    }
                    
                    // Обновляем имя и статус в главном окне
                    if (mainWindow != null)
                    {
                        mainWindow.UpdateProfileInfo(_userProfile.DisplayName, _userProfile.StatusText);
                    }
                    
                    // Вызываем событие обновления профиля
                    ProfileUpdated?.Invoke(this, _userProfile);
                    
                    MessageBox.Show("Профиль сохранен успешно!", "Успех", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    DialogResult = true;
                    Close();
                }
                
                SetLoading(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetLoading(false);
            }
        }

        private async Task<bool> SaveProfileToFirebaseAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                
                var url = $"{FIREBASE_URL}/profiles/{_userId}.json?auth={_idToken}";
                
                // Сохраняем данные профиля
                var profileData = new 
                {
                    userId = _userProfile.UserId,
                    displayName = _userProfile.DisplayName,
                    bio = _userProfile.Bio,
                    statusText = _userProfile.StatusText,
                    updatedAt = _userProfile.UpdatedAt.ToString("o")
                };
                
                var json = JsonSerializer.Serialize(profileData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await httpClient.PutAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {

                    return true;
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {

                return false;
            }
        }

        private async Task<bool> SaveAvatarToFirebaseAsync(string base64Image)
        {
            try
            {
                using var httpClient = new HttpClient();
                
                var url = $"{FIREBASE_URL}/avatars/{_userId}.json?auth={_idToken}";
                
                var avatarData = new 
                { 
                    base64 = base64Image,
                    updatedAt = DateTime.UtcNow.ToString("o")
                };
                
                var json = JsonSerializer.Serialize(avatarData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await httpClient.PutAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        
        private async void ChangeAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Изображения (*.jpg;*.jpeg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif|Все файлы (*.*)|*.*",
                    Title = "Выберите изображение для аватара",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var filePath = openFileDialog.FileName;
                    var fileInfo = new FileInfo(filePath);
                    
                    if (fileInfo.Length > 5 * 1024 * 1024)
                    {
                        MessageBox.Show("Файл слишком большой. Максимальный размер - 5MB.", "Ошибка", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Конвертируем изображение в Base64
                    var base64Image = ConvertImageToBase64(filePath);
                    
                    if (string.IsNullOrEmpty(base64Image))
                    {
                        MessageBox.Show("Не удалось загрузить изображение", "Ошибка", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // Показываем превью
                    var bitmap = ConvertBase64ToBitmap(base64Image);
                    if (bitmap != null)
                    {
                        _tempAvatarBase64 = base64Image;
                        AvatarImage.Source = bitmap;
                        StatusText.Text = "Аватар изменен";
                        _isModified = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки аватара: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("Удалить аватар?", "Подтверждение", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    _tempAvatarBase64 = "";
                    _userProfile.AvatarBase64 = null;
                    AvatarImage.Source = CreateDefaultAvatar();
                    _isModified = true;
                    StatusText.Text = "Аватар удален";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления аватара: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ExportDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = $"messenger_profile_{DateTime.Now:yyyyMMdd}.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(_userProfile, 
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveFileDialog.FileName, json);
                    
                    MessageBox.Show("Данные профиля экспортированы успешно!", "Успех", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Вы уверены, что хотите удалить аккаунт?\n\n" +
                "Это действие нельзя отменить. Все ваши данные будут удалены.",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    MessageBox.Show("Удаление аккаунта временно недоступно. Обратитесь к администратору.", 
                        "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления аккаунта: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool ValidateInput()
        {
            // Проверка имени
            var displayName = DisplayNameBox.Text?.Trim() ?? "";
            
            if (string.IsNullOrWhiteSpace(displayName))
            {
                MessageBox.Show("Введите имя", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DisplayNameBox.Focus();
                return false;
            }
            
            if (displayName.Length < 2)
            {
                MessageBox.Show("Имя должно содержать минимум 2 символа", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DisplayNameBox.Focus();
                return false;
            }

            // Проверка описания
            var bio = BioTextBox.Text?.Trim() ?? "";
            if (bio.Length > 500)
            {
                MessageBox.Show("Описание не должно превышать 500 символов", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                BioTextBox.Focus();
                return false;
            }

            return true;
        }

        private void SetLoading(bool isLoading)
        {
            try
            {
                SaveButton.IsEnabled = !isLoading;
                CancelButton.IsEnabled = !isLoading;
        
                if (isLoading)
                {
                    SaveButton.Content = "Сохранение...";
                }
                else
                {
                    SaveButton.Content = "Сохранить";
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isModified)
            {
                var result = MessageBox.Show("Сохранить изменения перед выходом?", "Подтверждение", 
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    SaveButton_Click(sender, e);
                    return;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }
            
            DialogResult = false;
            Close();
        }
        

        private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _isModified = true;
        }

        private string ConvertImageToBase64(string imagePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(imagePath);
                return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
            }
            catch
            {
                return null;
            }
        }

        private BitmapImage ConvertBase64ToBitmap(string base64String)
        {
            try
            {
                var base64Data = base64String;
                if (base64String.Contains(","))
                {
                    base64Data = base64String.Split(',')[1];
                }
        
                var bytes = Convert.FromBase64String(base64Data);
                using var ms = new MemoryStream(bytes);
        
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = 150;
                bitmap.DecodePixelHeight = 150;
                bitmap.EndInit();
                bitmap.Freeze();
        
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private ImageSource CreateDefaultAvatar()
        {
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                var brush = new SolidColorBrush(Color.FromRgb(100, 149, 237));
                drawingContext.DrawEllipse(brush, null, new Point(75, 75), 75, 75);
                
                var displayName = _userProfile?.DisplayName ?? "?";
                var firstChar = displayName.Length > 0 
                    ? displayName[0].ToString().ToUpper() 
                    : "?";
                
                var formattedText = new FormattedText(
                    firstChar,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    50,
                    Brushes.White,
                    96);
                
                drawingContext.DrawText(formattedText, 
                    new Point(75 - formattedText.Width / 2, 75 - formattedText.Height / 2));
            }
            
            var bmp = new RenderTargetBitmap(150, 150, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(drawingVisual);
            bmp.Freeze();
            
            return bmp;
        }
    }
}
