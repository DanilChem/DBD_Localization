using DBD_Trans.Models;
using DBD_Trans.Services;
using DBD_Trans.ViewModels;
using DBD_Trans.Views;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace DBD_Trans
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Папка, где лежит .sln (и наши JSON-файлы)
            string dataDir = GetSolutionDirectory();

            var fileService = new JsonFileService();
            var errorStorage = new JsonErrorStorage(fileService, dataDir);      // вместо BaseDirectory
            var statusStorage = new JsonStatusStorage(dataDir);                 // вместо BaseDirectory
            var appSettings = new AppSettings();

            var mainVM = new MainViewModel(fileService, errorStorage, statusStorage, appSettings, dataDir);
            var mainWindow = new MainWindow();
            mainWindow.DataContext = mainVM;
            mainWindow.Show();
        }
        private static string GetSolutionDirectory()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !Directory.GetFiles(dir, "*.sln").Any())
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            // Если папка решения не найдена (например, после публикации) – fallback на папку с exe
            return dir ?? AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}