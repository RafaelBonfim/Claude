using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using UniversalGameTranslator.Models;
using UniversalGameTranslator.Services;
using UniversalGameTranslator.Views;

namespace UniversalGameTranslator
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly GameDetector _gameDetector;
        private readonly TextHookManager _hookManager;
        private readonly TranslationService _translator;
        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

        public ObservableCollection<GameProcess> DetectedGames { get; set; }
        public ObservableCollection<TranslationEntry> Translations { get; set; }

        private GameProcess? _selectedGame; // <--- CORRIGIDO: Adicionado '?'
        public GameProcess? SelectedGame // <--- CORRIGIDO: Adicionado '?'
        {
            get => _selectedGame;
            set
            {
                _selectedGame = value;
                OnPropertyChanged(nameof(SelectedGame));
                if (value != null)
                    _ = StartHookingAsync(value);
            }
        }

        private bool _isHooking = false;
        private TranslationOverlay? _currentOverlay; // <--- CORRIGIDO: Adicionado '?'

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            DetectedGames = new ObservableCollection<GameProcess>();
            Translations = new ObservableCollection<TranslationEntry>();

            _gameDetector = new GameDetector();
            _hookManager = new TextHookManager();
            _translator = new TranslationService();

            _hookManager.TextCaptured += OnTextCaptured;

            // Setup refresh timer
            _refreshTimer = new System.Windows.Threading.DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(3);
            _refreshTimer.Tick += async (s, e) => await RefreshGameListAsync();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Initializing...", Colors.Orange);
            await RefreshGameListAsync();
            _refreshTimer.Start();
            UpdateStatus("Ready to capture game text...", Colors.Green);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e) // <--- CORRIGIDO: Adicionado '?'
        {
            _refreshTimer?.Stop();
            _hookManager?.Dispose();
            _currentOverlay?.Close();
        }

        private async Task RefreshGameListAsync()
        {
            try
            {
                var games = await _gameDetector.DetectGamesAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Preserve selection if possible
                    var selectedId = SelectedGame?.ProcessId;

                    DetectedGames.Clear();
                    foreach (var game in games.OrderBy(g => g.Name))
                    {
                        DetectedGames.Add(game);
                    }

                    // Restore selection
                    if (selectedId.HasValue)
                    {
                        var gameToSelect = DetectedGames.FirstOrDefault(g => g.ProcessId == selectedId.Value);
                        if (gameToSelect != null)
                            SelectedGame = gameToSelect;
                    }
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error detecting games: {ex.Message}", Colors.Red);
            }
        }

        private async Task StartHookingAsync(GameProcess game)
        {
            if (_isHooking) return;

            try
            {
                _isHooking = true;
                UpdateStatus($"Hooking into {game.Name}...", Colors.Orange);

                bool success = await _hookManager.HookProcessAsync(game.ProcessId);

                if (success)
                {
                    UpdateStatus($"Successfully hooked into {game.Name}!", Colors.Green);
                    HookStatusText.Text = "Active";
                    HookStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
                }
                else
                {
                    UpdateStatus($"Failed to hook {game.Name}. Trying OCR fallback...", Colors.Orange);

                    // Try OCR fallback
                    await _hookManager.StartOCRFallbackAsync(game.ProcessId);
                    UpdateStatus($"Using OCR fallback for {game.Name}", Colors.Yellow);
                    HookStatusText.Text = "OCR Mode";
                    HookStatusText.Foreground = new SolidColorBrush(Colors.Yellow);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", Colors.Red);
                HookStatusText.Text = "Error";
                HookStatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
            finally
            {
                _isHooking = false;
            }
        }

        private async void OnTextCaptured(object? sender, TextCapturedEventArgs e) // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                // Filter out non-game text
                if (!IsGameText(e.Text)) return;

                // Avoid duplicate translations
                if (Translations.Any(t => t.OriginalText.Equals(e.Text, StringComparison.OrdinalIgnoreCase)))
                    return;

                var targetLang = GetSelectedLanguageCode();
                var translation = await _translator.TranslateAsync(e.Text, targetLang);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var entry = new TranslationEntry
                    {
                        OriginalText = e.Text,
                        TranslatedText = translation,
                        Timestamp = DateTime.Now,
                        Source = e.Source
                    };

                    Translations.Insert(0, entry);

                    // Keep only last 50 translations for performance
                    if (Translations.Count > 50)
                        Translations.RemoveAt(Translations.Count - 1);

                    UpdateTranslationCount();

                    // Show overlay if enabled
                    if (ShowOverlayCheckbox.IsChecked == true)
                    {
                        ShowTranslationOverlay(entry);
                    }
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Translation error: {ex.Message}", Colors.Red);
            }
        }

        private bool IsGameText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length < 3 || text.Length > 300) return false;

            // Filter out common debug/technical text
            var lowerText = text.ToLower();
            if (lowerText.Contains("fps") ||
                lowerText.Contains("debug") ||
                lowerText.Contains(".dll") ||
                lowerText.Contains("error") ||
                lowerText.Contains("null") ||
                lowerText.Contains("undefined"))
                return false;

            // Must contain reasonable amount of letters
            int letterCount = text.Count(char.IsLetter);
            int spaceCount = text.Count(char.IsWhiteSpace);

            // At least 40% letters, and some spaces for natural language
            return letterCount > text.Length * 0.4 && (spaceCount > 0 || text.Length < 20);
        }

        private string GetSelectedLanguageCode()
        {
            return LanguageComboBox.SelectedIndex switch
            {
                0 => "pt", // Português
                1 => "en", // English
                2 => "es", // Español
                3 => "fr", // Français
                4 => "de", // Deutsch
                5 => "ja", // 日本語
                6 => "ko", // 한국어
                7 => "zh", // 中文
                _ => "pt"
            };
        }

        private void ShowTranslationOverlay(TranslationEntry entry)
        {
            try
            {
                // Close previous overlay
                _currentOverlay?.Close();

                // Create new overlay
                _currentOverlay = new TranslationOverlay(entry);
                _currentOverlay.Show();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Overlay error: {ex.Message}", Colors.Red);
            }
        }

        private void UpdateStatus(string message, Color color)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                StatusIndicator.Fill = new SolidColorBrush(color);
            });
        }

        private void UpdateTranslationCount()
        {
            TranslationCountText.Text = $"{Translations.Count} translations";
        }

        // Event Handlers
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Refreshing game list...", Colors.Orange);
            await RefreshGameListAsync();
            UpdateStatus("Game list updated", Colors.Green);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _hookManager.StopAll();
                _currentOverlay?.Close();
                SelectedGame = null;

                HookStatusText.Text = "Stopped";
                HookStatusText.Foreground = new SolidColorBrush(Colors.Gray);

                UpdateStatus("Hooking stopped", Colors.Gray);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error stopping: {ex.Message}", Colors.Red);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Translations.Clear();
            UpdateTranslationCount();
            UpdateStatus("Translations cleared", Colors.Green);
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged; // <--- CORRIGIDO: Adicionado '?'
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}