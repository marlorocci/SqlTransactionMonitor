using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SqlTransactionMonitor.ViewModels;

namespace SqlTransactionMonitor;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    public MainWindow()
    {
        Resources["InverseBooleanConverter"] = new InverseBooleanConverter();
        InitializeComponent();
        DataContext = new MainViewModel();
    }
    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = ((PasswordBox)sender).Password;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b && !b;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b && !b;
}
