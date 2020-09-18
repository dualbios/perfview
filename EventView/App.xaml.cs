using System.Windows;
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

            serviceProvider = CreateServices();

            var dataContext = serviceProvider.GetService<MainWindowViewModel>();
            var view = new MainWindow() { DataContext = dataContext };
            view.Show();
        }

        private ServiceProvider CreateServices()
        {
            services.AddSingleton<MainWindowViewModel, MainWindowViewModel>();

            services.AddSingleton<IFileFormatFactory, FileFormatFactory>();

            services.AddSingleton<IFileFormat, FileFormats.EtlPerf.ETLPerfFileFormat>();
            services.AddSingleton<FileFormats.EtlPerf.IEtlPerfPartFactory, FileFormats.EtlPerf.EtlPerfPartFactory>();

            services.AddSingleton<FileFormats.EtlPerf.IEtlFilePart, FileFormats.EtlPerf.PerfViewTraceInfo>();
            services.AddSingleton<FileFormats.EtlPerf.IEtlFilePart, FileFormats.EtlPerf.PerfViewProcesses>();
            services.AddSingleton<FileFormats.EtlPerf.IEtlFilePart, FileFormats.EtlPerf.PerfViewEventStats>();
            services.AddSingleton<FileFormats.EtlPerf.IEtlFilePart, FileFormats.EtlPerf.PerfViewEventSource>();

            return services.BuildServiceProvider();
        }
    }
}