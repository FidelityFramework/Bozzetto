namespace Bozzetto.VisualStudio.Commands;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Documents;

#pragma warning disable VSEXTPREVIEW_OUTPUTWINDOW

[VisualStudioContribution]
internal class ToggleErrorListCommand : Command
{
  private readonly Core.BozzettoClient client;
  private OutputChannel? output;
  private Services.ErrorListBridge? bridge;

  public ToggleErrorListCommand(Core.BozzettoClient client) => this.client = client;

  public override CommandConfiguration CommandConfiguration => new("%Bozzetto.ToggleErrorList.DisplayName%")
  {
    Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
    Icon = new(ImageMoniker.KnownValues.StatusError, IconSettings.IconAndText),
  };

  public override async Task InitializeAsync(CancellationToken ct)
  {
    output = await Extensibility.Views().Output.CreateOutputChannelAsync("Bozzetto", ct);
    await base.InitializeAsync(ct);
  }

  public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct)
  {
    if (bridge is not null)
    {
      bridge.Stop();
      bridge.Dispose();
      bridge = null;
      if (output is not null)
        await output.WriteLineAsync("○ Error List bridge stopped");
      return;
    }

    var sessions = (await client.GetSessionsAsync(ct)).ToList();
    if (sessions.Count == 0)
    {
      if (output is not null)
        await output.WriteLineAsync("⚠ No active session — start Bozzetto daemon first");
      return;
    }

    bridge = new Services.ErrorListBridge(Extensibility);
    bridge.Start(client.McpPort);

    if (output is not null)
      await output.WriteLineAsync("● Error List bridge started — diagnostics forwarded to Error List");
  }
}

#pragma warning restore VSEXTPREVIEW_OUTPUTWINDOW
