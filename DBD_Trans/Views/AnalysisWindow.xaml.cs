using DBD_Trans.Helpers;
using DBD_Trans.Models;
using DBD_Trans.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace DBD_Trans.Views
{
    public partial class AnalysisWindow : Window
    {
        private AnalysisViewModel ViewModel => (AnalysisViewModel)DataContext;

        private bool _isMiddleScrolling = false;
        // Кэш для сохранения позиций скролла до входа в режим фокуса
        private double _engOffsetBeforeFocus = 0;
        private double _rusOffsetBeforeFocus = 0;
        private Point _middleScrollOrigin;
        private ScrollViewer _targetScrollViewer;
        private DispatcherTimer _scrollTimer;

        private ErrorItem _currentToolTipError;
        private DispatcherTimer _cursorHideTimer;
        private RichTextBox _targetRtbForCursor;
        private const int CursorHideDelayMs = 40;

        private ToolTip _sharedToolTip;
        private TextBlock _toolTipTextBlock;

        // ===== Настройки жеста "умного скролла" тачпада в режиме фокуса =====
        // Всё, что помечено [РЕДАКТИРУЕМОЕ], можно спокойно подстраивать под себя.

        // [РЕДАКТИРУЕМОЕ] Сколько мс пальцы должны непрерывно "ехать" по тачпаду (без пауз),
        // прежде чем мы перейдём в режим непрерывного скролла по абзацам.
        // Пока это время не прошло — быстрый свайп после отрыва пальцев просто перелистывает 1 абзац.
        private const double HoldToScrollThresholdMs = 500;

        // [РЕДАКТИРУЕМОЕ] Через сколько мс без новых событий колеса/тачпада считаем,
        // что пальцы оторвались от тачпада (жест завершён). Действует ТОЛЬКО пока мы
        // ещё не вошли в режим удержания — здесь короткий порог нужен, чтобы быстрый
        // свайп отрабатывал отзывчиво, без задержки.
        private const double FingerLiftGapMs = 150;

        // [РЕДАКТИРУЕМОЕ] Тот же смысл, что и FingerLiftGapMs, но действует УЖЕ В режиме
        // удержания. Порог специально гораздо более щедрый: если пальцы почти не двигаются
        // (а тачпад в принципе не шлёт события без движения), мы не должны тут же считать
        // это отрывом пальцев — иначе скролл будет останавливаться, стоит на секунду
        // задержать палец на месте, что и была вторая жалоба.
        private const double HeldModeReleaseGapMs = 700;

        // [РЕДАКТИРУЕМОЕ] Минимальная суммарная дельта быстрого свайпа, чтобы считать его
        // осознанным движением, а не случайным касанием тачпада.
        private const double MinSwipeDeltaThreshold = 50;

        // [РЕДАКТИРУЕМОЕ] Защита от входа в режим удержания на затухающем "хвосте" инерции
        // тачпада, а не на реальном продолжении свайпа. Идея: в момент, когда время жеста
        // пересекает HoldToScrollThresholdMs, смотрим не на весь жест целиком, а только на
        // СВЕЖУЮ активность за последние RecentActivityWindowMs миллисекунд. У живого,
        // по-настоящему удерживаемого свайпа эта свежая сумма всегда останется существенной.
        // У инерционного хвоста (после реального отрыва пальцев) она к этому моменту почти
        // наверняка провалится ниже MinRecentActivityForHold — и неважно, плавно или
        // "ступеньками" эта инерция затухает, важна только суммарная свежая сила.
        // Если проверка не проходит — просто НЕ входим в удержание и ждём естественного
        // завершения жеста по паузе (сработает обычный прыжок ровно на 1 абзац).
        private const double RecentActivityWindowMs = 180;
        private const double MinRecentActivityForHold = 45;

        // [РЕДАКТИРУЕМОЕ] Сразу после завершения жеста ненадолго игнорируем новые события
        // колеса — это "гасит" остаток инерционного хвоста тачпада, чтобы он не запустил
        // новый мини-жест и не породил лишний прыжок сразу вслед за только что выполненным.
        private const double PostGestureCooldownMs = 180;

        // [РЕДАКТИРУЕМОЕ] Диапазон скорости перелистывания абзацев в режиме удержания (мс между прыжками):
        // MaxJumpIntervalMs — сразу после входа в режим (пальцы почти не отведены от начала свайпа);
        // MinJumpIntervalMs — потолок скорости, когда пальцы отведены далеко.
        private const int MaxJumpIntervalMs = 280;
        private const int MinJumpIntervalMs = 60;

        // [РЕДАКТИРУЕМОЕ] Насколько сильно накопленное отведение пальцев от начала свайпа
        // влияет на скорость перелистывания (чем больше значение — тем резче нарастает скорость).
        private const double SwipeSpeedFactor = 0.6;

        // Как часто "опрашиваем" состояние жеста. Не стоит делать сильно больше 30-40мс,
        // иначе просядет точность попадания в HoldToScrollThresholdMs и плавность ускорения.
        private const double GestureTickIntervalMs = 25;

        // Накопленное смещение с начала ТЕКУЩЕГО жеста. Пока жест короткий — это просто
        // направление/сила свайпа для решения "прыгнуть на 1 абзац или нет" при отпускании.
        // Как только вошли в режим удержания — это же значение работает как "виртуальное
        // расстояние", на которое пальцы отведены от точки начала свайпа, и определяет скорость.
        private double _wheelAccumulator = 0;
        private DateTime _lastWheelEventTime = DateTime.MinValue;
        private DateTime _gestureStartTime = DateTime.MinValue;
        private DateTime _lastParagraphJumpTime = DateTime.MinValue;
        private bool _isGestureActive = false;
        private bool _isHeldScrollMode = false;
        private DispatcherTimer _smartScrollTimer;

        // Для проверки "свежей активности" при входе в режим удержания (см. RecentActivityWindowMs выше):
        // список недавних событий колеса (время + сила), старые записи стираются по мере поступления новых
        private struct WheelEventSample
        {
            public DateTime Time;
            public double AbsDelta;
        }
        private readonly List<WheelEventSample> _recentEvents = new List<WheelEventSample>();

        // Когда завершился предыдущий жест — используется для короткого "гашения" (PostGestureCooldownMs)
        private DateTime _gestureEndedAt = DateTime.MinValue;

        // Вложенный класс для независимой анимации каждого ScrollViewer
        private class ScrollAnimator
        {
            private DispatcherTimer _timer;
            private ScrollViewer _sv;
            private double _startOffset;
            private double _targetOffset;
            private DateTime _startTime;
            private TimeSpan _duration;

            public void SmoothScrollTo(ScrollViewer sv, double targetOffset, int durationMs)
            {
                if (_timer != null && _timer.IsEnabled)
                {
                    _timer.Stop();
                }

                _sv = sv;
                _startOffset = sv.VerticalOffset;
                _targetOffset = targetOffset;
                _startTime = DateTime.Now;
                _duration = TimeSpan.FromMilliseconds(durationMs);

                _timer = new DispatcherTimer(DispatcherPriority.Render);
                _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
                _timer.Tick += Timer_Tick;
                _timer.Start();
            }

            private void Timer_Tick(object sender, EventArgs e)
            {
                var elapsed = DateTime.Now - _startTime;
                double progress = elapsed.TotalMilliseconds / _duration.TotalMilliseconds;

                if (progress >= 1.0)
                {
                    progress = 1.0;
                    _timer.Stop();
                }

                // 【НОВОЕ】 EaseOutQuint - дает очень мягкое, "премиальное" и естественное торможение
                double eased = 1 - Math.Pow(1 - progress, 5);
                double currentOffset = _startOffset + (_targetOffset - _startOffset) * eased;

                if (_sv != null)
                {
                    _sv.ScrollToVerticalOffset(currentOffset);
                }
            }
        }

        // Два независимых аниматора: один для английского, другой для русского
        private readonly ScrollAnimator _engAnimator = new ScrollAnimator();
        private readonly ScrollAnimator _rusAnimator = new ScrollAnimator();

        public AnalysisWindow()
        {
            InitializeComponent();

            this.SourceInitialized += (s, e) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SendMessage(handle, 0x80, IntPtr.Zero, IntPtr.Zero);
                SendMessage(handle, 0x80, IntPtr.Zero, new IntPtr(1));
            };

            Loaded += AnalysisWindow_Loaded;
            PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;

            DarkTitleBarHelper.ApplyDarkTitleBar(this);
            InitMiddleScroll();

            _cursorHideTimer = new DispatcherTimer();
            _cursorHideTimer.Interval = TimeSpan.FromMilliseconds(CursorHideDelayMs);
            _cursorHideTimer.Tick += CursorHideTimer_Tick;

            _smartScrollTimer = new DispatcherTimer();
            _smartScrollTimer.Tick += SmartScrollTimer_Tick;
        }

        private void AnalysisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.EnglishRichTextBox = EnglishRichTextBox;
            ViewModel.RussianRichTextBox = RussianRichTextBox;
            ViewModel.InitializeDocuments();
            InitSharedToolTip();

            ViewModel.FocusModeChanged += ViewModel_FocusModeChanged;
            ViewModel.DocumentsRebuilding += ViewModel_DocumentsRebuilding; // НОВОЕ
            ViewModel.DocumentsRebuilt += ViewModel_DocumentsRebuilt;       // реализация ниже — замените старую с Opacity
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            this.SizeChanged += (s, ev) => AdjustPaddingBlocks();
        }

        private double _savedEnglishOffset;
        private double _savedRussianOffset;

        private void ViewModel_DocumentsRebuilding()
        {
            // Синхронно, ДО пересборки — запоминаем текущую позицию скролла.
            // Document ещё старый, ничего не менялось.
            _savedEnglishOffset = EnglishScrollViewer.VerticalOffset;
            _savedRussianOffset = RussianScrollViewer.VerticalOffset;
        }

        private void ViewModel_DocumentsRebuilt()
        {
            // Вызывается сразу после того, как ViewModel присвоил новый .Document —
            // всё ещё в том же синхронном стеке вызовов, WPF пока не отрисовал
            // ни одного кадра. Правим паддинг и возвращаем скролл прямо здесь.
            AdjustPaddingBlocks();
            EnglishScrollViewer.ScrollToVerticalOffset(_savedEnglishOffset);
            RussianScrollViewer.ScrollToVerticalOffset(_savedRussianOffset);
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AnalysisViewModel.IsMarkerActive) && !ViewModel.IsMarkerActive)
            {
                EnglishRichTextBox.Focus(); // <-- ЗАМЕНИТЬ
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // ==========================================
        // ЛОГИКА РЕЖИМА ФОКУСА (Центрирование и Отступы)
        // ==========================================

        private string _paragraphJumpBuffer = "";

        // ===== DBD_Trans\Views\AnalysisWindow.xaml.cs =====

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                bool isHandled = false;
                string textToInsert = null;

                // Alt + 1: Только русский текст
                if (e.SystemKey == Key.D1 || e.SystemKey == Key.NumPad1)
                {
                    textToInsert = ViewModel.GetHighlightedText(false);
                    isHandled = true;
                }
                // Alt + 2: Только английский текст
                else if (e.SystemKey == Key.D2 || e.SystemKey == Key.NumPad2)
                {
                    textToInsert = ViewModel.GetHighlightedText(true);
                    isHandled = true;
                }
                // Alt + 3: Русский ≠ Английский (с заглавной буквы)
                else if (e.SystemKey == Key.D3 || e.SystemKey == Key.NumPad3)
                {
                    string ruText = ViewModel.GetHighlightedText(false);
                    string enText = ViewModel.GetHighlightedText(true);

                    if (!string.IsNullOrWhiteSpace(ruText) && !string.IsNullOrWhiteSpace(enText))
                    {
                        // Если выделены оба языка — формируем строку с "≠"
                        textToInsert = $"{CapitalizeFirstLetter(ruText)} ≠ {CapitalizeFirstLetter(enText)}";
                    }
                    else if (!string.IsNullOrWhiteSpace(ruText))
                    {
                        // Если выделен только русский
                        textToInsert = CapitalizeFirstLetter(ruText);
                    }
                    else if (!string.IsNullOrWhiteSpace(enText))
                    {
                        // Если выделен только английский
                        textToInsert = CapitalizeFirstLetter(enText);
                    }
                    isHandled = true;
                }

                // Если комбинация была нажата
                if (isHandled)
                {
                    if (!string.IsNullOrEmpty(textToInsert))
                    {
                        InsertTextAtCaret(textToInsert);
                        NewErrorTextBox.Focus(); // Переносим фокус для продолжения ввода
                    }
                    e.Handled = true; // Блокируем дальнейшую обработку клавиш
                    return;
                }
            }
            // ================================================

            // 1. Проверяем, что мы в режиме фокуса (твой старый код ниже)
            if (ViewModel.IsFocusMode && ViewModel.IsSplitBySentences)
            {
                // ... (остальной код без изменений)
                // 2. 【ЗАЩИТА】 Проверяем, не печатает ли пользователь сейчас в TextBox 
                // (Это защитит Поиск, Добавление и Редактирование замечаний)
                var focusedElement = Keyboard.FocusedElement;
                bool isTypingInTextBox = focusedElement is TextBox;

                if (!isTypingInTextBox)
                {
                    // Определяем нажатую цифру (основная клавиатура и Numpad)
                    int digit = -1;
                    if (e.Key >= Key.D0 && e.Key <= Key.D9) digit = e.Key - Key.D0;
                    else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) digit = e.Key - Key.NumPad0;

                    // --- Обработка цифр ---
                    if (digit != -1)
                    {
                        _paragraphJumpBuffer += digit.ToString();
                        UpdateJumpBufferUI();
                        e.Handled = true; // Блокируем дальнейшую обработку
                        return;
                    }

                    // --- Обработка Backspace (удалить последнюю цифру) ---
                    if (e.Key == Key.Back)
                    {
                        if (_paragraphJumpBuffer.Length > 0)
                        {
                            _paragraphJumpBuffer = _paragraphJumpBuffer.Substring(0, _paragraphJumpBuffer.Length - 1);
                            UpdateJumpBufferUI();
                        }
                        e.Handled = true;
                        return;
                    }

                    // --- Обработка Enter (Прыжок) ---
                    if (e.Key == Key.Enter)
                    {
                        if (int.TryParse(_paragraphJumpBuffer, out int targetNumber))
                        {
                            // В UI нумерация с 1, а индекс массива с 0
                            int targetIndex = targetNumber - 1;

                            // Проверяем, существует ли такой абзац
                            if (targetIndex >= 0 && targetIndex < ViewModel.TotalParagraphs)
                            {
                                ViewModel.CurrentFocusedParagraphIndex = targetIndex;
                            }
                        }
                        _paragraphJumpBuffer = ""; // Сбрасываем буфер в любом случае
                        UpdateJumpBufferUI();
                        e.Handled = true;
                        return;
                    }

                    // --- Обработка Escape (Отмена) ---
                    if (e.Key == Key.Escape)
                    {
                        _paragraphJumpBuffer = "";
                        UpdateJumpBufferUI();
                        e.Handled = true;
                        return;
                    }

                    // --- Сброс буфера при нажатии других клавиш (буквы, символы) ---
                    if (e.Key != Key.LeftShift && e.Key != Key.RightShift &&
                        e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl &&
                        e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
                    {
                        _paragraphJumpBuffer = "";
                        UpdateJumpBufferUI();
                    }
                }

                // --- Навигация стрелками (осталась как была) ---
                if (e.Key == Key.Down)
                {
                    ViewModel.NextParagraphCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Up)
                {
                    ViewModel.PrevParagraphCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            base.OnPreviewKeyDown(e);
        }

        // Вспомогательный метод для обновления UI
        private void UpdateJumpBufferUI()
        {
            ViewModel.JumpBufferText = string.IsNullOrEmpty(_paragraphJumpBuffer)
                ? ""
                : $"🎯 Переход к абзацу: {_paragraphJumpBuffer}";
        }
        private void ViewModel_FocusModeChanged()
        {
            // Определяем, входим мы в фокус или выходим из него
            bool isEnteringFocus = ViewModel.IsFocusMode;

            if (isEnteringFocus)
            {
                // 【ВХОД В РЕЖИМ ФОКУСА】
                // Сохраняем текущие позиции скролла СИНХРОННО. 
                // Это критично сделать прямо сейчас, ДО того как WPF пересчитает layout 
                // из-за изменения FontSize/Opacity в ApplyFocusMode() и добавления Padding.
                _engOffsetBeforeFocus = EnglishScrollViewer.VerticalOffset;
                _rusOffsetBeforeFocus = RussianScrollViewer.VerticalOffset;
            }

            // Используем Dispatcher, чтобы дать WPF завершить layout pass
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Обновляем отступы (Top/Bottom Padding). 
                // При входе в фокус они увеличатся, при выходе — вернутся к 0.
                AdjustPaddingBlocks();

                if (isEnteringFocus)
                {
                    // Если вошли в фокус — центрируем текущий абзац
                    ScrollToCurrentParagraph();
                }
                else
                {
                    // 【ВЫХОД ИЗ РЕЖИМА ФОКУСА】
                    // Так как AdjustPaddingBlocks() уже отработал, Padding вернулся в норму,
                    // и координатная сетка документа снова совпадает с той, что была до входа.
                    // Спокойно возвращаем скролл на сохраненные позиции.
                    EnglishScrollViewer.ScrollToVerticalOffset(_engOffsetBeforeFocus);
                    RussianScrollViewer.ScrollToVerticalOffset(_rusOffsetBeforeFocus);
                }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void AdjustPaddingBlocks()
        {
            AdjustPaddingForRtb(EnglishRichTextBox);
            AdjustPaddingForRtb(RussianRichTextBox);
        }

        private void AdjustPaddingForRtb(RichTextBox rtb)
        {
            if (rtb?.Document == null) return;
            ScrollViewer sv = (rtb == EnglishRichTextBox) ? EnglishScrollViewer : RussianScrollViewer;
            if (sv == null) return;

            rtb.UpdateLayout();
            sv.UpdateLayout();

            double viewportHeight = sv.ViewportHeight;
            if (viewportHeight <= 0) viewportHeight = sv.ActualHeight;

            double targetHeight = ViewModel.IsFocusMode ? viewportHeight : 0;
            if (targetHeight < 0) targetHeight = 0;

            if (rtb.Document.Blocks.FirstBlock is Paragraph topPad && topPad.Tag as string == "TopPadding")
            {
                topPad.Padding = new Thickness(0, targetHeight, 0, 0); // было Margin
            }

            if (rtb.Document.Blocks.LastBlock is Paragraph bottomPad && bottomPad.Tag as string == "BottomPadding")
            {
                bottomPad.Padding = new Thickness(0, 0, 0, targetHeight); // было Margin
            }

            rtb.UpdateLayout();
            sv.UpdateLayout();
        }
        private void ScrollToCurrentParagraph()
        {
            if (!ViewModel.IsFocusMode) return;
            ScrollToCenter(EnglishRichTextBox, ViewModel.GetEnglishParagraph(ViewModel.CurrentFocusedParagraphIndex));
            ScrollToCenter(RussianRichTextBox, ViewModel.GetRussianParagraph(ViewModel.CurrentFocusedParagraphIndex));
        }

        private void ScrollToCenter(RichTextBox rtb, Paragraph targetParagraph)
        {
            if (rtb == null || targetParagraph == null) return;
            ScrollViewer sv = (rtb == EnglishRichTextBox) ? EnglishScrollViewer : RussianScrollViewer;
            if (sv == null) return;

            rtb.UpdateLayout();
            sv.UpdateLayout();

            Rect startRect = targetParagraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Rect endRect = targetParagraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            Rect blockRect = Rect.Union(startRect, endRect);

            if (blockRect.IsEmpty) blockRect = new Rect(0, 0, 0, targetParagraph.FontSize);

            double viewportHeight = sv.ViewportHeight;
            if (viewportHeight <= 0) viewportHeight = sv.ActualHeight;

            double blockCenterY = blockRect.Y + (blockRect.Height / 2);
            double targetOffset = blockCenterY - (viewportHeight / 2);

            double maxOffset = sv.ExtentHeight - sv.ViewportHeight;
            if (maxOffset < 0) maxOffset = 0;
            targetOffset = Math.Max(0, Math.Min(targetOffset, maxOffset));

            // 【ИЗМЕНЕНО】 Увеличиваем время до 300мс для более плавного, "маслянистого" торможения
            var animator = (sv == EnglishScrollViewer) ? _engAnimator : _rusAnimator;
            animator.SmoothScrollTo(sv, targetOffset, 300);
        }
        // ==========================================
        // ОБРАБОТКА СКРОЛЛА МЫШИ
        // ==========================================

        private void RichTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Если включен режим фокуса — перехватываем управление
            if (ViewModel.IsFocusMode && ViewModel.IsSplitBySentences)
            {
                e.Handled = true;
                HandleFocusScrollWheel(e.Delta);
                return;
            }

            // === Обычный режим (код ниже остался без изменений) ===
            var rtb = sender as RichTextBox;
            if (rtb == null) return;

            ScrollViewer sv = (rtb == EnglishRichTextBox) ? EnglishScrollViewer : RussianScrollViewer;
            if (sv == null) return;

            double scrollAmount = e.Delta / 8.0;
            double newOffset = sv.VerticalOffset - scrollAmount;
            sv.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        // [РЕДАКТИРУЕМОЕ] Включает вывод отладочной информации о жестах в окно Output (Debug)
        // в Visual Studio. Если поведение всё ещё будет не устраивать — включите это,
        // повторите проблемный свайп и посмотрите реальные дельты/паузы/суммы по логу:
        // так пороги выше можно будет откалибровать не вслепую, а по фактическим цифрам.
        private const bool EnableGestureDebugLog = true;

        private void GestureLog(string message)
        {
            if (EnableGestureDebugLog)
                System.Diagnostics.Debug.WriteLine("[FocusScroll " + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);
        }

        private void HandleFocusScrollWheel(int delta)
        {
            var now = DateTime.Now;

            // Сразу после завершения предыдущего жеста ненадолго "глушим" входящие события —
            // отсекает остаток инерционного хвоста тачпада или дребезг при повторном касании,
            // который иначе мог бы ошибочно запустить новый жест / лишний прыжок.
            if (!_isGestureActive && (now - _gestureEndedAt).TotalMilliseconds < PostGestureCooldownMs)
                return;

            if (!_isGestureActive)
            {
                // Пальцы только коснулись тачпада и начали двигаться — начало нового жеста
                _isGestureActive = true;
                _isHeldScrollMode = false;
                _gestureStartTime = now;
                _wheelAccumulator = 0;
                _recentEvents.Clear();

                _smartScrollTimer.Interval = TimeSpan.FromMilliseconds(GestureTickIntervalMs);
                _smartScrollTimer.Start();
            }

            _lastWheelEventTime = now;
            _wheelAccumulator += delta;

            _recentEvents.Add(new WheelEventSample { Time = now, AbsDelta = Math.Abs(delta) });
            PruneRecentEvents(now);

            GestureLog("событие delta=" + delta + " накоплено=" + _wheelAccumulator.ToString("F0")
                + " удержание=" + _isHeldScrollMode + " свежаяАктивность=" + GetRecentActivitySum(now).ToString("F0"));
        }

        private void PruneRecentEvents(DateTime now)
        {
            _recentEvents.RemoveAll(ev => (now - ev.Time).TotalMilliseconds > RecentActivityWindowMs);
        }

        // Суммарная "сила" событий колеса за последние RecentActivityWindowMs мс. Используется
        // ТОЛЬКО как дополнительное условие для входа в режим удержания (см. комментарий у
        // RecentActivityWindowMs) — намеренно НЕ используется, пока мы уже в режиме удержания,
        // чтобы не сломать возможность держать палец почти неподвижно (вторая жалоба).
        private double GetRecentActivitySum(DateTime now)
        {
            PruneRecentEvents(now);
            double sum = 0;
            for (int i = 0; i < _recentEvents.Count; i++)
                sum += _recentEvents[i].AbsDelta;
            return sum;
        }

        private void FinalizeQuickSwipe(DateTime now)
        {
            _smartScrollTimer.Stop();

            bool willJump = Math.Abs(_wheelAccumulator) > MinSwipeDeltaThreshold;
            GestureLog("завершение быстрого свайпа, накоплено=" + _wheelAccumulator.ToString("F0") + " прыжок=" + willJump);

            if (willJump)
                ExecuteParagraphJump(_wheelAccumulator);

            ResetGestureState();
            _gestureEndedAt = now;
        }

        private void SmartScrollTimer_Tick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            double timeSinceLastWheel = (now - _lastWheelEventTime).TotalMilliseconds;

            // 1. Пальцы оторвались от тачпада: новых событий давно не было.
            // До входа в режим удержания порог короткий (свайп отрабатывает быстро),
            // а уже в режиме удержания — гораздо более щедрый (см. HeldModeReleaseGapMs),
            // чтобы можно было держать палец почти неподвижно и не терять скорость скролла.
            double releaseGapMs = _isHeldScrollMode ? HeldModeReleaseGapMs : FingerLiftGapMs;

            if (timeSinceLastWheel > releaseGapMs)
            {
                // Если мы так и не успели войти в режим удержания — это был быстрый свайп,
                // и FinalizeQuickSwipe перелистнёт РОВНО 1 абзац. Если мы уже были в режиме
                // удержания — просто останавливаем непрерывный скролл без лишнего прыжка
                // (последний абзац уже был показан предыдущим тиком).
                if (_isHeldScrollMode)
                {
                    _smartScrollTimer.Stop();
                    GestureLog("отпускание в режиме удержания, останавливаемся");
                    ResetGestureState();
                    _gestureEndedAt = now;
                }
                else
                {
                    FinalizeQuickSwipe(now);
                }
                return;
            }

            // 2. Пальцы всё ещё на тачпаде (свайп продолжается без пауз) — проверяем,
            // не пора ли перейти в режим непрерывного скролла
            if (!_isHeldScrollMode)
            {
                double timeSinceGestureStart = (now - _gestureStartTime).TotalMilliseconds;
                if (timeSinceGestureStart >= HoldToScrollThresholdMs)
                {
                    // Времени прошло достаточно, но это может быть и затухающий хвост
                    // инерции тачпада, а не живой удерживаемый свайп — дополнительно
                    // проверяем, что прямо СЕЙЧАС есть существенная свежая активность
                    double recentActivity = GetRecentActivitySum(now);
                    if (recentActivity >= MinRecentActivityForHold)
                    {
                        _isHeldScrollMode = true;
                        GestureLog("вход в режим удержания, свежаяАктивность=" + recentActivity.ToString("F0"));
                        // Сразу перелистываем первый абзац, не дожидаясь ещё одного интервала —
                        // иначе после 500мс ожидания будет ощущаться "залипание"
                        ExecuteParagraphJump(_wheelAccumulator);
                        _lastParagraphJumpTime = now;
                    }
                    else
                    {
                        GestureLog("порог времени пройден, но свежая активность мала (" + recentActivity.ToString("F0") + ") — похоже на хвост инерции, удержание НЕ включаем");
                    }
                    // Иначе — не входим в удержание и просто ждём: либо свежая активность
                    // ещё вернётся (жест продолжится), либо жест скоро завершится по паузе,
                    // и тогда сработает обычный прыжок ровно на 1 абзац
                }
                return;
            }

            // 3. Режим непрерывного скролла: чем дальше пальцы "отведены" от точки начала
            // свайпа (чем больше |_wheelAccumulator|), тем чаще перелистываем абзацы
            double timeSinceLastJump = (now - _lastParagraphJumpTime).TotalMilliseconds;
            int jumpIntervalMs = CalculateJumpInterval(_wheelAccumulator);

            if (timeSinceLastJump >= jumpIntervalMs)
            {
                ExecuteParagraphJump(_wheelAccumulator);
                _lastParagraphJumpTime = now;
                GestureLog("прыжок в режиме удержания, накоплено=" + _wheelAccumulator.ToString("F0") + " интервал=" + jumpIntervalMs);
            }
        }

        private int CalculateJumpInterval(double accumulatedDelta)
        {
            // Чем больше суммарное отведение пальцев от начала свайпа, тем короче интервал
            // между прыжками (быстрее листаем). Значение НЕ гасится со временем само по себе —
            // оно отражает то, насколько далеко "уехал" жест с момента, когда он начался.
            double absDelta = Math.Abs(accumulatedDelta);
            int intervalMs = (int)(MaxJumpIntervalMs - (absDelta * SwipeSpeedFactor));

            return Math.Max(MinJumpIntervalMs, Math.Min(MaxJumpIntervalMs, intervalMs));
        }

        private void ResetGestureState()
        {
            _isGestureActive = false;
            _isHeldScrollMode = false;
            _wheelAccumulator = 0;
            _recentEvents.Clear();
        }

        private void ExecuteParagraphJump(double delta)
        {
            if (delta < 0)
            {
                if (ViewModel.NextParagraphCommand.CanExecute(null))
                    ViewModel.NextParagraphCommand.Execute(null);
            }
            else if (delta > 0)
            {
                if (ViewModel.PrevParagraphCommand.CanExecute(null))
                    ViewModel.PrevParagraphCommand.Execute(null);
            }
        }



        // ==========================================
        // ТУЛТИПЫ И МАРКЕР
        // ==========================================

        private void InitSharedToolTip()
        {
            _toolTipTextBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Foreground = (Brush)FindResource("ForegroundPrimary") };
            _sharedToolTip = new ToolTip
            {
                Background = (Brush)FindResource("BackgroundLight"),
                Foreground = (Brush)FindResource("ForegroundPrimary"),
                BorderBrush = (Brush)FindResource("BorderDark"),
                Padding = new Thickness(8, 4, 8, 4),
                Content = _toolTipTextBlock,
                Placement = PlacementMode.Mouse
            };
            ToolTipService.SetInitialShowDelay(_sharedToolTip, 0);
            ToolTipService.SetBetweenShowDelay(_sharedToolTip, 0);
            ToolTipService.SetShowDuration(_sharedToolTip, 10000);
        }

        private void CursorHideTimer_Tick(object sender, EventArgs e)
        {
            _cursorHideTimer.Stop();
            if (_targetRtbForCursor != null) _targetRtbForCursor.Cursor = Cursors.None;
        }

        private void RichTextBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_sharedToolTip == null) return;
            var rtb = sender as RichTextBox;
            if (rtb == null) return;

            if (ViewModel.IsMarkerActive)
            {
                if (rtb.Cursor == Cursors.None) rtb.Cursor = Cursors.IBeam;
                _cursorHideTimer.Stop(); _targetRtbForCursor = null; HideToolTip(); return;
            }

            var pos = e.GetPosition(rtb);
            var pointer = rtb.GetPositionFromPoint(pos, false);
            var error = GetErrorAtPointer(rtb, pointer);

            if (error != null)
            {
                if (_targetRtbForCursor != rtb || !_cursorHideTimer.IsEnabled)
                {
                    _targetRtbForCursor = rtb; _cursorHideTimer.Stop(); _cursorHideTimer.Start();
                }

                if (_toolTipTextBlock.Text != error.Text) _toolTipTextBlock.Text = error.Text;

                Rect charRect = pointer.GetCharacterRect(LogicalDirection.Forward);
                Point bottomLeftInWindow = rtb.TransformToAncestor(this).Transform(new Point(charRect.Left, charRect.Bottom));

                if (!_sharedToolTip.IsOpen || _currentToolTipError != error)
                {
                    _sharedToolTip.PlacementTarget = this;
                    _sharedToolTip.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                    _sharedToolTip.VerticalOffset = bottomLeftInWindow.Y + 2;
                    _sharedToolTip.HorizontalOffset = bottomLeftInWindow.X;
                    _currentToolTipError = error;
                    if (!_sharedToolTip.IsOpen) _sharedToolTip.IsOpen = true;
                }
            }
            else
            {
                _cursorHideTimer.Stop(); _targetRtbForCursor = null;
                if (rtb.Cursor == Cursors.None) rtb.Cursor = Cursors.IBeam;
                HideToolTip();
            }
        }

        private void RichTextBox_MouseLeave(object sender, MouseEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb != null && rtb.Cursor == Cursors.None) rtb.Cursor = Cursors.IBeam;
            _cursorHideTimer.Stop(); _targetRtbForCursor = null; HideToolTip();
        }

        private void HideToolTip()
        {
            if (_sharedToolTip != null && _sharedToolTip.IsOpen) _sharedToolTip.IsOpen = false;
            _currentToolTipError = null;
        }

        private ErrorItem GetErrorAtPointer(RichTextBox rtb, TextPointer pointer)
        {
            if (pointer == null) return null;
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context != TextPointerContext.Text) return null;

            var nextContext = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (nextContext == null) return null;

            var checkRange = new TextRange(pointer, nextContext);
            string currentText = checkRange.Text;

            if (string.IsNullOrEmpty(currentText)) return null;

            var bg = checkRange.GetPropertyValue(TextElement.BackgroundProperty);
            if (bg is SolidColorBrush brush)
            {
                Color permColor = (Color)ColorConverter.ConvertFromString("#33CA5100");
                Color selColor = (Color)ColorConverter.ConvertFromString("#FFCA5100");

                if (brush.Color == permColor || brush.Color == selColor)
                {
                    int index = GetTextIndexFromPointer(rtb.Document, pointer);
                    if (index >= 0)
                    {
                        bool isEnglish = (rtb == EnglishRichTextBox);
                        var vm = DataContext as AnalysisViewModel;
                        if (vm == null) return null;

                        foreach (var error in vm.Errors)
                        {
                            var highlights = isEnglish ? error.EnglishHighlights : error.RussianHighlights;
                            if (highlights != null)
                            {
                                foreach (var h in highlights)
                                {
                                    if (index >= h.StartIndex && index < h.StartIndex + h.Length) return error;
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        private int GetTextIndexFromPointer(FlowDocument doc, TextPointer target)
        {
            var targetParagraph = target.Paragraph;
            if (targetParagraph == null) return -1;

            var paraSentences = targetParagraph.Tag as List<SentenceInfo>;
            int localIndex = 0;
            var pointer = targetParagraph.ContentStart;

            while (pointer != null && pointer.CompareTo(target) < 0)
            {
                var context = pointer.GetPointerContext(LogicalDirection.Forward);
                if (context == TextPointerContext.Text)
                {
                    var next = pointer.GetNextContextPosition(LogicalDirection.Forward);
                    if (next != null)
                    {
                        if (pointer.Parent is Run parentRun && parentRun.Tag as string == "ParagraphNumber")
                        {
                            pointer = next;
                            continue;
                        }

                        if (next.CompareTo(target) > 0)
                        {
                            var range = new TextRange(pointer, target);
                            string text = range.Text;
                            int len = text.Length;
                            if (text.EndsWith("\r\n")) len -= 2;
                            else if (text.EndsWith("\n") || text.EndsWith("\r")) len -= 1;
                            localIndex += len;
                            break;
                        }
                        else
                        {
                            var range = new TextRange(pointer, next);
                            string text = range.Text;
                            int len = text.Length;
                            if (text.EndsWith("\r\n")) len -= 2;
                            else if (text.EndsWith("\n") || text.EndsWith("\r")) len -= 1;
                            localIndex += len;
                            pointer = next;
                        }
                    }
                    else break;
                }
                else
                {
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
                }
            }

            if (paraSentences == null)
            {
                return localIndex;
            }

            int offsetInPara = localIndex;
            foreach (var s in paraSentences)
            {
                if (offsetInPara <= s.Length)
                {
                    return s.StartIndex + offsetInPara;
                }
                offsetInPara -= (s.Length + 1);
            }

            var lastSentence = paraSentences[paraSentences.Count - 1];
            return lastSentence.StartIndex + lastSentence.Length;
        }

        // ==========================================
        // МАРКЕР И КЛИКИ
        // ==========================================

        private void RichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                TextPointer pos = rtb.GetPositionFromPoint(e.GetPosition(rtb), true);
                if (pos != null) { ViewModel.RemoveHighlightAtPosition(rtb, pos); e.Handled = true; }
            }
        }

        private void RichTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var rtb = sender as RichTextBox;
            if (rtb == null || !ViewModel.IsMarkerActive) return;
            if (!rtb.Selection.IsEmpty)
            {
                ViewModel.ApplyMarkerToSelection(rtb);
                Mouse.Capture(null); e.Handled = true;
                rtb.Selection.Select(rtb.Selection.Start, rtb.Selection.Start);

                // ---> АВТОФОКУС НА ПОЛЕ ВВОДА <---
                NewErrorTextBox.Focus();
            }
        }

        private void RichTextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ViewModel.IsSplitBySentences) return;

            var rtb = sender as RichTextBox;
            var pos = e.GetPosition(rtb);
            var pointer = rtb.GetPositionFromPoint(pos, false);

            if (pointer != null)
            {
                var para = pointer.Paragraph;
                if (para != null && para.Tag is List<SentenceInfo> paraSentences && paraSentences.Count > 0)
                {
                    int cursorIndex = GetTextIndexFromPointer(rtb.Document, pointer);
                    SentenceInfo targetSentence = null;

                    foreach (var s in paraSentences)
                    {
                        if (cursorIndex >= s.StartIndex && cursorIndex <= s.StartIndex + s.Length)
                        {
                            targetSentence = s;
                            break;
                        }
                    }

                    if (targetSentence == null)
                    {
                        targetSentence = paraSentences[paraSentences.Count - 1];
                    }

                    if (targetSentence != null)
                    {
                        var menu = new ContextMenu();

                        var prevItem = new MenuItem { Header = "⬆️ Объединить с предыдущим" };
                        prevItem.Click += (s, ev) => ViewModel.MergeWithPreviousCommand.Execute(targetSentence);

                        var nextItem = new MenuItem { Header = "⬇️ Объединить со следующим" };
                        nextItem.Click += (s, ev) => ViewModel.MergeWithNextCommand.Execute(targetSentence);

                        var splitItem = new MenuItem { Header = "↕️ Выделить в отдельную строку" };
                        splitItem.Click += (s, ev) => ViewModel.SplitSentenceCommand.Execute(targetSentence);

                        menu.Items.Add(prevItem);
                        menu.Items.Add(nextItem);
                        menu.Items.Add(new Separator());
                        menu.Items.Add(splitItem);

                        menu.PlacementTarget = rtb;
                        menu.IsOpen = true;
                        e.Handled = true;
                    }
                }
            }
        }

        private void RichTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is RichTextBox rtb && rtb.Document != null)
            {
                // WPF в режиме IsReadOnly="True" имеет особенность "замораживать" выделение 
                // при потере фокуса и некорректно восстанавливать его при следующем клике.
                // Принудительно сворачиваем выделение в текущую позицию курсора, чтобы сбросить кэш.
                var caret = rtb.CaretPosition;
                if (caret != null)
                {
                    rtb.Selection.Select(caret, caret);
                }
            }
        }

        // ==========================================
        // ОШИБКИ И ПАНЕЛИ
        // ==========================================

        private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (e.ClickCount == 2 && IsChildOf(source, ErrorsHeaderPanel)) { ToggleErrorsPanel(); e.Handled = true; return; }
            if (FindParentOfType<ListBox>(source) == ErrorsListBox) return;
            if (FindParentOfType<Button>(source) != null || FindParentOfType<ToggleButton>(source) != null ||
                FindParentOfType<TextBox>(source) != null || FindParentOfType<RichTextBox>(source) != null ||
                FindParentOfType<ScrollBar>(source) != null || FindParentOfType<Thumb>(source) != null) return;

            ViewModel.SelectedError = null;

            // ---> ДОБАВЬ ЭТУ СТРОКУ <---
            EnglishRichTextBox.Focus(); // <-- ЗАМЕНИТЬ

            e.Handled = true;
        }

        private void ToggleErrorsPanel()
        {
            if (ErrorsRow == null) return;

            bool isCollapsed = ErrorsRow.Height.IsStar || (ErrorsRow.Height.IsAbsolute && ErrorsRow.Height.Value <= 30);

            if (isCollapsed)
            {
                int errorCount = ViewModel.Errors.Count;
                if (errorCount == 0) return;

                double calculatedHeight = 30 + (errorCount * 32);
                ErrorsRow.Height = new GridLength(Math.Min(calculatedHeight, Math.Max(150, this.ActualHeight * 0.4)));
            }
            else ErrorsRow.Height = new GridLength(20);
        }

        private bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;

                DependencyObject next = LogicalTreeHelper.GetParent(child);
                if (next == null)
                {
                    if (child is Visual || child is Visual3D) next = VisualTreeHelper.GetParent(child);
                    else if (child is TextElement te) next = te.Parent as DependencyObject;
                    else break;
                }
                child = next;
            }
            return false;
        }

        private static T FindParentOfType<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                DependencyObject parent = LogicalTreeHelper.GetParent(child);
                if (parent == null)
                {
                    if (child is Visual || child is Visual3D) parent = VisualTreeHelper.GetParent(child);
                    else break;
                }
                if (parent is T typedParent) return typedParent;
                child = parent;
            }
            return null;
        }

        private void ErrorsListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is ErrorItem errorItem)
            {
                ViewModel.EditErrorCommand.Execute(errorItem);
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    var textBox = FindVisualChild<TextBox>(item);
                    textBox?.Focus(); textBox?.SelectAll();
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
                if (string.IsNullOrWhiteSpace(errorItem.Text)) ViewModel.DeleteErrorCommand.Execute(errorItem);
                else { ViewModel.SaveChanges(); ViewModel.SelectedError = null; }
            }
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                {
                    errorItem.IsEditing = false;
                    if (string.IsNullOrWhiteSpace(errorItem.Text)) ViewModel.DeleteErrorCommand.Execute(errorItem);
                    else { ViewModel.SelectedError = null; ViewModel.SaveChanges(); EnglishRichTextBox.Focus(); } // <-- ЗАМЕНИТЬ
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (sender is TextBox textBox && textBox.DataContext is ErrorItem errorItem)
                {
                    errorItem.IsEditing = false; ViewModel.SelectedError = null; EnglishRichTextBox.Focus(); // <-- ЗАМЕНИТЬ
                }
                e.Handled = true;
            }
        }

        private void NewErrorTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.AddErrorCommand.Execute(null);

                // Снимаем фокус с TextBox, чтобы он не оставался выделенным
                EnglishRichTextBox.Focus();
                e.Handled = true;
            }
        }

        private void NewErrorTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.SelectedError = null;
        }

        // ==========================================
        // СКРОЛЛ СРЕДНЕЙ КНОПКОЙ МЫШИ
        // ==========================================

        private void InitMiddleScroll()
        {
            _scrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += ScrollTimer_Tick;

            EnglishScrollViewer.PreviewMouseDown += MiddleScroll_PreviewMouseDown;
            RussianScrollViewer.PreviewMouseDown += MiddleScroll_PreviewMouseDown;

            this.PreviewMouseUp += MiddleScroll_PreviewMouseUp;
            this.Deactivated += (s, e) => StopMiddleScroll();
        }

        private void MiddleScroll_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed && !_isMiddleScrolling)
            {
                _isMiddleScrolling = true; _middleScrollOrigin = Mouse.GetPosition(this);
                _targetScrollViewer = sender as ScrollViewer; this.Cursor = Cursors.ScrollAll;
                _scrollTimer.Start(); e.Handled = true;
            }
            else if (_isMiddleScrolling && e.ChangedButton != MouseButton.Middle) StopMiddleScroll();
        }

        private void MiddleScroll_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isMiddleScrolling) { StopMiddleScroll(); e.Handled = true; }
        }

        private void ScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMiddleScrolling || _targetScrollViewer == null) return;

            Point currentPos = Mouse.GetPosition(this);
            double deltaY = currentPos.Y - _middleScrollOrigin.Y;

            const double deadzone = 15.0; const double speed = 0.8; const double maxScroll = 40.0;

            if (Math.Abs(deltaY) > deadzone)
            {
                double distance = Math.Abs(deltaY) - deadzone;
                double scrollY = (deltaY > 0 ? 1 : -1) * distance * speed;

                if (scrollY > maxScroll) scrollY = maxScroll; else if (scrollY < -maxScroll) scrollY = -maxScroll;
                scrollY *= 0.6;

                if (Math.Abs(scrollY) > 0.1)
                {
                    double currentOffset = _targetScrollViewer.VerticalOffset;
                    double newOffsetY = currentOffset + scrollY;

                    double maxOffset = _targetScrollViewer.ExtentHeight - _targetScrollViewer.ViewportHeight;
                    if (maxOffset < 0) maxOffset = 0;

                    if (newOffsetY < 0) newOffsetY = 0; else if (newOffsetY > maxOffset) newOffsetY = maxOffset;

                    if (Math.Abs(newOffsetY - currentOffset) > 0.01) _targetScrollViewer.ScrollToVerticalOffset(newOffsetY);
                }
            }
        }

        private void StopMiddleScroll()
        {
            if (_isMiddleScrolling) { _isMiddleScrolling = false; _scrollTimer.Stop(); this.Cursor = Cursors.Arrow; Mouse.Capture(null); }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ViewModel.OnClosing();
            base.OnClosing(e);
        }

        // ===== DBD_Trans\Views\AnalysisWindow.xaml.cs =====

        // ... (внутри класса AnalysisWindow)

        /// <summary>
        /// Делает первую букву строки заглавной.
        /// </summary>
        private string CapitalizeFirstLetter(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // char.ToUpper корректно работает с кириллицей и латиницей
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        /// <summary>
        /// Умная вставка текста в NewErrorTextBox с учетом позиции курсора, выделения и авто-пробела.
        /// </summary>
        private void InsertTextAtCaret(string textToInsert)
        {
            if (string.IsNullOrEmpty(textToInsert)) return;

            int selStart = NewErrorTextBox.SelectionStart;
            int selLength = NewErrorTextBox.SelectionLength;
            string currentText = NewErrorTextBox.Text ?? "";

            // Добавляем пробел перед вставкой, если нужно (чтобы слова не слипались)
            string prefix = "";
            if (selStart > 0 && currentText[selStart - 1] != ' ')
            {
                prefix = " ";
            }

            string finalTextToInsert = prefix + textToInsert;

            // Формируем новый текст
            string newText = currentText.Substring(0, selStart) +
                             finalTextToInsert +
                             currentText.Substring(selStart + selLength);

            // Обновляем UI и ViewModel
            ViewModel.NewErrorText = newText;
            NewErrorTextBox.Text = newText;

            // Ставим курсор в конец вставленного текста
            NewErrorTextBox.CaretIndex = selStart + finalTextToInsert.Length;
            NewErrorTextBox.SelectionLength = 0;
        }

    }
}