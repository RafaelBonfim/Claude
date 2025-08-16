using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq; // <--- ADICIONADO PARA CORRIGIR ERRO 'ToArray'
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UniversalGameTranslator.Models;

namespace UniversalGameTranslator.Services
{
    public class TextHookManager : IDisposable
    {
        private readonly Dictionary<int, IntPtr> _hookedProcesses = new();
        private readonly HashSet<string> _seenTexts = new();
        private OCRService? _ocrService; // <--- CORRIGIDO: Adicionado '?'
        private bool _disposed = false;

        public event EventHandler<TextCapturedEventArgs>? TextCaptured; // <--- CORRIGIDO: Adicionado '?'

        // DLL Import for TextHooker.dll
        [DllImport("TextHooker.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern bool InstallHooks(int processId, TextCallbackDelegate callback);

        [DllImport("TextHooker.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool RemoveHooks(int processId);

        [DllImport("TextHooker.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool IsHookActive(int processId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate void TextCallbackDelegate(IntPtr text, int source, int processId);

        private readonly TextCallbackDelegate _textCallback;

        public TextHookManager()
        {
            _textCallback = OnTextFromNative;
        }

        public async Task<bool> HookProcessAsync(int processId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Check if process exists and is accessible
                    var process = Process.GetProcessById(processId);
                    if (process.HasExited) return false;

                    // Remove existing hooks for this process
                    if (_hookedProcesses.ContainsKey(processId))
                    {
                        RemoveHooks(processId);
                        _hookedProcesses.Remove(processId);
                    }

                    // Install new hooks
                    bool success = InstallHooks(processId, _textCallback);

                    if (success)
                    {
                        _hookedProcesses[processId] = process.Handle;
                        Debug.WriteLine($"Successfully hooked process {processId} ({process.ProcessName})");
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to hook process {processId} ({process.ProcessName})");
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error hooking process {processId}: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task StartOCRFallbackAsync(int processId)
        {
            try
            {
                _ocrService ??= new OCRService();
                await _ocrService.StartAsync(processId, OnOCRTextCaptured);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting OCR for process {processId}: {ex.Message}");
            }
        }

        private void OnTextFromNative(IntPtr textPtr, int source, int processId)
        {
            try
            {
                if (textPtr == IntPtr.Zero) return;

                string? text = Marshal.PtrToStringUni(textPtr); // <--- CORRIGIDO: Adicionado '?'
                if (string.IsNullOrWhiteSpace(text)) return;

                text = text.Trim();

                // Avoid duplicate texts
                string textKey = $"{processId}:{text}";
                if (_seenTexts.Contains(textKey)) return;

                _seenTexts.Add(textKey);

                // Limit seen texts cache to prevent memory leaks
                if (_seenTexts.Count > 1000)
                {
                    _seenTexts.Clear();
                }

                // Raise event
                TextCaptured?.Invoke(this, new TextCapturedEventArgs
                {
                    Text = text,
                    Source = (TextSource)source,
                    Timestamp = DateTime.Now,
                    ProcessId = processId
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing text from native: {ex.Message}");
            }
        }

        private void OnOCRTextCaptured(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                TextCaptured?.Invoke(this, new TextCapturedEventArgs
                {
                    Text = text.Trim(),
                    Source = TextSource.OCR,
                    Timestamp = DateTime.Now,
                    ProcessId = 0 // OCR doesn't have specific process ID
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing OCR text: {ex.Message}");
            }
        }

        public void StopHooking(int processId)
        {
            try
            {
                if (_hookedProcesses.ContainsKey(processId))
                {
                    RemoveHooks(processId);
                    _hookedProcesses.Remove(processId);
                    Debug.WriteLine($"Stopped hooking process {processId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping hooks for process {processId}: {ex.Message}");
            }
        }

        public void StopAll()
        {
            try
            {
                foreach (var processId in _hookedProcesses.Keys.ToArray())
                {
                    StopHooking(processId);
                }

                _ocrService?.Stop();
                _seenTexts.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping all hooks: {ex.Message}");
            }
        }

        public bool IsProcessHooked(int processId)
        {
            try
            {
                return _hookedProcesses.ContainsKey(processId) && IsHookActive(processId);
            }
            catch
            {
                return false;
            }
        }

        public List<int> GetHookedProcesses()
        {
            return new List<int>(_hookedProcesses.Keys);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopAll();
                    _ocrService?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~TextHookManager()
        {
            Dispose(false);
        }
    }
}