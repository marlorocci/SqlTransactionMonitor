using System.Windows;
using System.Windows.Controls;
using SqlTransactionMonitor.ViewModels;

namespace SqlTransactionMonitor
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel =>
            (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = new MainViewModel();
        }

        private void PasswordBox_OnPasswordChanged(
            object sender,
            RoutedEventArgs e)
        {
            ViewModel.Password =
                ((PasswordBox)sender).Password;
        }
    }
}