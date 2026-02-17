using LmpCommon;
using Server.Context;
using Server.Settings.Structures;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Generic;
using System.Linq;

namespace Server.Tui
{
    public class SubspaceInfo
    {
        public int Id { get; set; }
        public List<string> Players { get; set; } = new List<string>();
        public double Time { get; set; }
    }

    public static class TuiWidgets
    {
        public static IRenderable SubspaceVisualization(IEnumerable<SubspaceInfo> subspaces)
        {
            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn("[bold white]Id[/]");
            table.AddColumn("[bold white]Time[/]");
            table.AddColumn("[bold white]Players[/]");

            foreach (var subspace in subspaces.OrderBy(s => s.Id))
            {
                table.AddRow(
                    subspace.Id.ToString(),
                    subspace.Time.ToString("F2"),
                    string.Join(", ", subspace.Players)
                );
            }

            return new Panel(table).Header("[bold white]Subspaces[/]").BorderColor(Color.Blue);
        }
    }
}
