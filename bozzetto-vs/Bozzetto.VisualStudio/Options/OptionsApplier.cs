namespace Bozzetto.VisualStudio.Options;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;

/// <summary>
/// Reads the user-configured daemon URL from Tools → Options → Bozzetto and updates
/// <see cref="Core.BozzettoClient"/> and daemon.json so the net472 MEF assembly
/// picks up the correct port on the next SSE reconnect.
///
/// On VS startup the URL is already loaded by <see cref="BozzettoExtension"/> from
/// daemon.json. This part persists any option change so it survives the next restart.
/// </summary>
[VisualStudioContribution]
internal class OptionsApplier : ExtensionPart
{
    private readonly BozzettoOptions _options;
    private readonly Core.BozzettoClient _client;

    public OptionsApplier(BozzettoOptions options, Core.BozzettoClient client)
    {
        _options = options;
        _client = client;
    }

    protected override async Task InitializeAsync(CancellationToken ct)
    {
        await base.InitializeAsync(ct);

        try
        {
            // If DaemonUrl was updated since InitializeServices ran (e.g., user changed
            // Tools → Options → Bozzetto and reloaded without restarting VS), apply it now.
            var port = BozzettoOptions.ParsePort(_options.DaemonUrl) ?? Core.Constants.DefaultMcpPort;
            if (port != _client.McpPort)
            {
                _client.McpPort = port;
                _client.DashboardPort = port + 1;
                BozzettoExtension.WriteDaemonJson(_options.DaemonUrl);
                System.Diagnostics.Debug.WriteLine(
                    $"[Bozzetto] OptionsApplier: updated daemon port to {port} from {_options.DaemonUrl}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Bozzetto] OptionsApplier: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
