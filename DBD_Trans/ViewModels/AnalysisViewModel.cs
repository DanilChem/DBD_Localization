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
    public class SentenceInfo
    {
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public string Text { get; set; }
        public bool IsMergedWithNext { get; set; }
    }

    public class AnalysisViewModel : ObservableObject
    {
        private readonly IErrorStorage _errorStorage;
        private readonly IAppSettings _appSettings;
        private readonly string _key;
        private readonly IStatusStorage _statusStorage;
        private readonly IMergeStorage _mergeStorage; // <-- НОВОЕ
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
                if (IsCompleted && HasErrors) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF750000"));
                if (IsCompleted && !HasErrors) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF004A7C"));
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public ICommand ToggleCompletedCommand { get; }
        public ICommand ToggleSplitCommand { get; }
        public ICommand MergeWithPreviousCommand { get; }
        public ICommand MergeWithNextCommand { get; }
        public ICommand SplitSentenceCommand { get; }

        private static readonly SolidColorBrush TemporaryHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA5100"));
        private static readonly SolidColorBrush PermanentHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33CA5100"));
        private static readonly SolidColorBrush SelectedPermanentHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA5100"));
        private static readonly SolidColorBrush SearchHighlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2f9cd6"));

        public string EnglishText { get; }
        public string RussianText { get; }
        public string CleanEnglishText => EnglishText?.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ") ?? "";
        public string CleanRussianText => RussianText?.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ") ?? "";

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
                if (Set(ref _selectedError, value)) RebuildDocuments();
            }
        }

        private bool _isMarkerActive;
        public bool IsMarkerActive
        {
            get => _isMarkerActive;
            set => Set(ref _isMarkerActive, value);
        }

        private bool _isSplitBySentences;
        public bool IsSplitBySentences
        {
            get => _isSplitBySentences;
            set
            {
                if (Set(ref _isSplitBySentences, value))
                {
                    RebuildDocuments(); // Просто перерисовываем, предложения уже распарсены
                }
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value)) RebuildDocuments();
            }
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

        public List<SentenceInfo> EnglishSentences { get; private set; } = new List<SentenceInfo>();
        public List<SentenceInfo> RussianSentences { get; private set; } = new List<SentenceInfo>();

        public AnalysisViewModel(LocalizationEntry entry, List<ErrorItem> existingErrors,
            IErrorStorage errorStorage, IStatusStorage statusStorage, IAppSettings appSettings, IMergeStorage mergeStorage)
        {
            _key = entry.Key;
            EnglishText = entry.English;
            RussianText = entry.Russian;
            Title = $"Анализ строки: {_key}";
            _errorStorage = errorStorage;
            _appSettings = appSettings;
            _entry = entry;
            _statusStorage = statusStorage;
            _mergeStorage = mergeStorage; // <-- НОВОЕ

            Errors.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasErrors));
                OnPropertyChanged(nameof(CompletedButtonBrush));
                _entry.HasErrors = HasErrors;
                _entry.ErrorCount = Errors.Count;
                RebuildDocuments();
            };

            foreach (var err in existingErrors) Errors.Add(err);

            IncreaseFontCommand = new RelayCommand(_ => FontSize += 1);
            DecreaseFontCommand = new RelayCommand(_ => FontSize -= 1, _ => FontSize > 8);
            AddErrorCommand = new RelayCommand(AddError, _ => !string.IsNullOrWhiteSpace(NewErrorText));
            DeleteErrorCommand = new RelayCommand<ErrorItem>(DeleteError, item => item != null);
            EditErrorCommand = new RelayCommand<ErrorItem>(EditError);
            SaveCommand = new RelayCommand(_ => SaveChanges());
            ToggleMarkerCommand = new RelayCommand(_ => ToggleMarker());
            ToggleCompletedCommand = new RelayCommand(_ => IsCompleted = !IsCompleted);

            ToggleSplitCommand = new RelayCommand(_ => IsSplitBySentences = !IsSplitBySentences);
            MergeWithPreviousCommand = new RelayCommand<SentenceInfo>(MergeWithPrevious);
            MergeWithNextCommand = new RelayCommand<SentenceInfo>(MergeWithNext);
            SplitSentenceCommand = new RelayCommand<SentenceInfo>(SplitSentence);
        }

        public void InitializeDocuments()
        {
            // Парсим предложения
            EnglishSentences = ParseSentences(CleanEnglishText);
            RussianSentences = ParseSentences(CleanRussianText);

            // Загружаем сохраненные склейки
            var engMerges = _mergeStorage.GetMerges(_key, true);
            var rusMerges = _mergeStorage.GetMerges(_key, false);

            foreach (var s in EnglishSentences)
                if (engMerges.Contains(s.StartIndex)) s.IsMergedWithNext = true;
            foreach (var s in RussianSentences)
                if (rusMerges.Contains(s.StartIndex)) s.IsMergedWithNext = true;

            RebuildDocuments();
        }

        private class HighlightSegment
        {
            public int Start { get; set; }
            public int Length { get; set; }
            public Brush Brush { get; set; }
            public int Priority { get; set; }
        }

        private void RebuildDocuments()
        {
            if (EnglishRichTextBox == null || RussianRichTextBox == null) return;

            string engText = IsSplitBySentences ? CleanEnglishText : EnglishText;
            string rusText = IsSplitBySentences ? CleanRussianText : RussianText;

            var searchHighlightsEng = FindMatches(engText, _searchText);
            var searchHighlightsRus = FindMatches(rusText, _searchText);

            var allEngPermanent = Errors.SelectMany(e => e.EnglishHighlights).ToList();
            var allRusPermanent = Errors.SelectMany(e => e.RussianHighlights).ToList();

            var selectedEng = SelectedError?.EnglishHighlights;
            var selectedRus = SelectedError?.RussianHighlights;

            var engSegments = BuildSegments(allEngPermanent, selectedEng, searchHighlightsEng);
            var rusSegments = BuildSegments(allRusPermanent, selectedRus, searchHighlightsRus);

            EnglishRichTextBox.Document = BuildDocument(engText, engSegments, IsSplitBySentences, EnglishSentences);
            RussianRichTextBox.Document = BuildDocument(rusText, rusSegments, IsSplitBySentences, RussianSentences);

            ScrollToFirstMatch(EnglishRichTextBox, searchHighlightsEng);
            ScrollToFirstMatch(RussianRichTextBox, searchHighlightsRus);

        }

        private List<HighlightSegment> BuildSegments(List<TextRangeInfo> permanent, List<TextRangeInfo> selected, List<TextRangeInfo> search)
        {
            var segments = new List<HighlightSegment>();
            if (permanent != null)
                foreach (var h in permanent)
                    segments.Add(new HighlightSegment { Start = h.StartIndex, Length = h.Length, Brush = PermanentHighlightBrush, Priority = 1 });
            if (selected != null)
                foreach (var h in selected)
                    segments.Add(new HighlightSegment { Start = h.StartIndex, Length = h.Length, Brush = SelectedPermanentHighlightBrush, Priority = 2 });
            if (search != null)
                foreach (var h in search)
                    segments.Add(new HighlightSegment { Start = h.StartIndex, Length = h.Length, Brush = SearchHighlightBrush, Priority = 3 });
            return segments;
        }

        private FlowDocument BuildDocument(string text, List<HighlightSegment> highlights, bool isSplit, List<SentenceInfo> sentences)
        {
            var doc = new FlowDocument();
            if (string.IsNullOrEmpty(text))
            {
                doc.Blocks.Add(new Paragraph());
                return doc;
            }

            if (!isSplit || sentences == null || sentences.Count == 0)
            {
                var para = new Paragraph();
                ApplyRunsToParagraph(para, text, 0, text.Length, highlights, 0);
                doc.Blocks.Add(para);
            }
            else
            {
                Paragraph currentPara = null;
                List<SentenceInfo> currentParaSentences = null;

                foreach (var sentence in sentences)
                {
                    if (currentPara == null)
                    {
                        currentPara = new Paragraph();
                        currentParaSentences = new List<SentenceInfo>();
                        currentPara.Tag = currentParaSentences; // Сохраняем список предложений в Tag абзаца
                        doc.Blocks.Add(currentPara);
                    }

                    currentParaSentences.Add(sentence);
                    // runTag больше не нужен, мы убрали его
                    ApplyRunsToParagraph(currentPara, sentence.Text, sentence.StartIndex, sentence.Length, highlights, sentence.StartIndex);

                    currentPara.Margin = sentence.IsMergedWithNext ? new Thickness(0) : new Thickness(0, 0, 0, 25);

                    if (sentence.IsMergedWithNext)
                    {
                        currentPara.Inlines.Add(new Run(" "));
                    }
                    else
                    {
                        currentPara = null;
                        currentParaSentences = null;
                    }
                }
            }
            return doc;
        }
        // Добавлен параметр runTag
        private void ApplyRunsToParagraph(Paragraph para, string text, int textStart, int textLength, List<HighlightSegment> highlights, int globalOffset = 0)
        {
            var brushes = new Brush[textLength];
            var sorted = highlights.OrderBy(h => h.Priority).ToList();

            foreach (var h in sorted)
            {
                int hStart = h.Start - globalOffset;
                int hEnd = hStart + h.Length;
                int drawStart = Math.Max(0, hStart);
                int drawEnd = Math.Min(textLength, hEnd);

                for (int i = drawStart; i < drawEnd; i++)
                {
                    brushes[i] = h.Brush;
                }
            }

            int lastPos = 0;
            Brush currentBrush = brushes.Length > 0 ? brushes[0] : null;

            for (int i = 1; i <= textLength; i++)
            {
                Brush nextBrush = i < textLength ? brushes[i] : null;
                if (nextBrush != currentBrush || i == textLength)
                {
                    int length = i - lastPos;
                    if (length > 0)
                    {
                        var run = new Run(text.Substring(lastPos, length));
                        if (currentBrush != null) run.Background = currentBrush;
                        para.Inlines.Add(run);
                    }
                    lastPos = i;
                    currentBrush = nextBrush;
                }
            }
        }
        private List<SentenceInfo> ParseSentences(string text)
        {
            var result = new List<SentenceInfo>();
            if (string.IsNullOrEmpty(text)) return result;

            int start = 0;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '.' || c == '!' || c == '?')
                {
                    bool isEnd = false;
                    if (i == text.Length - 1) isEnd = true;
                    else if (char.IsWhiteSpace(text[i + 1])) isEnd = true;
                    else if (text[i + 1] == '"' || text[i + 1] == '»' || text[i + 1] == ']' || text[i + 1] == ')') isEnd = true;

                    if (isEnd)
                    {
                        int length = i - start + 1;
                        if (length > 0)
                        {
                            result.Add(new SentenceInfo
                            {
                                StartIndex = start,
                                Length = length,
                                Text = text.Substring(start, length),
                                IsMergedWithNext = false
                            });
                        }
                        start = i + 1;
                        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
                    }
                }
            }

            if (start < text.Length)
            {
                int end = text.Length - 1;
                while (end >= start && char.IsWhiteSpace(text[end])) end--;
                int length = end - start + 1;
                if (length > 0)
                {
                    result.Add(new SentenceInfo
                    {
                        StartIndex = start,
                        Length = length,
                        Text = text.Substring(start, length),
                        IsMergedWithNext = false
                    });
                }
            }
            return result;
        }

        private List<TextRangeInfo> FindMatches(string text, string search)
        {
            var matches = new List<TextRangeInfo>();
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(text)) return matches;
            int index = 0;
            while ((index = text.IndexOf(search, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                matches.Add(new TextRangeInfo { StartIndex = index, Length = search.Length });
                index += search.Length;
            }
            return matches;
        }

        private void ScrollToFirstMatch(RichTextBox rtb, List<TextRangeInfo> matches)
        {
            if (matches == null || matches.Count == 0) return;
            var firstMatch = matches[0];
            var pointer = GetTextPointerByIndex(rtb.Document, firstMatch.StartIndex);
            if (pointer != null && pointer.Parent is FrameworkContentElement element)
            {
                element.BringIntoView();
            }
        }

        private TextPointer GetTextPointerByIndex(FlowDocument doc, int index)
        {
            var pointer = doc.ContentStart;
            int currentIndex = 0;
            while (pointer != null)
            {
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var next = pointer.GetNextContextPosition(LogicalDirection.Forward);
                    if (next != null)
                    {
                        var range = new TextRange(pointer, next);
                        int len = range.Text.Length;
                        if (currentIndex + len > index) return pointer.GetPositionAtOffset(index - currentIndex, LogicalDirection.Forward);
                        currentIndex += len;
                        pointer = next;
                    }
                    else break;
                }
                else pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
            return null;
        }

        private void ToggleMarker() => IsMarkerActive = !IsMarkerActive;

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
                    if (bg is SolidColorBrush brush && brush.Color == targetColor) current = next;
                    else break;
                }
                else current = current.GetNextContextPosition(direction);
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
            SelectedError = null;
            IsMarkerActive = false;
            SaveChanges();
        }

        private List<TextRangeInfo> ExtractHighlightsFromDocument(FlowDocument doc, Color targetColor)
        {
            var highlights = new List<TextRangeInfo>();
            foreach (Block block in doc.Blocks)
            {
                if (block is Paragraph para)
                {
                    var paraSentences = para.Tag as List<SentenceInfo>;
                    int localPos = 0;

                    foreach (var inline in para.Inlines)
                    {
                        if (inline is Run run && !string.IsNullOrEmpty(run.Text))
                        {
                            string text = run.Text;
                            int textLen = text.Length;

                            // Защита от скрытых \r\n
                            if (text.EndsWith("\r\n")) textLen -= 2;
                            else if (text.EndsWith("\n") || text.EndsWith("\r")) textLen -= 1;

                            var bg = run.Background as SolidColorBrush;
                            if (bg != null && bg.Color == targetColor && textLen > 0)
                            {
                                if (paraSentences == null)
                                {
                                    // Обычный режим
                                    highlights.Add(new TextRangeInfo
                                    {
                                        StartIndex = localPos,
                                        Length = textLen
                                    });
                                }
                                else
                                {
                                    // Режим разделения: математически маппим localPos на предложения
                                    int currentOffset = localPos;
                                    int remaining = textLen;

                                    while (remaining > 0)
                                    {
                                        int offsetInPara = currentOffset;
                                        SentenceInfo targetSentence = null;
                                        int localIndexInSentence = 0;

                                        foreach (var s in paraSentences)
                                        {
                                            if (offsetInPara < s.Length)
                                            {
                                                targetSentence = s;
                                                localIndexInSentence = offsetInPara;
                                                break;
                                            }
                                            offsetInPara -= (s.Length + 1); // +1 за пробел между предложениями
                                        }

                                        if (targetSentence != null)
                                        {
                                            int charsInSentence = Math.Min(remaining, targetSentence.Length - localIndexInSentence);

                                            highlights.Add(new TextRangeInfo
                                            {
                                                StartIndex = targetSentence.StartIndex + localIndexInSentence,
                                                Length = charsInSentence
                                            });

                                            currentOffset += charsInSentence;
                                            remaining -= charsInSentence;

                                            // Если дошли до конца предложения, пропускаем пробел
                                            if (localIndexInSentence + charsInSentence == targetSentence.Length && remaining > 0)
                                            {
                                                currentOffset++;
                                                remaining--;
                                            }
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                            localPos += textLen;
                        }
                    }
                }
            }
            return highlights;
        }

        private void DeleteError(ErrorItem item)
        {
            if (item == null || !Errors.Contains(item)) return;
            int index = Errors.IndexOf(item);
            Errors.Remove(item);
            if (SelectedError == item) SelectedError = Errors.Count > 0 ? Errors[Math.Min(index, Errors.Count - 1)] : null;
            SaveChanges();
        }

        private void EditError(ErrorItem item)
        {
            if (item != null) item.IsEditing = true;
        }

        public void SaveChanges()
        {
            var errorList = Errors.Where(e => !string.IsNullOrWhiteSpace(e.Text)).ToList();
            _errorStorage.UpdateErrors(_key, errorList);
        }

        public void OnClosing() => SaveChanges();

        // --- Логика объединения предложений ---
        private void MergeWithPrevious(SentenceInfo sentence) => UpdateMergeState(sentence, true);
        private void MergeWithNext(SentenceInfo sentence) => UpdateMergeState(sentence, false);
        private void SplitSentence(SentenceInfo sentence) => UpdateMergeState(sentence, null);

        private void UpdateMergeState(SentenceInfo sentence, bool? mergeWithPrev)
        {
            int engIndex = EnglishSentences.IndexOf(sentence);
            if (engIndex >= 0)
            {
                if (mergeWithPrev == true && engIndex > 0) EnglishSentences[engIndex - 1].IsMergedWithNext = true;
                else if (mergeWithPrev == false) sentence.IsMergedWithNext = true;
                else if (mergeWithPrev == null) // Split
                {
                    if (engIndex > 0) EnglishSentences[engIndex - 1].IsMergedWithNext = false;
                    sentence.IsMergedWithNext = false;
                }
            }

            int rusIndex = RussianSentences.IndexOf(sentence);
            if (rusIndex >= 0)
            {
                if (mergeWithPrev == true && rusIndex > 0) RussianSentences[rusIndex - 1].IsMergedWithNext = true;
                else if (mergeWithPrev == false) sentence.IsMergedWithNext = true;
                else if (mergeWithPrev == null)
                {
                    if (rusIndex > 0) RussianSentences[rusIndex - 1].IsMergedWithNext = false;
                    sentence.IsMergedWithNext = false;
                }
            }

            SaveMerges(); // <-- СОХРАНЯЕМ СОСТОЯНИЕ
            RebuildDocuments();
        }

        private void SaveMerges()
        {
            var engMerges = EnglishSentences.Where(s => s.IsMergedWithNext).Select(s => s.StartIndex).ToList();
            var rusMerges = RussianSentences.Where(s => s.IsMergedWithNext).Select(s => s.StartIndex).ToList();

            _mergeStorage.SetMerges(_key, true, engMerges);
            _mergeStorage.SetMerges(_key, false, rusMerges);
            _mergeStorage.Save();
        }
    }
}