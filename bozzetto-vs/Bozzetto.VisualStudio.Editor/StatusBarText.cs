namespace Bozzetto.VisualStudio.Editor;

/// <summary>
/// Pure text formatting for the Bozzetto status bar indicator.
/// No VS SDK dependencies — fully testable without a VS host.
/// </summary>
internal static class StatusBarText
{
  /// <summary>
  /// Formats the status bar display string for the Bozzetto connection indicator.
  /// </summary>
  public static string FormatStatusBarText(bool connected, int passingTests, int latencyMs)
  {
    if (!connected)
      return "⬤ Bozzetto Disconnected";

    return $"⬤ Bozzetto Connected  {passingTests} passing  {latencyMs}ms";
  }
}
