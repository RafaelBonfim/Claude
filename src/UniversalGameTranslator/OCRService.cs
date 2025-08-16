using System;
using System.Collections.Generic; // <--- ADICIONADO PARA CORRIGIR O ERRO DO HASHSET
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using UniversalGameTranslator.Models;

namespace UniversalGameTranslator.Services
{
    public class OCRService : IDisposable
    {
        private bool _isRunning;
        private int _targetProcessId;
        private Action<string>? _textCallback; // <--- CORRIGIDO: Adicionado '?'
        private CancellationTokenSource? _cancellationTokenSource; // <--- CORRIGIDO: Adicionado '?'
        private string _lastCapturedText = "";
        private readonly object _lockObject = new object();

        // Windows OCR API (if available)
        private bool _useWindowsOCR = true;

        // Simple OCR using basic text recognition
        private readonly HashSet<string> _commonGameTexts = new()
        {
            "press", "start", "continue", "exit", "menu", "options", "settings",
            "play", "pause", "resume", "quit", "save", "load", "new game",
            "health", "mana", "level", "score", "points", "coins", "gold"
        };

        public async Task StartAsync(int processId, Action<string> callback)
        {
            lock (_lockObject)
            {
                if (_isRunning) return;

                _targetProcessId = processId;
                _textCallback = callback;
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            await Task.Run(async () => await OCRLoop(_cancellationTokenSource.Token));
        }

        private async Task OCRLoop(CancellationToken cancellationToken)
        {
            var process = GetTargetProcess();
            if (process == null) return;

            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Check if process is still running
                    if (process.HasExited)
                    {
                        Debug.WriteLine("Target process has exited, stopping OCR");
                        break;
                    }

                    var ocrResult = await CaptureAndProcessScreenAsync(process);

                    if (ocrResult.Success && !string.IsNullOrWhiteSpace(ocrResult.Text))
                    {
                        // Avoid duplicate texts
                        if (ocrResult.Text != _lastCapturedText)
                        {
                            _lastCapturedText = ocrResult.Text;
                            _textCallback?.Invoke(ocrResult.Text);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"OCR Loop Error: {ex.Message}");
                }

                // Wait before next capture (adjust based on performance needs)
                await Task.Delay(2000, cancellationToken); // 2 seconds interval
            }
        }

        private Process? GetTargetProcess() // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                return Process.GetProcessById(_targetProcessId);
            }
            catch
            {
                return null;
            }
        }

        private async Task<OCRResult> CaptureAndProcessScreenAsync(Process process)
        {
            var startTime = DateTime.Now;

            try
            {
                // Get window rectangle
                var windowRect = GetWindowRectangle(process);
                if (windowRect.IsEmpty)
                {
                    return new OCRResult
                    {
                        Success = false,
                        ErrorMessage = "Could not get window rectangle"
                    };
                }

                // Capture screenshot of game window
                using var screenshot = CaptureWindow(windowRect);
                if (screenshot == null)
                {
                    return new OCRResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to capture screenshot"
                    };
                }

                // Process image for better OCR
                using var processedImage = PreprocessImageForOCR(screenshot);

                // Perform OCR
                var text = await PerformOCR(processedImage);

                return new OCRResult
                {
                    Text = text,
                    Success = !string.IsNullOrWhiteSpace(text),
                    ProcessingTime = DateTime.Now - startTime,
                    CaptureTime = DateTime.Now,
                    Confidence = CalculateConfidence(text)
                };
            }
            catch (Exception ex)
            {
                return new OCRResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTime = DateTime.Now - startTime
                };
            }
        }

        private Rectangle GetWindowRectangle(Process process)
        {
            try
            {
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) return Rectangle.Empty;

                NativeMethods.GetWindowRect(handle, out var rect);
                return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            catch
            {
                return Rectangle.Empty;
            }
        }

        private Bitmap? CaptureWindow(Rectangle windowRect) // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                // Focus on likely text areas (bottom portion of screen for subtitles, etc.)
                var textArea = new Rectangle(
                    windowRect.X,
                    windowRect.Y + (windowRect.Height * 2 / 3), // Bottom third
                    windowRect.Width,
                    windowRect.Height / 3
                );

                var bitmap = new Bitmap(textArea.Width, textArea.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(textArea.Location, Point.Empty, textArea.Size);

                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Screenshot capture failed: {ex.Message}");
                return null;
            }
        }

        private Bitmap PreprocessImageForOCR(Bitmap original)
        {
            try
            {
                // Create a copy for processing
                var processed = new Bitmap(original.Width, original.Height);

                using var graphics = Graphics.FromImage(processed);

                // Convert to grayscale and increase contrast
                var colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {0.3f, 0.3f, 0.3f, 0, 0},
                    new float[] {0.59f, 0.59f, 0.59f, 0, 0},
                    new float[] {0.11f, 0.11f, 0.11f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0.2f, 0.2f, 0.2f, 0, 1} // Increase brightness
                });

                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                graphics.DrawImage(original,
                    new Rectangle(0, 0, processed.Width, processed.Height),
                    0, 0, original.Width, original.Height,
                    GraphicsUnit.Pixel, attributes);

                return processed;
            }
            catch
            {
                // Return original if processing fails
                return new Bitmap(original);
            }
        }

        private async Task<string> PerformOCR(Bitmap image)
        {
            try
            {
                if (_useWindowsOCR)
                {
                    return await PerformWindowsOCR(image);
                }
                else
                {
                    return await PerformBasicTextDetection(image);
                }
            }
            catch
            {
                // Fallback to basic detection
                return await PerformBasicTextDetection(image);
            }
        }

        private async Task<string> PerformWindowsOCR(Bitmap image)
        {
            try
            {
                // This is a placeholder for Windows OCR API integration
                // In a full implementation, you would use Windows.Media.Ocr
                // For now, fall back to basic detection
                return await PerformBasicTextDetection(image);
            }
            catch
            {
                return "";
            }
        }

        private async Task<string> PerformBasicTextDetection(Bitmap image)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Very basic "OCR" - looks for text-like patterns
                    // This is a simplified approach and won't work as well as real OCR
                    // In a production version, integrate with Tesseract or Windows OCR

                    var result = AnalyzeImageForText(image);
                    return result;
                }
                catch
                {
                    return "";
                }
            });
        }

        private string AnalyzeImageForText(Bitmap image)
        {
            // This is a very basic placeholder implementation
            // Real OCR would use libraries like Tesseract

            // For demonstration, return empty string
            // In production, integrate proper OCR library
            return "";
        }

        private float CalculateConfidence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0f;

            // Simple confidence calculation based on common game terms
            var lowerText = text.ToLower();
            var matches = 0;

            foreach (var commonText in _commonGameTexts)
            {
                if (lowerText.Contains(commonText))
                    matches++;
            }

            return Math.Min(1.0f, matches / 5.0f); // Max confidence based on matches
        }

        public void Stop()
        {
            lock (_lockObject)
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel();
            }
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }

    // Native methods for window handling
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}