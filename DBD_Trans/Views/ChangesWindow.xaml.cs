using DBD_Trans.Helpers;
using DBD_Trans.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace DBD_Trans.Views
{
    public partial class ChangesWindow : Window
    {
        private ChangesViewModel ViewModel => DataContext as ChangesViewModel;

        public ChangesWindow()
        {
            InitializeComponent();

            this.SourceInitialized += (s, e) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SendMessage(handle, 0x80, IntPtr.Zero, IntPtr.Zero);
                SendMessage(handle, 0x80, IntPtr.Zero, new IntPtr(1));
            };

            DarkTitleBarHelper.ApplyDarkTitleBar(this);

            this.Loaded += (s, e) =>
            {
                if (ViewModel != null)
                    ViewModel.RequestClose += OnRequestClose;
            };

            this.Closed += (s, e) =>
            {
                if (ViewModel != null)
                    ViewModel.RequestClose -= OnRequestClose;
            };
        }

        private void OnRequestClose()
        {
            Close();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}
