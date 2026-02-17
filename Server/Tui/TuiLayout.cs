using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Tui
{
    public enum TuiTab { Console, Players, Vessels }

    public class PlayerTuiInfo
    {
        public string Name { get; set; }
        public int Ping { get; set; }
        public TimeSpan OnlineTime { get; set; }
        public int LockCount { get; set; }
        public string KspVersion { get; set; }
    }

    public class VesselTuiInfo
    {
        public string Name { get; set; }
        public string VesselId { get; set; }
        public string Controller { get; set; }
        public int PartCount { get; set; }
        public string Location { get; set; }
        public bool IsControlled { get; set; }
    }

    public static class TuiLayout
    {
        public static Layout Generate(string serverName, string version, string uptime, string status, 
            IEnumerable<string> logs, IEnumerable<string> players, int subspaceCount, int vesselCount, long memoryUsage, string currentInput,
            IEnumerable<SubspaceInfo> subspaces, int selectedPlayerIndex,
            TuiTab activeTab, IEnumerable<PlayerTuiInfo> playerDetails, IEnumerable<VesselTuiInfo> vesselDetails, int logHeight, int logWidth,
            DateTime lastBackupTime, bool resolveGuids)
        {
            var mainLayout = activeTab == TuiTab.Console 
                ? new Layout("Main").SplitColumns(
                    new Layout("LogArea"),
                    new Layout("Sidebar").Size(30)
                )
                : new Layout("Main").SplitColumns(
                    new Layout("ContentArea")
                );

            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(3),
                    new Layout("TabBar").Size(3),
                    mainLayout,
                    new Layout("Footer").Size(3)
                );

            // Header
            var backupColor = "yellow"; // Default manilla/uptime color
            var backupStr = "Never";
            if (lastBackupTime != DateTime.MinValue)
            {
                var diff = DateTime.UtcNow - lastBackupTime;
                backupStr = lastBackupTime.ToLocalTime().ToString("HH:mm:ss");
                if (diff.TotalSeconds < 30) backupColor = "green";
                else if (diff.TotalMinutes < 5) backupColor = "yellow";
                else backupColor = "yellow"; 
            }

            var mainMarkup = new Markup($"[bold cyan]LunaMultiplayer Server[/] - [white]{Markup.Escape(serverName)}[/] | [green]{status}[/] | [yellow]{uptime}[/] | [blue]v{version}[/]");
            var backupMarkup = new Markup($"[bold white]Last Backup:[/] [{backupColor}]{backupStr}[/]");

            // 3-column grid for perfect centering: [Left Spacer] [Center Title] [Right Backup]
            // We use 25 chars for side columns to balance the layout.
            var headerGrid = new Grid();
            headerGrid.AddColumn(new GridColumn().Width(25)); // Left filler
            headerGrid.AddColumn(new GridColumn()); // Main info (auto-size)
            headerGrid.AddColumn(new GridColumn().Width(25)); // Right backup info

            headerGrid.AddRow(
                new Text(""),
                Align.Center(mainMarkup),
                Align.Right(backupMarkup)
            );

            layout["Header"].Update(
                new Panel(headerGrid).BorderColor(Color.Cyan1)
            );

            // Tab Bar
            var tabs = new List<Markup>();
            foreach (var tab in Enum.GetValues<TuiTab>())
            {
                var hotkey = tab switch
                {
                    TuiTab.Console => "F1",
                    TuiTab.Players => "F2",
                    TuiTab.Vessels => "F3",
                    _ => ""
                };

                var style = tab == activeTab ? "yellow bold" : "grey";
                var title = tab.ToString().ToUpper();

                // Show hotkey in square brackets with light red color
                tabs.Add(new Markup($"[{style}]  {title} [/][red][[{hotkey}]]  [/]"));
            }
            
            layout["TabBar"].Update(
                new Panel(
                    Align.Center(
                        new Columns(tabs)
                    )
                ).BorderColor(Color.Grey)
            );

            if (activeTab == TuiTab.Console)
            {
                RenderConsoleView(layout, logs, players, subspaceCount, vesselCount, memoryUsage, subspaces, selectedPlayerIndex, logHeight, logWidth);
            }
            else if (activeTab == TuiTab.Players)
            {
                RenderPlayersView(layout, playerDetails);
            }
            else if (activeTab == TuiTab.Vessels)
            {
                RenderVesselsView(layout, vesselDetails, resolveGuids);
            }

            // Footer
            layout["Footer"].Update(
                new Panel(
                    new Markup($"[bold cyan]>[/] {Markup.Escape(currentInput)}")
                ).BorderColor(Color.Cyan1)
            );

            return layout;
        }


        private static IRenderable CreatePaddedLogEntry(string logEntry, int currentLogWidth)
        {
            if (string.IsNullOrEmpty(logEntry)) return new Text("");
            try
            {
                // We don't need the elaborate measurement here anymore as we rely on Panel constraints,
                // but we keep the Markup wrapper to ensure colors are processed.
                return new Markup(logEntry);
            }
            catch
            {
                // Strip tags for safe fallback
                var safeText = logEntry.Replace("[[", "[").Replace("]]", "]");
                return new Text(safeText);
            }
        }

        private static void RenderConsoleView(Layout layout, IEnumerable<string> logs, IEnumerable<string> players, int subspaceCount, int vesselCount, long memoryUsage, IEnumerable<SubspaceInfo> subspaces, int selectedPlayerIndex, int logHeight, int logWidth)
        {
            // Logs
            // We DO NOT truncate logs manually here because it breaks markup tags (e.g. cutting [white] in half)
            // We let Spectre.Console handle the wrapping/overflow naturally
            var logRows = logs.Select(l => 
            {
                // Logs are already escaped and highlighted by TuiEngine
                // We wrap in [white] to ensure the rest of the line is readable
                return $"[white]{l}[/]"; 
            });

            // Padding logs to ensure constant layout height
            var finalLogRows = logRows.ToList();
            while (finalLogRows.Count < logHeight) 
            {
                finalLogRows.Add("");
            }

            var logRenderables = new List<IRenderable>();
            foreach (var r in finalLogRows)
            {
                logRenderables.Add(CreatePaddedLogEntry(r, logWidth));
            }

            layout["LogArea"].Update(
                new Panel(
                    new Rows(logRenderables.ToArray())
                ).Header("[bold white]Server Console[/]").BorderColor(Color.Grey).Expand()
            );

            // Sidebar
            var playerStrings = players.ToList();
            var playerListMarkup = new List<string>();
            // Limit sidebar player list to 5 to save space
            for (var i = 0; i < Math.Min(5, playerStrings.Count); i++)
            {
                var prefix = (i == selectedPlayerIndex) ? "[reverse][yellow]>[/]" : "[green]•[/]";
                var suffix = (i == selectedPlayerIndex) ? " [/]" : "";
                // Use light green for player names
                playerListMarkup.Add($"{prefix} [green1]{playerStrings[i]}[/]{suffix}");
            }
            if (playerStrings.Count > 5) playerListMarkup.Add($"[grey]... and {playerStrings.Count - 5} more[/]");
            var playerListContent = playerListMarkup.Any() ? string.Join("\n", playerListMarkup) : "[grey]No players connected[/]";
            
            layout["Sidebar"].Update(
                new Rows(
                    new Panel(new Markup(playerListContent)).Header("[bold white]Players[/]").Expand(),
                    TuiWidgets.SubspaceVisualization(subspaces.Take(5)), // Limit subspaces in sidebar
                    new Panel(
                        new Rows(
                            new Markup($"[bold white]Subs:[/] [yellow]{subspaceCount}[/]"),
                            new Markup($"[bold white]Vess:[/] [yellow]{vesselCount}[/]"),
                            new Markup($"[bold white]Mem :[/] [yellow]{memoryUsage / 1024 / 1024}MB[/]")
                        )
                    ).Header("[bold white]Stats[/]").Expand()
                )
            );
        }

        private static void RenderPlayersView(Layout layout, IEnumerable<PlayerTuiInfo> players)
        {
            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn("[bold]Name[/]");
            table.AddColumn("[bold]Ping[/]");
            table.AddColumn("[bold]Online Time[/]");
            table.AddColumn("[bold]Locks[/]");
            table.AddColumn("[bold]KSP Version[/]");

            foreach (var p in players)
            {
                var onlineStr = p.OnlineTime.ToString(@"hh\:mm\:ss");
                table.AddRow(
                    new Markup($"[green1]{Markup.Escape(p.Name)}[/]"),
                    new Markup($"[yellow]{p.Ping}ms[/]"),
                    new Markup(onlineStr),
                    new Markup($"[blue]{p.LockCount}[/]"),
                    new Markup($"[grey74]{Markup.Escape(p.KspVersion ?? "N/A")}[/]")
                );
            }

            layout["ContentArea"].Update(
                new Panel(table).Header("[bold white]Connected Players[/]").BorderColor(Color.Grey).Expand()
            );
        }

        private static void RenderVesselsView(Layout layout, IEnumerable<VesselTuiInfo> vessels, bool resolveGuids)
        {
            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn("[bold]Vessel Name[/]");
            table.AddColumn("[bold]Controller[/]");
            table.AddColumn("[bold]Parts[/]");
            table.AddColumn("[bold]Location[/]");

            foreach (var v in vessels)
            {
                // Vessel Name is always yellow
                var displayName = resolveGuids ? v.Name : $"{v.Name} ({v.VesselId?.Substring(0, 4) ?? "????"})";
                
                table.AddRow(
                    new Markup($"[yellow]{Markup.Escape(displayName)}[/]"),
                    new Markup(v.IsControlled ? $"[green1]{Markup.Escape(v.Controller)}[/]" : "[grey]Uncontrolled[/]"),
                    new Markup($"[blue]{v.PartCount}[/]"),
                    new Markup($"[grey]{Markup.Escape(v.Location)}[/]")
                );
            }

            layout["ContentArea"].Update(
                new Panel(table).Header("[bold white]Active Vessels[/]").BorderColor(Color.Grey).Expand()
            );
        }
    }
}
