using System.Windows;
using EventView.ViewModels;

namespace EventView
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var dataContext = new MainWindowViewModel();
            var view = new MainWindow() { DataContext = dataContext };
            view.Show();
        }
    }
}