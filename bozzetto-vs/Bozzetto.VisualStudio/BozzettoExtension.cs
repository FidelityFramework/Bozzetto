// Thin C# shim — source generators require C#.
// All real logic lives in Bozzetto.VisualStudio.Core (F#).
namespace Bozzetto.VisualStudio;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

[VisualStudioContribution]
internal class BozzettoExtension : Extension
{
  public override ExtensionConfiguration ExtensionConfiguration => new()
  {
    Metadata = new(
      id: "bozzetto-visualstudio",
      version: this.ExtensionAssemblyVersion,
      publisherName: "WillEhrendreich",
      displayName: "Bozzetto — F# Live Development",
      description: """
        Live F# development environment for Visual Studio. Features:
        • Real-time test gutter markers (pass/fail/skip indicators)
        • F# IntelliSense completions powered by the Bozzetto daemon
        • TypeExplorer tool window with auto-refresh
        • Live test status panel with run policy controls
        • Inline eval adornments and squiggles
        • Daemon health notifications on startup

        Requires: Bozzetto CLI (dotnet tool install --global Bozzetto), VS 2022 17.14+
        """)
    {
      MoreInfo = "https://github.com/WillEhrendreich/SageFs",
      Icon = @"Assets\icon.png",
      PreviewImage = @"Assets\preview.png",
      Tags = ["F#", "fsharp", "repl", "live-coding", "testing", "functional"],
    },
  };

  protected override void InitializeServices(IServiceCollection serviceCollection)
  {
    base.InitializeServices(serviceCollection);

    // Determine the daemon URL. Prefer the persisted port only when a daemon is
    // actually running there; otherwise fall back to the live default daemon or keep
    // the configured port for the next start.
    var existingUrl = TryReadDaemonUrl();
    int daemonPort = Core.DaemonManager.resolveDiscoveryMcpPort(existingUrl);
    var daemonUrl = $"http://localhost:{daemonPort}";
    var options = new Options.BozzettoOptions
    {
      DaemonUrl = daemonUrl
    };

    // Write daemon.json so the in-process MEF assembly (Bozzetto.VisualStudio.Editor)
    // can discover the URL without a direct project reference across TFM boundaries.
    // Uses %LOCALAPPDATA%\Bozzetto\daemon.json — survives session across VS restarts.
    WriteDaemonJson(daemonUrl);

    serviceCollection.AddSingleton(options);
    serviceCollection.AddSingleton<Core.BozzettoClient>(sp =>
    {
      var client = new Core.BozzettoClient();
      client.McpPort = daemonPort;
      client.DashboardPort = daemonPort + 1;
      return client;
    });
    serviceCollection.AddSingleton<Core.LiveTestingSubscriber>(sp =>
    {
      var sub = new Core.LiveTestingSubscriber(daemonPort);
      sub.Start();
      return sub;
    });
    serviceCollection.AddSingleton<Core.EvalCancellation>(sp =>
    {
      var cancel = new Core.EvalCancellation();
      var testSub = sp.GetRequiredService<Core.LiveTestingSubscriber>();
      Core.EvalCancellation.Wire(cancel, testSub);
      return cancel;
    });
    serviceCollection.AddSingleton<Core.SessionSubscriber>(sp =>
    {
      var sub = new Core.SessionSubscriber(daemonPort);
      sub.Start();
      return sub;
    });

    // Daemon startup health check: StatusBarManager (ExtensionPart) handles this via
    // constructor-injected BozzettoClient and fires a 2-second delayed ping in InitializeAsync,
    // writing the result ("✓ connected" / "⚠ not running") to the Bozzetto output channel.
  }

  /// <summary>
  /// Reads the daemon URL from a previously written daemon.json, or returns <c>null</c>.
  /// Allows a custom DaemonUrl (set via Tools → Options → Bozzetto and persisted by
  /// <see cref="Options.OptionsApplier"/>) to survive across VS restarts.
  /// </summary>
  internal static string? TryReadDaemonUrl()
  {
    return Core.DaemonManager.tryReadConfiguredDaemonUrl()?.Value;
  }

  /// <summary>Writes the daemon URL to %LOCALAPPDATA%\Bozzetto\daemon.json.</summary>
  internal static void WriteDaemonJson(string url)
  {
    var bozzettoDir = System.IO.Path.Combine(
      System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
      "Bozzetto");
    System.IO.Directory.CreateDirectory(bozzettoDir);
    System.IO.File.WriteAllText(
      System.IO.Path.Combine(bozzettoDir, "daemon.json"),
      $"{{\"Url\":\"{url}\"}}");
  }
}

