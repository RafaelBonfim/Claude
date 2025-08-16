using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UniversalGameTranslator.Models;

namespace UniversalGameTranslator.Services
{
    public class GameDetector
    {
        private readonly HashSet<string> _gameKeywords = new()
        {
            // Game engines
            "unity", "unreal", "godot", "gamemaker", "construct",

            // Graphics APIs
            "directx", "opengl", "vulkan", "dx11", "dx12",

            // Game-related terms
            "game", "player", "rpg", "adventure", "action",

            // Common game executables
            "game.exe", "launcher.exe", "client.exe"
        };

        private readonly HashSet<string> _excludeProcesses = new()
        {
            // System processes
            "explorer", "dwm", "winlogon", "csrss", "lsass", "services", "svchost",
            "taskhostw", "taskmgr", "registry", "fontdrvhost", "consent",

            // Development tools
            "devenv", "code", "notepad", "notepad++", "sublime", "atom",

            // Browsers (usually not games)
            "chrome", "firefox", "edge", "opera", "brave",

            // System utilities
            "cmd", "powershell", "conhost", "wininit", "smss"
        };

        public async Task<List<GameProcess>> DetectGamesAsync()
        {
            return await Task.Run(() =>
            {
                var detectedGames = new List<GameProcess>();

                try
                {
                    var processes = Process.GetProcesses()
                        .Where(p => IsLikelyGame(p))
                        .ToList();

                    foreach (var process in processes)
                    {
                        try
                        {
                            var gameProcess = CreateGameProcess(process);
                            if (gameProcess != null)
                            {
                                detectedGames.Add(gameProcess);
                            }
                        }
                        catch
                        {
                            // Skip processes we can't access
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error detecting games: {ex.Message}");
                }

                return detectedGames.OrderBy(g => g.Name).ToList();
            });
        }

        private bool IsLikelyGame(Process process)
        {
            try
            {
                // Basic checks
                if (process == null || process.HasExited) return false;
                if (string.IsNullOrEmpty(process.ProcessName)) return false;
                if (process.ProcessName.Length < 3) return false;

                var processName = process.ProcessName.ToLower();

                // Exclude known non-game processes
                if (_excludeProcesses.Any(excluded => processName.Contains(excluded)))
                    return false;

                // Must have a main window (GUI application)
                if (process.MainWindowHandle == IntPtr.Zero)
                    return false;

                // Check for game indicators
                if (HasGameIndicators(process))
                    return true;

                // Check memory usage (games typically use more memory)
                if (HasSignificantMemoryUsage(process))
                    return true;

                // Check window properties
                if (HasGameLikeWindow(process))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool HasGameIndicators(Process process)
        {
            var processName = process.ProcessName.ToLower();
            var windowTitle = (process.MainWindowTitle ?? "").ToLower();
            var executablePath = GetSafeExecutablePath(process).ToLower();

            // Check process name
            if (_gameKeywords.Any(keyword => processName.Contains(keyword)))
                return true;

            // Check window title
            if (_gameKeywords.Any(keyword => windowTitle.Contains(keyword)))
                return true;

            // Check executable path
            if (_gameKeywords.Any(keyword => executablePath.Contains(keyword)))
                return true;

            // Common game patterns
            if (processName.EndsWith("game") || processName.StartsWith("game"))
                return true;

            // Check for common game directories
            if (executablePath.Contains("games") ||
                executablePath.Contains("steam") ||
                executablePath.Contains("epic") ||
                executablePath.Contains("gog"))
                return true;

            return false;
        }

        private bool HasSignificantMemoryUsage(Process process)
        {
            try
            {
                // Games typically use more than 50MB of working set
                return process.WorkingSet64 > 50 * 1024 * 1024;
            }
            catch
            {
                return false;
            }
        }

        private bool HasGameLikeWindow(Process process)
        {
            try
            {
                // Has meaningful window title (not just process name)
                var windowTitle = process.MainWindowTitle;
                if (string.IsNullOrEmpty(windowTitle))
                    return false;

                // Window title is different from process name (indicates custom UI)
                if (windowTitle.ToLower() != process.ProcessName.ToLower())
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private GameProcess? CreateGameProcess(Process process) // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                return new GameProcess
                {
                    Name = process.ProcessName,
                    ProcessId = process.Id,
                    WindowTitle = process.MainWindowTitle ?? "",
                    ExecutablePath = GetSafeExecutablePath(process),
                    MemoryUsage = GetSafeMemoryUsage(process),
                    StartTime = GetSafeStartTime(process)
                };
            }
            catch
            {
                return null;
            }
        }

        private string GetSafeExecutablePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "";
            }
            catch
            {
                return "";
            }
        }

        private long GetSafeMemoryUsage(Process process)
        {
            try
            {
                return process.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }

        private DateTime GetSafeStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        public bool IsProcessStillRunning(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        public GameProcess? GetGameProcessById(int processId) // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                var process = Process.GetProcessById(processId);
                return CreateGameProcess(process);
            }
            catch
            {
                return null;
            }
        }
    }
}