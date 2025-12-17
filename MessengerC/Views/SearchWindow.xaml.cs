using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MessengerApp
{
    public partial class SearchWindow : Window
    {
        public event EventHandler<UserProfile> UserSelected;
        
        private readonly string _userId;
        private readonly string _idToken;
        private readonly ChatService _chatService;
        
        public SearchWindow(string userId, string idToken, ChatService chatService)
        {
            InitializeComponent();
            
            _userId = userId;
            _idToken = idToken;
            _chatService = chatService;
            
            Loaded += SearchWindow_Loaded;
        }
        
        private async void SearchWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Фокус на поле поиска при загрузке
            SearchTextBox.Focus();
            
            // Можно показать недавние контакты при загрузке
            await LoadRecentContacts();
        }
        
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = SearchTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(searchText))
            {
                await LoadRecentContacts();
                return;
            }
            
            if (searchText.Length < 2)
                return;
            
            await SearchUsersAsync(searchText);
        }
        
        private async Task SearchUsersAsync(string query)
        {
            try
            {
                ShowLoading(true);
                
                var users = await _chatService.SearchUsersAsync(query);
                
                Dispatcher.Invoke(() =>
                {
                    UsersList.ItemsSource = FormatUsersForDisplay(users);
                    
                    // Показываем/скрываем сообщение "не найдено"
                    if (users.Count == 0)
                    {
                        NoResultsPanel.Visibility = Visibility.Visible;
                        UsersList.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        NoResultsPanel.Visibility = Visibility.Collapsed;
                        UsersList.Visibility = Visibility.Visible;
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка поиска: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }
        
        private async Task LoadRecentContacts()
        {
            try
            {
                // Загружаем последние чаты как "недавние контакты"
                var chats = await _chatService.GetUserChatsAsync();
                
                Dispatcher.Invoke(() =>
                {
                    var displayList = new List<UserProfile>();
                    
                    foreach (var chat in chats)
                    {
                        displayList.Add(new UserProfile
                        {
                            UserId = chat.Participant1Id == _userId 
                                ? chat.Participant2Id 
                                : chat.Participant1Id,
                            DisplayName = chat.OtherUserName,
                            StatusText = chat.OtherUserStatus
                        });
                    }
                    
                    UsersList.ItemsSource = FormatUsersForDisplay(displayList);
                    
                    if (chats.Count == 0)
                    {
                        NoResultsPanel.Visibility = Visibility.Visible;
                        UsersList.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        NoResultsPanel.Visibility = Visibility.Collapsed;
                        UsersList.Visibility = Visibility.Visible;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($" {ex.Message}");
            }
        }
        
        private List<object> FormatUsersForDisplay(List<UserProfile> users)
        {
            var result = new List<object>();
            
            foreach (var user in users)
            {
                // Создаем объект для отображения
                var displayItem = new
                {
                    UserId = user.UserId,
                    DisplayName = user.DisplayName,
                    StatusText = user.StatusText ?? "онлайн",
                    Initials = GetInitials(user.DisplayName)
                };
                
                result.Add(displayItem);
            }
            
            return result;
        }
        
        private string GetInitials(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return "??";
            
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpper();
            }
            
            return displayName.Length >= 2 
                ? displayName.Substring(0, 2).ToUpper() 
                : displayName.ToUpper();
        }
        
        private void UserItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext != null)
            {
                // Получаем данные пользователя
                var userData = GetUserDataFromContext(border.DataContext);
                
                if (userData != null)
                {
                    var userProfile = new UserProfile
                    {
                        UserId = userData.UserId,
                        DisplayName = userData.DisplayName
                    };
                    
                    // Вызываем событие выбора пользователя
                    UserSelected?.Invoke(this, userProfile);
                    
                    // Закрываем окно поиска
                    DialogResult = true;
                    Close();
                }
            }
        }
        
        private dynamic GetUserDataFromContext(object dataContext)
        {
            // Используем рефлексию для получения данных
            var type = dataContext.GetType();
            var userIdProp = type.GetProperty("UserId");
            var displayNameProp = type.GetProperty("DisplayName");
            
            if (userIdProp != null && displayNameProp != null)
            {
                return new
                {
                    UserId = userIdProp.GetValue(dataContext) as string,
                    DisplayName = displayNameProp.GetValue(dataContext) as string
                };
            }
            
            return null;
        }
        
        private void ShowLoading(bool isLoading)
        {
            Dispatcher.Invoke(() =>
            {
                LoadingPanel.Visibility = isLoading 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
                
                SearchTextBox.IsEnabled = !isLoading;
            });
        }
        
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            
            base.OnKeyDown(e);
        }
    }
}