using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Bozzetto.VisualStudio.Editor.StatusBar;

[Export(typeof(IStatusBarService))]
[PartCreationPolicy(System.ComponentModel.Composition.CreationPolicy.Shared)]
internal sealed class StatusBarService : IStatusBarService
{
  public StatusBarService()
  {
    StatusBarBridge.Current = this;
  }

  public void SetText(string text)
  {
    try
    {
      ThreadHelper.ThrowIfNotOnUIThread();
      var statusBar = ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
      statusBar?.SetText(text);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine($"[Bozzetto] StatusBar.SetText failed: {ex.Message}");
    }
  }

  public void Clear()
  {
    try
    {
      ThreadHelper.ThrowIfNotOnUIThread();
      var statusBar = ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
      statusBar?.SetText("");
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine($"[Bozzetto] StatusBar.Clear failed: {ex.Message}");
    }
  }
}
