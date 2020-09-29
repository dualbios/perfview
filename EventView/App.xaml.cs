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
            //services.AddSingleton<MainWindowViewModel, MainWindowViewModel>();
            services.AddSingleton<IDialogPlaceHolder, MainWindowViewModel>(x=> GetMainWindowViewModel(x.GetService<IFileFormatFactory>()));

            serviceProvider = services.BuildServiceProvider();

            IFileFormatFactory fileFormatFactory = serviceProvider.GetRequiredService<IFileFormatFactory>();
            var dataContext = GetMainWindowViewModel(fileFormatFactory);
            var view = new MainWindow() { DataContext = dataContext };
            view.Show();
        }

        private MainWindowViewModel mainWindowViewModel = null;
        private MainWindowViewModel GetMainWindowViewModel(IFileFormatFactory fileFormatFactory)
        {
            if (mainWindowViewModel == null)
            {
                mainWindowViewModel = new MainWindowViewModel(fileFormatFactory);
            }

            return mainWindowViewModel;
        }
    }
}