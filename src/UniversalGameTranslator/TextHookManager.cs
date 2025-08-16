using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UniversalGameTranslator.Models;

namespace UniversalGameTranslator.Services
{
    public class TextHookManager : IDisposable
    {
        private readonly Dictionary<int, IntPtr> _hookedProcesses = new();
        private readonly HashSet<string> _seenTexts = new();
        private OCRService? _ocrService;
        private bool _disposed = false;

        public event EventHandler<TextCapturedEventArgs>? TextCaptured;

        // Native Windows API imports for DLL injection
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetLastError();

        // Constants
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint MEM_RELEASE = 0x8000;
        private const uint INFINITE = 0xFFFFFFFF;

        public async Task<bool> HookProcessAsync(int processId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (process.HasExited)
                    {
                        Debug.WriteLine($"Process {processId} has already exited");
                        return false;
                    }

                    Debug.WriteLine($"Attempting to hook process {processId} ({process.ProcessName})");

                    // Check admin privileges
                    if (!IsRunningAsAdmin())
                    {
                        Debug.WriteLine("WARNING: Not running as administrator. DLL injection will likely fail.");
                        return false;
                    }

                    // Check if TextHooker.dll exists
                    string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TextHooker.dll");
                    if (!File.Exists(dllPath))
                    {
                        Debug.WriteLine($"TextHooker.dll not found at: {dllPath}");
                        
                        // Try alternative locations
                        string[] alternativePaths = {
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x64", "TextHooker.dll"),
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug", "TextHooker.dll"),
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Release", "TextHooker.dll")
                        };

                        foreach (string altPath in alternativePaths)
                        {
                            if (File.Exists(altPath))
                            {
                                dllPath = altPath;
                                Debug.WriteLine($"Found TextHooker.dll at alternative location: {altPath}");
                                break;
                            }
                        }

                        if (!File.Exists(dllPath))
                        {
                            Debug.WriteLine("TextHooker.dll not found in any expected location");
                            return false;
                        }
                    }

                    // Remove existing hooks for this process
                    if (_hookedProcesses.ContainsKey(processId))
                    {
                        UnhookProcess(processId);
                    }

                    // Try DLL injection with detailed error reporting
                    bool success = InjectDLLWithDetailedLogging(processId, dllPath);

                    if (success)
                    {
                        _hookedProcesses[processId] = process.Handle;
                        Debug.WriteLine($"Successfully injected DLL into process {processId} ({process.ProcessName})");
                        
                        // Start monitoring for text
                        _ = Task.Run(() => MonitorProcess(processId));
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error hooking process {processId}: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    return false;
                }
            });
        }

        private bool InjectDLLWithDetailedLogging(int processId, string dllPath)
        {
            IntPtr hProcess = IntPtr.Zero;
            IntPtr pRemoteMemory = IntPtr.Zero;
            IntPtr hRemoteThread = IntPtr.Zero;

            try
            {
                Debug.WriteLine($"Starting DLL injection for process {processId}");
                Debug.WriteLine($"DLL Path: {dllPath}");

                // Step 1: Open target process
                Debug.WriteLine("Step 1: Opening target process...");
                hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
                if (hProcess == IntPtr.Zero)
                {
                    uint error = GetLastError();
                    Debug.WriteLine($"Failed to open process {processId}. Error code: {error}");
                    
                    // Try with fewer permissions
                    const uint PROCESS_CREATE_THREAD = 0x0002;
                    const uint PROCESS_QUERY_INFORMATION = 0x0400;
                    const uint PROCESS_VM_OPERATION = 0x0008;
                    const uint PROCESS_VM_WRITE = 0x0020;
                    const uint PROCESS_VM_READ = 0x0010;
                    
                    uint reducedAccess = PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | 
                                       PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ;
                    
                    hProcess = OpenProcess(reducedAccess, false, processId);
                    if (hProcess == IntPtr.Zero)
                    {
                        error = GetLastError();
                        Debug.WriteLine($"Failed to open process with reduced permissions. Error code: {error}");
                        return false;
                    }
                    Debug.WriteLine("Opened process with reduced permissions");
                }
                else
                {
                    Debug.WriteLine("Successfully opened process with full access");
                }

                // Step 2: Get LoadLibrary address
                Debug.WriteLine("Step 2: Getting LoadLibraryW address...");
                IntPtr hKernel32 = GetModuleHandle("kernel32.dll");
                if (hKernel32 == IntPtr.Zero)
                {
                    Debug.WriteLine("Failed to get kernel32.dll handle");
                    return false;
                }

                IntPtr pLoadLibrary = GetProcAddress(hKernel32, "LoadLibraryW");
                if (pLoadLibrary == IntPtr.Zero)
                {
                    Debug.WriteLine("Failed to get LoadLibraryW address");
                    return false;
                }
                Debug.WriteLine($"LoadLibraryW address: 0x{pLoadLibrary.ToInt64():X}");

                // Step 3: Allocate memory in target process
                Debug.WriteLine("Step 3: Allocating memory in target process...");
                byte[] dllBytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");
                Debug.WriteLine($"DLL path bytes length: {dllBytes.Length}");

                pRemoteMemory = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)dllBytes.Length, 
                    MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                
                if (pRemoteMemory == IntPtr.Zero)
                {
                    uint error = GetLastError();
                    Debug.WriteLine($"Failed to allocate memory in target process. Error code: {error}");
                    return false;
                }
                Debug.WriteLine($"Allocated memory at: 0x{pRemoteMemory.ToInt64():X}");

                // Step 4: Write DLL path to target process
                Debug.WriteLine("Step 4: Writing DLL path to target process...");
                if (!WriteProcessMemory(hProcess, pRemoteMemory, dllBytes, (uint)dllBytes.Length, out int bytesWritten))
                {
                    uint error = GetLastError();
                    Debug.WriteLine($"Failed to write DLL path to target process. Error code: {error}");
                    return false;
                }
                Debug.WriteLine($"Successfully wrote {bytesWritten} bytes to target process");

                // Step 5: Create remote thread to load DLL
                Debug.WriteLine("Step 5: Creating remote thread to load DLL...");
                hRemoteThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, pLoadLibrary, pRemoteMemory, 0, IntPtr.Zero);
                if (hRemoteThread == IntPtr.Zero)
                {
                    uint error = GetLastError();
                    Debug.WriteLine($"Failed to create remote thread. Error code: {error}");
                    return false;
                }
                Debug.WriteLine("Successfully created remote thread");

                // Step 6: Wait for injection to complete
                Debug.WriteLine("Step 6: Waiting for injection to complete...");
                uint waitResult = WaitForSingleObject(hRemoteThread, 10000); // 10 second timeout
                
                switch (waitResult)
                {
                    case 0: // WAIT_OBJECT_0
                        Debug.WriteLine("Remote thread completed successfully");
                        break;
                    case 0x102: // WAIT_TIMEOUT
                        Debug.WriteLine("Remote thread timed out");
                        return false;
                    default:
                        Debug.WriteLine($"WaitForSingleObject returned unexpected value: {waitResult}");
                        return false;
                }

                Debug.WriteLine($"DLL successfully injected into process {processId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception during DLL injection: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
            finally
            {
                // Cleanup
                if (hRemoteThread != IntPtr.Zero)
                {
                    CloseHandle(hRemoteThread);
                }

                if (pRemoteMemory != IntPtr.Zero && hProcess != IntPtr.Zero)
                {
                    VirtualFreeEx(hProcess, pRemoteMemory, 0, MEM_RELEASE);
                }

                if (hProcess != IntPtr.Zero)
                {
                    CloseHandle(hProcess);
                }
            }
        }

        private async Task MonitorProcess(int processId)
        {
            string tempPath = Path.GetTempPath();
            string communicationFile = Path.Combine(tempPath, $"UGT_Process_{processId}.txt");
            
            Debug.WriteLine($"Monitoring communication file: {communicationFile}");

            try
            {
                int emptyReadCount = 0;
                const int maxEmptyReads = 100; // Stop after 10 seconds of no activity

                while (_hookedProcesses.ContainsKey(processId))
                {
                    if (File.Exists(communicationFile))
                    {
                        try
                        {
                            string[] lines = File.ReadAllLines(communicationFile);
                            if (lines.Length > 0)
                            {
                                Debug.WriteLine($"Read {lines.Length} lines from communication file");
                                File.Delete(communicationFile); // Clear after reading
                                emptyReadCount = 0; // Reset counter
                                
                                foreach (string line in lines)
                                {
                                    if (!string.IsNullOrWhiteSpace(line) && !line.Equals("[HOOK_ACTIVE]") && !line.Equals("[HOOK_REMOVED]"))
                                    {
                                        Debug.WriteLine($"Processing captured text: {line}");
                                        ProcessCapturedText(line, processId);
                                    }
                                    else if (line.Equals("[HOOK_ACTIVE]"))
                                    {
                                        Debug.WriteLine("Hook is confirmed active in target process");
                                    }
                                }
                            }
                            else
                            {
                                emptyReadCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error reading communication file: {ex.Message}");
                        }
                    }
                    else
                    {
                        emptyReadCount++;
                    }

                    // Check if process is still running
                    try
                    {
                        var process = Process.GetProcessById(processId);
                        if (process.HasExited)
                        {
                            Debug.WriteLine($"Process {processId} has exited, stopping monitoring");
                            break;
                        }
                    }
                    catch
                    {
                        Debug.WriteLine($"Process {processId} no longer exists, stopping monitoring");
                        break;
                    }

                    if (emptyReadCount >= maxEmptyReads)
                    {
                        Debug.WriteLine($"No activity detected for process {processId}, stopping monitoring");
                        break;
                    }
                    
                    await Task.Delay(100); // Check every 100ms
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error monitoring process {processId}: {ex.Message}");
            }
            finally
            {
                // Cleanup
                if (_hookedProcesses.ContainsKey(processId))
                {
                    _hookedProcesses.Remove(processId);
                }

                try
                {
                    if (File.Exists(communicationFile))
                    {
                        File.Delete(communicationFile);
                    }
                }
                catch { }
            }
        }

        private void ProcessCapturedText(string text, int processId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                text = text.Trim();
                Debug.WriteLine($"Processing text from process {processId}: '{text}'");

                // Avoid duplicate texts
                string textKey = $"{processId}:{text}";
                if (_seenTexts.Contains(textKey))
                {
                    Debug.WriteLine("Text already seen, ignoring duplicate");
                    return;
                }

                _seenTexts.Add(textKey);

                // Limit seen texts cache to prevent memory leaks
                if (_seenTexts.Count > 1000)
                {
                    Debug.WriteLine("Clearing text cache to prevent memory leak");
                    _seenTexts.Clear();
                }

                // Raise event
                Debug.WriteLine($"Raising TextCaptured event for: '{text}'");
                TextCaptured?.Invoke(this, new TextCapturedEventArgs
                {
                    Text = text,
                    Source = TextSource.DrawText,
                    Timestamp = DateTime.Now,
                    ProcessId = processId
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing captured text: {ex.Message}");
            }
        }

        public async Task StartOCRFallbackAsync(int processId)
        {
            try
            {
                Debug.WriteLine($"Starting OCR fallback for process {processId}");
                _ocrService ??= new OCRService();
                await _ocrService.StartAsync(processId, OnOCRTextCaptured);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting OCR for process {processId}: {ex.Message}");
            }
        }

        private void OnOCRTextCaptured(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;

                Debug.WriteLine($"OCR captured text: '{text}'");

                TextCaptured?.Invoke(this, new TextCapturedEventArgs
                {
                    Text = text.Trim(),
                    Source = TextSource.OCR,
                    Timestamp = DateTime.Now,
                    ProcessId = 0
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing OCR text: {ex.Message}");
            }
        }

        private bool IsRunningAsAdmin()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                Debug.WriteLine($"Running as administrator: {isAdmin}");
                return isAdmin;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking admin status: {ex.Message}");
                return false;
            }
        }

        public void UnhookProcess(int processId)
        {
            try
            {
                Debug.WriteLine($"Unhooking process {processId}");
                
                if (_hookedProcesses.ContainsKey(processId))
                {
                    _hookedProcesses.Remove(processId);
                }

                // Clean up communication file
                string tempPath = Path.GetTempPath();
                string communicationFile = Path.Combine(tempPath, $"UGT_Process_{processId}.txt");
                if (File.Exists(communicationFile))
                {
                    try
                    {
                        File.Delete(communicationFile);
                        Debug.WriteLine($"Deleted communication file: {communicationFile}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting communication file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error unhooking process {processId}: {ex.Message}");
            }
        }

        public void StopAll()
        {
            try
            {
                Debug.WriteLine("Stopping all hooks...");
                
                foreach (var processId in _hookedProcesses.Keys.ToArray())
                {
                    UnhookProcess(processId);
                }

                _ocrService?.Stop();
                _seenTexts.Clear();
                
                Debug.WriteLine("All hooks stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping all hooks: {ex.Message}");
            }
        }

        public bool IsProcessHooked(int processId)
        {
            return _hookedProcesses.ContainsKey(processId);
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