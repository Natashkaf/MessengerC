using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace MessengerApp
{
    public partial class NotificationSettingsWindow : Window
    {
        public NotificationSettings Settings { get; private set; }
        private readonly SystemNotificationManager _notificationManager;
        private readonly MessengerWindow _mainWindow;

        public NotificationSettingsWindow(NotificationSettings settings)
        {
            InitializeComponent();
            Settings = settings ?? new NotificationSettings();
            DataContext = this;
            Loaded += NotificationSettingsWindow_Loaded;
        }

        public NotificationSettingsWindow(NotificationSettings settings, SystemNotificationManager notificationManager, MessengerWindow mainWindow)
            : this(settings)
        {
            _notificationManager = notificationManager;
            _mainWindow = mainWindow;
        }

        private void NotificationSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            UpdateMutedChatsCount();
        }

        private void LoadSettings()
        {
            try
            {
                // Устанавливаем выбранный звук в комбобокс
                foreach (ComboBoxItem item in SoundComboBox.Items)
                {
                    if (item.Tag?.ToString() == Settings.SoundName)
                    {
                        SoundComboBox.SelectedItem = item;
                        break;
                    }
                }

                // Если не нашли, выбираем первый
                if (SoundComboBox.SelectedItem == null && SoundComboBox.Items.Count > 0)
                {
                    SoundComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
            }
        }

        private void UpdateMutedChatsCount()
        {
            try
            {
                int mutedCount = Settings.MutedChats?.Count(kv => kv.Value) ?? 0;
                MutedChatsCount.Text = mutedCount.ToString();
            }
            catch
            {
                MutedChatsCount.Text = "0";
            }
        }

        private void TestNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Тестовое уведомление
                if (_notificationManager != null)
                {
                    _notificationManager.ShowNotification(
                        "Тестовое уведомление",
                        "Это тестовое уведомление с текущими настройками. Если вы его видите, значит уведомления работают правильно!",
                        "test_chat"
                    );
                }
                else
                {
                    // Локальный показ уведомления если менеджер не передан
                    MessageBox.Show("Тестовое уведомление сгенерировано! Проверьте системный трей.",
                        "Тест уведомлений", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка тестового уведомления: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Сохраняем выбранный звук
                if (SoundComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    Settings.SoundName = selectedItem.Tag?.ToString() ?? "default";
                }

                // Обновляем настройки в менеджере уведомлений
                _notificationManager?.UpdateSettings(Settings);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения настроек: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        



    }
}