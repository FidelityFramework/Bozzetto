namespace SageFs.VisualStudio.ToolWindows;

using System.Threading;
using Microsoft.VisualStudio.Extensibility.UI;

internal class HotReloadControl : RemoteUserControl
{
  public HotReloadControl(object? dataContext, SynchronizationContext? synchronizationContext = null)
    : base(dataContext, synchronizationContext)
  {
  }
}
