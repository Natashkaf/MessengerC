using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MessengerApp
{
    public partial class SelectContactWindow : Window
    {
        public Contact SelectedContact { get; private set; }
        private ObservableCollection<Contact> _allContacts;
        
        public SelectContactWindow(ObservableCollection<Contact> contacts)
        {
            InitializeComponent();
            
            // Фильтруем контакты 
            _allContacts = new ObservableCollection<Contact>(
                contacts.Where(c => c != null).ToList());
            
            ContactsList.ItemsSource = _allContacts;
            
            // Показываем/скрываем сообщение "нет контактов"
            NoContactsPanel.Visibility = _allContacts.Count == 0 
                ? Visibility.Visible 
                : Visibility.Collapsed;
            ContactsList.Visibility = _allContacts.Count == 0 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }
        
        private void ContactItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is Contact contact)
            {
                SelectedContact = contact;
                DialogResult = true;
                Close();
            }
        }
        
        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContactsList.SelectedItem is Contact selected)
            {
                SelectedContact = selected;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Выберите контакт", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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