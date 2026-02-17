using LmpCommon;
using Microsoft.VisualStudio.Threading;
using Server.Command;
using Server.Context;
using Server.Log;
using Server.Settings.Structures;
using Server.System;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using WarpContext = Server.Context.WarpContext;
using Subspace = Server.Context.Subspace;

namespace Server.Tui
{
    public static class TuiEngine
    {
        private static readonly ConcurrentQueue<string> LogQueue = new ConcurrentQueue<string>();
        private static readonly List<string> DisplayLogs = new List<string>();
        private const int MaxLogs = 200;
        private static bool _running = true;
        private static string _currentInput = "";

        private static bool _showDebug = true;
        private static bool _showNormal = true;
        private static bool _showWarning = true;
        private static bool _showError = true;
        private static bool _showChat = true;

        private static int _selectedPlayerIndex = 0;
        private static bool _sidebarFocused = false;
        private static bool _needsRedraw = true;
        private static int _scrollOffset = 0;
        private static TuiTab _activeTab = TuiTab.Console;
        private static DateTime _lastPeriodicUpdate = DateTime.MinValue;
        private static DateTime _lastDrawTime = DateTime.MinValue;
        private static int _lastWidth = 0;
        private static int _lastHeight = 0;
        private static bool _resolveGuids = false;
        private static readonly Regex GuidRegex = new Regex(@"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}");
        private static readonly Regex KerbalRegex = new Regex(@"\b([A-Z][a-z]+ Kerman|Jebediah|Bill|Bob|Valentina)\b");
        private static readonly HashSet<string> AllSessionPlayerNames = new HashSet<string>();

        public static void Start(CancellationToken token)
        {
            CommandHandler.UseStandardConsole = false;
            BaseLogger.SilenceConsole = true;
            LunaLog.OnLog += OnLogReceived;

            try 
            {
                Console.CursorVisible = false;
                Console.Clear();
            } 
            catch { /* Ignore if terminal doesn't support */ }

            var tuiThread = new Thread(() => 
            {
                try 
                {
                    new JoinableTaskContext().Factory.Run(() => RunLoopAsync(token));
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                }
                catch (Exception e)
                {
                    Console.Clear();
                    Console.WriteLine($"TUI CRASHED: {e}");
                }
                finally
                {
                    try { Console.CursorVisible = true; } catch {}
                    BaseLogger.SilenceConsole = false;
                }
            });
            tuiThread.IsBackground = true;
            tuiThread.Start();
        }

        private static void OnLogReceived(string line)
        {
            LogQueue.Enqueue(line);
            if (LogQueue.Count > MaxLogs)
            {
                LogQueue.TryDequeue(out _);
            }
            _needsRedraw = true;
        }

        private static async Task RunLoopAsync(CancellationToken token)
        {
            try
            {
                // Enter alternate buffer and hide cursor
                AnsiConsole.Write(new ControlCode("\u001b[?1049h"));
                AnsiConsole.Write(new ControlCode("\u001b[?25l"));

                while (!token.IsCancellationRequested && _running)
                {
                    var logUpdated = UpdateLogs();
                    if (logUpdated) _needsRedraw = true;

                    var now = DateTime.Now;
                    
                    // Detect window resize
                    if (Console.WindowWidth != _lastWidth || Console.WindowHeight != _lastHeight)
                    {
                        _lastWidth = Console.WindowWidth;
                        _lastHeight = Console.WindowHeight;
                        _needsRedraw = true;
                        Console.Clear();
                    }

                    // Redraw only if needed and at most 10 times per second, or every 1 second for pulse
                    if ((_needsRedraw && (now - _lastDrawTime).TotalMilliseconds >= 100) || (now - _lastPeriodicUpdate).TotalSeconds >= 1) 
                    {
                        DrawManual();
                        _needsRedraw = false;
                        _lastPeriodicUpdate = now;
                        _lastDrawTime = now;
                    }
                    
                    if (Console.KeyAvailable)
                    {
                        HandleInput(Console.ReadKey(true));
                        _needsRedraw = true;
                    }

                    await Task.Delay(50, token);
                }
            }
            finally
            {
                // Exit alternate buffer and show cursor
                AnsiConsole.Write(new ControlCode("\u001b[?25h"));
                AnsiConsole.Write(new ControlCode("\u001b[?1049l"));
            }
        }

        private static void DrawManual()
        {
            var serverName = GeneralSettings.SettingsStore?.ServerName ?? "Unknown";
            var version = LmpVersioning.CurrentVersion.ToString();
            var uptime = (DateTime.UtcNow - TimeContext.StartTime).ToString(@"dd\.hh\:mm\:ss");
            var status = ServerContext.ServerRunning ? "RUNNING" : "STOPPED";
            var players = ServerContext.Clients.Values.Select(c => c.PlayerName).ToList();
            var subspaceCount = WarpContext.Subspaces.Count;
            var vesselCount = VesselStoreSystem.CurrentVessels.Count;
            var memoryUsage = Environment.WorkingSet;

            var subspaceData = WarpContext.Subspaces.Values.Select(s => new SubspaceInfo
            {
                Id = s.Id,
                Time = s.Time,
                Players = ServerContext.Clients.Values.Where(c => c.Subspace == s.Id).Select(c => c.PlayerName).ToList()
            }).ToList();

            var filteredLogs = DisplayLogs.Where(l => 
            {
                if (l.Contains("[Debug]") && !_showDebug) return false;
                if (l.Contains("[LMP]") && !_showNormal) return false;
                if (l.Contains("[Warning]") && !_showWarning) return false;
                if (l.Contains("[Error]") && !_showError) return false;
                if (l.Contains("[Chat]") && !_showChat) return false;
                // Suppress backup logs from TUI console
                if (l.Contains("Performing backups...") || l.Contains("Backups done")) return false;
                return true;
            });

            // Escape all logs to be Markup-safe, then apply specific highlighting
            var safeLogs = filteredLogs.Select(l => Markup.Escape(l));

            // Get current player names for highlighting
            var currentPlayerNames = ServerContext.Clients.Values.Select(c => c.PlayerName).ToList();
            foreach (var name in currentPlayerNames.Where(n => !string.IsNullOrEmpty(n)))
            {
                AllSessionPlayerNames.Add(name);
            }

            filteredLogs = safeLogs.Select(l => 
            {
                // Build player pattern pattern dynamically
                var validPlayerNames = AllSessionPlayerNames
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => Regex.Escape(n))
                    .ToArray();
                
                var playerPattern = validPlayerNames.Length > 0 
                    ? $@"\b(?:{string.Join("|", validPlayerNames)})\b" 
                    : @"$ ^"; // Pattern that never matches

                // Combined Regex: GUID | Kerbal | Player
                var combinedPattern = $@"({GuidRegex})|({KerbalRegex})|({playerPattern})";
                
                var highlighted = Regex.Replace(l, combinedPattern, match => 
                {
                    var val = match.Value;
                    if (string.IsNullOrEmpty(val)) return val;

                    // 1. Handle GUID
                    if (GuidRegex.IsMatch(val))
                    {
                        if (Guid.TryParse(val, out var id) && VesselStoreSystem.CurrentVessels.TryGetValue(id, out var vessel))
                        {
                            if (_resolveGuids)
                            {
                                var name = vessel.Fields.GetSingle("name")?.Value ?? "Unknown";
                                // Colorize vessel name in yellow
                                return $"[yellow]{Markup.Escape(name)}[/]";
                            }
                            else
                            {
                                // Just colorize the GUID in yellow if resolution is off
                                return $"[yellow]{val}[/]";
                            }
                        }
                        // Even if not found, colorize GUID
                        return $"[yellow]{val}[/]";
                    }

                    // 2. Handle Kerbal
                    if (KerbalRegex.IsMatch(val))
                    {
                        return $"[cyan]{val}[/]";
                    }

                    // 3. Handle Player (Default fallback if matched)
                    return $"[green1]{val}[/]";
                });

                // Apply log level colors directly here
                if (highlighted.Contains("[[Error]]")) highlighted = highlighted.Replace("[[Error]]", "[red][[Error]][/]");
                else if (highlighted.Contains("[[Warning]]")) highlighted = highlighted.Replace("[[Warning]]", "[yellow][[Warning]][/]");
                else if (highlighted.Contains("[[Debug]]")) highlighted = highlighted.Replace("[[Debug]]", "[grey][[Debug]][/]");
                else if (highlighted.Contains("[[Chat]]")) highlighted = highlighted.Replace("[[Chat]]", "[magenta][[Chat]][/]");

                return highlighted;
            }).ToList();

            // Calculate dynamic dimensions
            var terminalHeight = Console.WindowHeight;
            var terminalWidth = Console.WindowWidth;
            var logHeight = Math.Max(5, terminalHeight - 12); 
            var logWidth = Math.Max(10, terminalWidth - 34); 

            // Handle scrolling
            var logsList = filteredLogs.Cast<string>().ToList();
            var totalFiltered = logsList.Count;
            var logsToDisplay = logsList
                .Skip(Math.Max(0, totalFiltered - logHeight - _scrollOffset))
                .Take(logHeight)
                .ToList();

            var playerDetails = ServerContext.Clients.Values.Select(c => new PlayerTuiInfo
            {
                Name = c.PlayerName,
                Ping = (int)(c.Connection?.AverageRoundtripTime * 1000 ?? 0),
                OnlineTime = DateTime.UtcNow - c.ConnectionTime,
                LockCount = LockSystem.LockQuery.GetAllPlayerLocks(c.PlayerName).Count(),
                KspVersion = string.IsNullOrWhiteSpace(c.KspVersion) ? "N/A" : c.KspVersion
            }).ToList();

            var vesselDetails = VesselStoreSystem.CurrentVessels.Values.Select(v => {
                var pidStr = v.Fields.GetSingle("pid")?.Value ?? v.Fields.GetSingle("id")?.Value;
                var vesselId = Guid.TryParse(pidStr, out var id) ? id : Guid.Empty;
                var controlLock = LockSystem.LockQuery.GetControlLock(vesselId);
                var orbitingBody = v.GetOrbitingBodyName();
                var situation = v.Fields.GetSingle("sit")?.Value ?? "Unknown";

                return new VesselTuiInfo
                {
                    Name = v.Fields.GetSingle("name")?.Value ?? "Unknown",
                    VesselId = pidStr,
                    Controller = controlLock?.PlayerName ?? "None",
                    PartCount = v.Parts.Count(),
                    Location = $"{situation} @ {orbitingBody}",
                    IsControlled = controlLock != null
                };
            }).ToList();

            var layout = TuiLayout.Generate(
                serverName, version, uptime, status, 
                logsToDisplay, players, subspaceCount, vesselCount, memoryUsage, _currentInput,
                subspaceData, _sidebarFocused ? _selectedPlayerIndex : -1,
                _activeTab, playerDetails, vesselDetails, logHeight, logWidth,
                BackupSystem.LastBackupTime,
                _resolveGuids
            );

            // Atomic Frame Buffering: Render to StringWriter then write to console at 0,0
            using (var sw = new StringWriter())
            {
                var console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(sw),
                    ColorSystem = ColorSystemSupport.TrueColor, // Force TrueColor for consistent buffer output
                    Interactive = InteractionSupport.No
                });
                console.Profile.Width = terminalWidth;
                console.Profile.Height = terminalHeight;

                console.Write(layout);

                // Write the whole frame at once. 
                // We use SetCursorPosition(0,0) and then Write to avoid flickering.
                var output = sw.ToString();
                Console.SetCursorPosition(0, 0);
                Console.Write(output);
            }
        }

        private static bool UpdateLogs()
        {
            var updated = false;
            while (LogQueue.TryDequeue(out var log))
            {
                DisplayLogs.Add(log);
                if (DisplayLogs.Count > MaxLogs)
                {
                    DisplayLogs.RemoveAt(0);
                }
                updated = true;
            }
            return updated;
        }

        private static void HandleInput(ConsoleKeyInfo key)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                if (!string.IsNullOrEmpty(_currentInput))
                {
                    if (_currentInput.StartsWith("/"))
                    {
                        CommandHandler.HandleServerInput(_currentInput.Substring(1));
                    }
                    else
                    {
                        CommandHandler.Commands["say"].Func(_currentInput);
                    }
                    _currentInput = "";
                }
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (_currentInput.Length > 0)
                {
                    _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
                }
            }
            else if (key.Key == ConsoleKey.F1) _activeTab = TuiTab.Console;
            else if (key.Key == ConsoleKey.F2) _activeTab = TuiTab.Players;
            else if (key.Key == ConsoleKey.F3) _activeTab = TuiTab.Vessels;
            else if (key.Key == ConsoleKey.F5) _showDebug = !_showDebug;
            else if (key.Key == ConsoleKey.F6) _showNormal = !_showNormal;
            else if (key.Key == ConsoleKey.F7) _showWarning = !_showWarning;
            else if (key.Key == ConsoleKey.F8) _showError = !_showError;
            else if (key.Key == ConsoleKey.F9) _showChat = !_showChat;
            else if (key.Key == ConsoleKey.F10) _resolveGuids = !_resolveGuids;
            else if (key.Key == ConsoleKey.Tab)
            {
                _sidebarFocused = !_sidebarFocused;
            }
            else if (key.Key == ConsoleKey.PageUp)
            {
                _scrollOffset += 5;
            }
            else if (key.Key == ConsoleKey.PageDown)
            {
                _scrollOffset = Math.Max(0, _scrollOffset - 5);
            }
            else if (key.Key == ConsoleKey.Home)
            {
                _scrollOffset = 200; 
            }
            else if (key.Key == ConsoleKey.End)
            {
                _scrollOffset = 0;
            }
            else if (_sidebarFocused)
            {
                var players = ServerContext.Clients.Values.ToList();
                if (key.Key == ConsoleKey.UpArrow)
                {
                    _selectedPlayerIndex = Math.Max(0, _selectedPlayerIndex - 1);
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    _selectedPlayerIndex = Math.Min(players.Count - 1, _selectedPlayerIndex + 1);
                }
                else if (key.Key == ConsoleKey.K)
                {
                    if (players.Count > _selectedPlayerIndex)
                    {
                        CommandHandler.HandleServerInput($"kick {players[_selectedPlayerIndex].PlayerName}");
                    }
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                _currentInput += key.KeyChar;
            }
        }
    }
}
