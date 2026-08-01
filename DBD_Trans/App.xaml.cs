using DBD_Trans.Models;
using DBD_Trans.Services;
using DBD_Trans.ViewModels;
using DBD_Trans.Views;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace DBD_Trans
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string dataDir = GetSolutionDirectory();
            var fileService = new JsonFileService();
            var errorStorage = new JsonErrorStorage(fileService, dataDir);
            var statusStorage = new JsonStatusStorage(dataDir);
            var mergeStorage = new JsonMergeStorage(fileService, dataDir); // <-- НОВОЕ
            var changeHistoryStorage = new JsonChangeHistoryStorage(fileService, dataDir); // <-- НОВОЕ: история изменений строк
            var appSettings = new AppSettings();

            var mainVM = new MainViewModel(fileService, errorStorage, statusStorage, appSettings, dataDir, mergeStorage, changeHistoryStorage);
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
            return dir ?? AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
