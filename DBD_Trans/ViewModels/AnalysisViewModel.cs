using DBD_Trans.Base;
using DBD_Trans.Models;
using DBD_Trans.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DBD_Trans.ViewModels
{
    public class AnalysisViewModel : ObservableObject
    {
        private readonly IErrorStorage _errorStorage;
        private readonly IAppSettings _appSettings;
        private readonly string _key;
        private readonly IStatusStorage _statusStorage;
        private readonly LocalizationEntry _entry;

        public bool IsCompleted
        {
            get => _entry.Status == ItemStatus.Completed;
            set
            {
                if (_entry.Status == ItemStatus.Completed != value)
                {
                    _entry.Status = value ? ItemStatus.Completed : ItemStatus.InProgress;
                    _statusStorage.SetStatus(_key, _entry.Status);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CompletedButtonBrush));
                }
            }
        }

        public bool HasErrors => Errors.Count > 0;

        public Brush CompletedButtonBrush
        {
            get
            {
                if (IsCompleted && HasErrors)
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF750000")); // красный
                if (IsCompleted && !HasErrors)
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF004A7C")); // синий
                return new SolidColorBrush(Colors.Transparent);
            }
        }
        public ICommand ToggleCompletedCommand { get; }

        public IEnumerable<ItemStatus> StatusOptions { get; }
        = Enum.GetValues(typeof(ItemStatus)).Cast<ItemStatus>();

        private static readonly SolidColorBrush TemporaryHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA5100"));
        private static readonly SolidColorBrush PermanentHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33CA5100"));
        private static readonly SolidColorBrush SelectedPermanentHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA5100"));

        public string EnglishText { get; }
        public string RussianText { get; }
        public string Title { get; }

        public ObservableCollection<ErrorItem> Errors { get; } = new ObservableCollection<ErrorItem>();

        private string _newErrorText;
        public string NewErrorText
        {
            get => _newErrorText;
            set => Set(ref _newErrorText, value);
        }

        private ErrorItem _selectedError;
        public ErrorItem SelectedError
        {
            get => _selectedError;
            set
            {
                if (Set(ref _selectedError, value))
                {
                    UpdateHighlightsForSelection();
                }
            }
        }

        private bool _isMarkerActive;
        public bool IsMarkerActive
        {
            get => _isMarkerActive;
            set => Set(ref _isMarkerActive, value);
        }

        public double FontSize
        {
            get => _appSettings.AnalysisFontSize;
            set
            {
                _appSettings.AnalysisFontSize = value;
                OnPropertyChanged();
                _appSettings.Save();
            }
        }

        public ICommand IncreaseFontCommand { get; }
        public ICommand DecreaseFontCommand { get; }
        public ICommand AddErrorCommand { get; }
        public ICommand DeleteErrorCommand { get; }
        public ICommand EditErrorCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ToggleMarkerCommand { get; }

        public RichTextBox EnglishRichTextBox { get; set; }
        public RichTextBox RussianRichTextBox { get; set; }

        public AnalysisViewModel(LocalizationEntry entry, System.Collections.Generic.List<ErrorItem> existingErrors,
                                 IErrorStorage errorStorage, IStatusStorage statusStorage, IAppSettings appSettings)
        {
            _key = entry.Key;
            EnglishText = entry.English;
            RussianText = entry.Russian;
            Title = $"Анализ строки: {_key}";
            _errorStorage = errorStorage;
            _appSettings = appSettings;
            _entry = entry;
            _key = entry.Key;
            _statusStorage = statusStorage;

            Errors.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasErrors));
                OnPropertyChanged(nameof(CompletedButtonBrush));
                _entry.HasErrors = HasErrors;
                _entry.ErrorCount = Errors.Count;   // ← добавить
            };

            foreach (var err in existingErrors)
                Errors.Add(err);

            IncreaseFontCommand = new RelayCommand(_ => FontSize += 1);
            DecreaseFontCommand = new RelayCommand(_ => FontSize -= 1, _ => FontSize > 8);
            AddErrorCommand = new RelayCommand(AddError, _ => !string.IsNullOrWhiteSpace(NewErrorText));
            DeleteErrorCommand = new RelayCommand<ErrorItem>(DeleteError, item => item != null);
            EditErrorCommand = new RelayCommand<ErrorItem>(EditError);
            SaveCommand = new RelayCommand(_ => SaveChanges());
            ToggleMarkerCommand = new RelayCommand(_ => ToggleMarker());
            ToggleCompletedCommand = new RelayCommand(_ => IsCompleted = !IsCompleted);
        }

        public void InitializeDocuments()
        {
            BuildDocumentsWithAllPermanentHighlights();
        }

        private void BuildDocumentsWithAllPermanentHighlights()
        {
            var allEngHighlights = Errors.SelectMany(e => e.EnglishHighlights).ToList();
            var allRusHighlights = Errors.SelectMany(e => e.RussianHighlights).ToList();

            EnglishRichTextBox.Document = BuildDocumentWithHighlights(EnglishText, allEngHighlights, PermanentHighlightBrush);
            RussianRichTextBox.Document = BuildDocumentWithHighlights(RussianText, allRusHighlights, PermanentHighlightBrush);
        }

        private FlowDocument BuildDocumentWithHighlights(string text, System.Collections.Generic.List<TextRangeInfo> highlights, Brush highlightBrush)
        {
            var doc = new FlowDocument();
            var para = new Paragraph();
            int lastPos = 0;
            foreach (var range in highlights.OrderBy(r => r.StartIndex))
            {
                if (range.StartIndex > lastPos)
                {
                    para.Inlines.Add(new Run(text.Substring(lastPos, range.StartIndex - lastPos)));
                }
                var highlightedRun = new Run(text.Substring(range.StartIndex, range.Length));
                highlightedRun.Background = highlightBrush;
                para.Inlines.Add(highlightedRun);
                lastPos = range.StartIndex + range.Length;
            }
            if (lastPos < text.Length)
            {
                para.Inlines.Add(new Run(text.Substring(lastPos)));
            }
            doc.Blocks.Add(para);
            return doc;
        }

        private void UpdateHighlightsForSelection()
        {
            if (EnglishRichTextBox == null || RussianRichTextBox == null) return;

            // Все постоянные выделения (от всех ошибок)
            var allEngHighlights = Errors.SelectMany(e => e.EnglishHighlights).ToList();
            var allRusHighlights = Errors.SelectMany(e => e.RussianHighlights).ToList();

            // Выделения выбранной ошибки
            var selectedEng = SelectedError?.EnglishHighlights;
            var selectedRus = SelectedError?.RussianHighlights;

            // Если есть выбранные, удаляем их из постоянных, чтобы они не рисовались дважды
            if (selectedEng != null && selectedEng.Any())
            {
                allEngHighlights = allEngHighlights
                    .Where(h => !selectedEng.Any(s => s.StartIndex == h.StartIndex && s.Length == h.Length))
                    .ToList();
            }
            if (selectedRus != null && selectedRus.Any())
            {
                allRusHighlights = allRusHighlights
                    .Where(h => !selectedRus.Any(s => s.StartIndex == h.StartIndex && s.Length == h.Length))
                    .ToList();
            }

            EnglishRichTextBox.Document = BuildDocumentWithHighlights(EnglishText, allEngHighlights, PermanentHighlightBrush, selectedEng, SelectedPermanentHighlightBrush);
            RussianRichTextBox.Document = BuildDocumentWithHighlights(RussianText, allRusHighlights, PermanentHighlightBrush, selectedRus, SelectedPermanentHighlightBrush);
        }


        private void ToggleMarker()
        {
            IsMarkerActive = !IsMarkerActive;
        }

        public void ApplyMarkerToSelection(RichTextBox rtb)
        {
            if (rtb == null || rtb.Selection.IsEmpty || !IsMarkerActive) return;

            var range = new TextRange(rtb.Selection.Start, rtb.Selection.End);
            range.ApplyPropertyValue(TextElement.BackgroundProperty, TemporaryHighlightBrush);
            rtb.Selection.Select(rtb.Selection.Start, rtb.Selection.Start);
        }

        public void RemoveHighlightAtPosition(RichTextBox rtb, TextPointer position)
        {
            if (rtb == null || position == null || !IsMarkerActive) return;

            var start = position.GetPositionAtOffset(-1);
            var end = position.GetPositionAtOffset(1);
            if (start != null && end != null)
            {
                var checkRange = new TextRange(start, end);
                var bg = checkRange.GetPropertyValue(TextElement.BackgroundProperty);
                if (bg is SolidColorBrush brush && brush.Color == TemporaryHighlightBrush.Color)
                {
                    var redStart = FindBoundary(start, LogicalDirection.Backward, TemporaryHighlightBrush.Color);
                    var redEnd = FindBoundary(end, LogicalDirection.Forward, TemporaryHighlightBrush.Color);
                    if (redStart != null && redEnd != null)
                    {
                        var redRange = new TextRange(redStart, redEnd);
                        redRange.ClearAllProperties();
                    }
                }
            }
        }

        private TextPointer FindBoundary(TextPointer pointer, LogicalDirection direction, Color targetColor)
        {
            var current = pointer;
            while (current != null)
            {
                var context = current.GetPointerContext(direction);
                if (context == TextPointerContext.Text)
                {
                    var next = current.GetNextContextPosition(direction);
                    if (next == null) break;
                    var range = new TextRange(current, next);
                    var bg = range.GetPropertyValue(TextElement.BackgroundProperty);
                    if (bg is SolidColorBrush brush && brush.Color == targetColor)
                        current = next;
                    else
                        break;
                }
                else
                {
                    current = current.GetNextContextPosition(direction);
                }
            }
            return current;
        }

        private void AddError(object _)
        {
            if (string.IsNullOrWhiteSpace(NewErrorText)) return;

            var engHighlights = ExtractHighlightsFromDocument(EnglishRichTextBox.Document, TemporaryHighlightBrush.Color);
            var rusHighlights = ExtractHighlightsFromDocument(RussianRichTextBox.Document, TemporaryHighlightBrush.Color);

            var errorItem = new ErrorItem
            {
                Text = NewErrorText.Trim(),
                EnglishHighlights = engHighlights,
                RussianHighlights = rusHighlights
            };

            Errors.Add(errorItem);
            NewErrorText = string.Empty;
            SelectedError = errorItem;

            BuildDocumentsWithAllPermanentHighlights();
            UpdateHighlightsForSelection();
            IsMarkerActive = false;

            SaveChanges();
        }

        private List<TextRangeInfo> ExtractHighlightsFromDocument(FlowDocument doc, Color targetColor)
        {
            var highlights = new List<TextRangeInfo>();
            int currentPos = 0;
            var navigator = doc.ContentStart;

            while (navigator != null)
            {
                if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var next = navigator.GetNextContextPosition(LogicalDirection.Forward);
                    if (next != null)
                    {
                        var textRange = new TextRange(navigator, next);
                        var bg = textRange.GetPropertyValue(TextElement.BackgroundProperty);
                        int length = textRange.Text.Length;

                        if (bg is SolidColorBrush brush && brush.Color == targetColor)
                        {
                            highlights.Add(new TextRangeInfo { StartIndex = currentPos, Length = length });
                        }
                        currentPos += length;
                        navigator = next;
                        continue;
                    }
                }
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }
            return highlights;
        }

        private void DeleteError(ErrorItem item)
        {
            if (item == null || !Errors.Contains(item)) return;

            int index = Errors.IndexOf(item);
            Errors.Remove(item);

            if (SelectedError == item)
            {
                SelectedError = Errors.Count > 0 ? Errors[Math.Min(index, Errors.Count - 1)] : null;
            }

            BuildDocumentsWithAllPermanentHighlights();
            UpdateHighlightsForSelection();
            SaveChanges();
        }

        private void EditError(ErrorItem item)
        {
            if (item != null)
                item.IsEditing = true;
        }

        public void SaveChanges()
        {
            var errorList = Errors.Where(e => !string.IsNullOrWhiteSpace(e.Text)).ToList();
            _errorStorage.UpdateErrors(_key, errorList);
        }

        public void OnClosing()
        {
            SaveChanges();
        }

        public void SaveDocumentsToSelectedError()
        {

        }

        private FlowDocument BuildDocumentWithHighlights(string text,
    List<TextRangeInfo> permanentHighlights,
    Brush permanentBrush,
    List<TextRangeInfo> selectedHighlights = null,
    Brush selectedBrush = null)
        {
            var doc = new FlowDocument();
            var para = new Paragraph();
            int lastPos = 0;

            // Собираем все диапазоны с их кистями (приоритет у selected)
            var ranges = new Dictionary<int, (int length, Brush brush)>();

            if (permanentHighlights != null)
            {
                foreach (var h in permanentHighlights)
                    ranges[h.StartIndex] = (h.Length, permanentBrush);
            }

            if (selectedHighlights != null && selectedBrush != null)
            {
                foreach (var h in selectedHighlights)
                    ranges[h.StartIndex] = (h.Length, selectedBrush); // перезаписывает постоянный, если ключ совпадает
            }

            foreach (var range in ranges.OrderBy(x => x.Key))
            {
                int start = range.Key;
                int length = range.Value.length;
                Brush brush = range.Value.brush;

                if (start > lastPos)
                    para.Inlines.Add(new Run(text.Substring(lastPos, start - lastPos)));

                var run = new Run(text.Substring(start, length));
                run.Background = brush;
                para.Inlines.Add(run);
                lastPos = start + length;
            }

            if (lastPos < text.Length)
                para.Inlines.Add(new Run(text.Substring(lastPos)));

            doc.Blocks.Add(para);
            return doc;
        }
    }
}