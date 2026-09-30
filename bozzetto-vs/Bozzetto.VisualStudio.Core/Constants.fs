namespace Bozzetto.VisualStudio.Core

/// Shared constants for the Bozzetto VS extension.
/// Single source of truth for port numbers and other fixed configuration.
module Constants =

  /// Default port for the Bozzetto MCP / eval server.
  /// Clients that read %LOCALAPPDATA%\Bozzetto\daemon.json should use that value;
  /// this constant is the hardcoded fallback when no daemon.json is present.
  let DefaultMcpPort = 47749

  /// Default port for the Bozzetto HTTP dashboard / REST API (McpPort + 1).
  let DefaultDashboardPort = DefaultMcpPort + 1

  /// Expected apiVersion from the /version endpoint.
  /// Used by CheckVersionAsync to detect incompatible daemon builds.
  let ExpectedApiVersion = 1
