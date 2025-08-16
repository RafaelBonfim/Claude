using System;
using System.Windows;
using System.Windows.Media.Animation;
using UniversalGameTranslator.Models;

namespace UniversalGameTranslator.Views
{
    public partial class TranslationOverlay : Window
    {
        private readonly System.Windows.Threading.DispatcherTimer _hideTimer;
        // private readonly System.Windows.Threading.DispatcherTimer _fadeTimer; // <--- REMOVIDO: Campo não utilizado

        public TranslationOverlay(TranslationEntry entry)
        {
            InitializeComponent();

            // Position at top-center of screen
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 80;

            // Set content
            OriginalTextBlock.Text = entry.OriginalText;
            TranslatedTextBlock.Text = entry.TranslatedText;

            // Update source indicator
            SourceIndicator.Text = entry.Source switch
            {
                TextSource.DrawText => "[DrawText]",
                TextSource.TextOut => "[TextOut]",
                TextSource.DirectWrite => "[DirectWrite]",
                TextSource.ExtTextOut => "[ExtTextOut]",
                TextSource.OCR => "[OCR]",
                _ => "[Unknown]"
            };

            // Entrance animation
            Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);

            // Auto-hide timer (8 seconds)
            _hideTimer = new System.Windows.Threading.DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromSeconds(8);
            _hideTimer.Tick += HideTimer_Tick;
            _hideTimer.Start();

            // Add click to close functionality
            MouseLeftButtonDown += (s, e) => CloseOverlay();
        }

        private void HideTimer_Tick(object? sender, EventArgs e) // <--- CORRIGIDO: Adicionado '?'
        {
            _hideTimer.Stop();
            CloseOverlay();
        }

        private void CloseOverlay()
        {
            _hideTimer?.Stop();

            // Fade out animation
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Make window click-through for game input
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            WindowsServices.SetWindowExTransparent(hwnd);
        }
    }

    // Helper class for Windows API calls
    internal static class WindowsServices
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int GWL_EXSTYLE = (-20);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public static void SetWindowExTransparent(IntPtr hwnd)
        {
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }
    }
}