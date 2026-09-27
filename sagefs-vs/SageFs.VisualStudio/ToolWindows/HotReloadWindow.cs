namespace SageFs.VisualStudio.ToolWindows;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

[VisualStudioContribution]
internal class HotReloadWindow : ToolWindow
{
  private readonly Core.SageFsClient client;
  private HotReloadData? dataContext;

  public HotReloadWindow(Core.SageFsClient client)
  {
    this.client = client;
    this.Title = "SageFs Hot Reload";
  }

  public override ToolWindowConfiguration ToolWindowConfiguration => new()
  {
    Placement = ToolWindowPlacement.DocumentWell,
  };

  public override Task InitializeAsync(CancellationToken ct)
  {
    dataContext = new HotReloadData(Extensibility, client);
    return Task.CompletedTask;
  }

  public override Task<IRemoteUserControl> GetContentAsync(CancellationToken ct)
  {
    return Task.FromResult<IRemoteUserControl>(new HotReloadControl(dataContext));
  }
}

[VisualStudioContribution]
internal class ShowHotReloadCommand : Command
{
  public override CommandConfiguration CommandConfiguration => new("%SageFs.ShowHotReload.DisplayName%")
  {
    Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
    Icon = new(ImageMoniker.KnownValues.Refresh, IconSettings.IconAndText),
  };

  public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken ct)
  {
    await Extensibility.Shell().ShowToolWindowAsync<HotReloadWindow>(activate: true, ct);
  }
}
