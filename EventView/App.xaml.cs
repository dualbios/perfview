using System.Windows;
using EventView.Dialogs;
using EventView.FileFormats;
using EventView.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EventView
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider serviceProvider;
        private ServiceCollection services = new ServiceCollection();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            services.AddSingleton<IFileFormat, FileFormats.EtlPerf.ETLPerfFileFormat>();
            services.AddSingleton<IFileFormatFactory, FileFormatFactory>();
            serviceProvider = services.BuildServiceProvider();

            IFileFormatFactory fileFormatFactory = serviceProvider.GetRequiredService<IFileFormatFactory>();
            var dataContext = new MainWindowViewModel(fileFormatFactory);

            var view = new MainWindow() { DataContext = dataContext };
            view.Show();
        }

    }
}