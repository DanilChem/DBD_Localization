using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DBD_Trans.Views
{
    public partial class AnalysisWindow : Window
    {
        private AnalysisViewModel ViewModel => (AnalysisViewModel)DataContext;

        public AnalysisWindow()
        {
            InitializeComponent();
            Loaded += AnalysisWindow_Loaded;
            PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;
            DarkTitleBarHelper.ApplyDarkTitleBar(this);
        }

        private void AnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.EnglishRichTextBox = EnglishRichTextBox;
            ViewModel.RussianRichTextBox = RussianRichTextBox;
            ViewModel.InitializeDocuments();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ViewModel.OnClosing();
            base.OnClosing(e);
        }

        private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (FindParentOfType<ListBox>(source) == ErrorsListBox)
                return;
            if (FindParentOfType<Button>(source) != null ||
                FindParentOfType<TextBox>(source) != null ||
                FindParentOfType<RichTextBox>(source) != null ||
                FindParentOfType<ScrollBar>(source) != null ||
                FindParentOfType<Thumb>(source) != null)
                return;

            ViewModel.SelectedError = null;
            e.Handled = true;
        }

        private static T FindParentOfType<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                DependencyObject parent = LogicalTreeHelper.GetParent(child);
                if (parent == null)
                {
                    if (child is Visual || child is Visual3D)
                        parent = VisualTreeHelper.GetParent(child);
                    else
                        break;
                }
                if (parent is T typedParent)
                    return typedParent;
                child = parent;
            }
            return null;
        }

        private void ErrorsListBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(ErrorsListBox, e.GetPosition(ErrorsListBox));
            if (hit?.VisualHit is FrameworkElement element)
            {
                var item = FindParentOfType<ListBoxItem>(element);
                if (item == null)
                    ViewModel.SelectedError = null;
            }
        }

        private void ErrorsListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is ErrorItem errorItem)
            {
                ViewModel.EditErrorCommand.Execute(errorItem);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new System.Action(() =>
                    {
                        var textBox = FindVisualChild<TextBox>(item);
                        textBox?.Focus();
                        textBox?.SelectAll();
                    }));
                e.Handled = true;
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void EditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
            {
                errorItem.IsEditing = false;
                if (string.IsNullOrWhiteSpace(errorItem.Text))
                    ViewModel.DeleteErrorCommand.Execute(errorItem);
                else
                    ViewModel.SaveChanges();
            }
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                {
                    errorItem.IsEditing = false;
                    if (string.IsNullOrWhiteSpace(errorItem.Text))
                        ViewModel.DeleteErrorCommand.Execute(errorItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                    errorItem.IsEditing = false;
                e.Handled = true;
            }
        }

        private void NewErrorTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.AddErrorCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void RichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                TextPointer pos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
                if (pos != null)
                {
                    ViewModel.RemoveHighlightAtPosition(rtb, pos);
                    e.Handled = true;
                }
            }
        }

        private void RichTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;
            
            if (!rtb.Selection.IsEmpty)
            {
                ViewModel.ApplyMarkerToSelection(rtb);
                Mouse.Capture(null);
                e.Handled = true;
                rtb.Selection.Select(rtb.Selection.Start, rtb.Selection.Start);
            }
        }

        private void RichTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
        }
    }
}