using System;

namespace UniversalGameTranslator.Models
{
    public class GameProcess
    {
        public string Name { get; set; } = "";
        public int ProcessId { get; set; }
        public string WindowTitle { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public long MemoryUsage { get; set; }
        public DateTime StartTime { get; set; }

        public string DisplayName =>
            string.IsNullOrEmpty(WindowTitle) ? Name : $"{Name} - {WindowTitle}";

        public string MemoryUsageFormatted =>
            MemoryUsage > 0 ? $"{MemoryUsage / (1024 * 1024):N0} MB" : "Unknown";

        public override string ToString() => DisplayName;

        public override bool Equals(object? obj) // <--- CORRIGIDO: Adicionado '?'
        {
            return obj is GameProcess other && ProcessId == other.ProcessId;
        }

        public override int GetHashCode()
        {
            return ProcessId.GetHashCode();
        }
    }

    public class TranslationEntry
    {
        public string OriginalText { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public TextSource Source { get; set; }

        public string TimeFormatted => Timestamp.ToString("HH:mm:ss");

        public string SourceDisplayName => Source switch
        {
            TextSource.DrawText => "DrawText API",
            TextSource.TextOut => "TextOut API",
            TextSource.DirectWrite => "DirectWrite API",
            TextSource.ExtTextOut => "ExtTextOut API",
            TextSource.OCR => "OCR Capture",
            _ => "Unknown"
        };
    }

    public class TextCapturedEventArgs : EventArgs
    {
        public string Text { get; set; } = "";
        public TextSource Source { get; set; }
        public DateTime Timestamp { get; set; }
        public int ProcessId { get; set; }
    }

    public enum TextSource
    {
        DrawText = 0,
        TextOut = 1,
        DirectWrite = 2,
        ExtTextOut = 3,
        OCR = 4,
        MemoryScanning = 5
    }

    public class HookResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public TextSource Method { get; set; }
        public int ProcessId { get; set; }

        public static HookResult CreateSuccess(TextSource method, int processId, string message = "")
        {
            return new HookResult
            {
                Success = true,
                Method = method,
                ProcessId = processId,
                Message = message
            };
        }

        public static HookResult CreateFailure(string message)
        {
            return new HookResult
            {
                Success = false,
                Message = message
            };
        }
    }

    public class TranslationRequest
    {
        public string Text { get; set; } = "";
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "pt";
        public DateTime RequestTime { get; set; } = DateTime.Now;
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
    }

    public class TranslationResponse
    {
        public string TranslatedText { get; set; } = "";
        public string DetectedLanguage { get; set; } = "";
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public string RequestId { get; set; } = "";
    }

    public class OCRResult
    {
        public string Text { get; set; } = "";
        public float Confidence { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public TimeSpan ProcessingTime { get; set; }
        public DateTime CaptureTime { get; set; }
    }

    public class GameConfiguration
    {
        public string ProcessName { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public bool EnableHooking { get; set; } = true;
        public bool EnableOCR { get; set; } = true;
        public bool ShowOverlay { get; set; } = true;
        public string PreferredLanguage { get; set; } = "pt";
        public int MaxTranslations { get; set; } = 50;
        public bool FilterDebugText { get; set; } = true;
        public DateTime LastUsed { get; set; } = DateTime.Now;

        public override string ToString() => ProcessName;
    }
}