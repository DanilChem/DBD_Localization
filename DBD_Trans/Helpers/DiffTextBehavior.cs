using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DBD_Trans.Helpers
{
    /// <summary>
    /// TextBlock.Inlines не является обычным DependencyProperty, поэтому список сегментов
    /// diff'а нельзя просто забиндить через {Binding}. Этот attached property решает это:
    /// при изменении списка сегментов пересобирает Inlines заново, окрашивая убранные слова
    /// красным зачёркнутым, а добавленные — акцентным цветом с полужирным начертанием.
    /// </summary>
    public static class DiffTextBehavior
    {
        private static readonly SolidColorBrush RemovedBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x6A));
        private static readonly SolidColorBrush RemovedBg = new SolidColorBrush(Color.FromArgb(0x40, 0xE2, 0x4A, 0x4A));
        private static readonly SolidColorBrush AddedBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0xE0, 0x94));
        private static readonly SolidColorBrush AddedBg = new SolidColorBrush(Color.FromArgb(0x40, 0x4C, 0xC9, 0x7A));

        static DiffTextBehavior()
        {
            RemovedBrush.Freeze();
            RemovedBg.Freeze();
            AddedBrush.Freeze();
            AddedBg.Freeze();
        }

        public static readonly DependencyProperty SegmentsProperty =
            DependencyProperty.RegisterAttached(
                "Segments",
                typeof(IEnumerable<DiffSegment>),
                typeof(DiffTextBehavior),
                new PropertyMetadata(null, OnSegmentsChanged));

        public static void SetSegments(DependencyObject element, IEnumerable<DiffSegment> value) =>
            element.SetValue(SegmentsProperty, value);

        public static IEnumerable<DiffSegment> GetSegments(DependencyObject element) =>
            (IEnumerable<DiffSegment>)element.GetValue(SegmentsProperty);

        private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock textBlock)) return;

            textBlock.Inlines.Clear();

            if (!(e.NewValue is IEnumerable<DiffSegment> segments)) return;

            foreach (var segment in segments)
            {
                var run = new Run(segment.Text);
                switch (segment.Type)
                {
                    case DiffSegmentType.Removed:
                        run.Foreground = RemovedBrush;
                        run.Background = RemovedBg;
                        run.TextDecorations = TextDecorations.Strikethrough;
                        break;
                    case DiffSegmentType.Added:
                        run.Foreground = AddedBrush;
                        run.Background = AddedBg;
                        run.FontWeight = FontWeights.SemiBold;
                        break;
                    default:
                        // Неизменившийся текст — наследует Foreground самого TextBlock
                        break;
                }
                textBlock.Inlines.Add(run);
            }
        }
    }
}
