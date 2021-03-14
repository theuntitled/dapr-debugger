using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;

namespace DaprDebugger
{
	internal static class Extensions
	{
		internal static async Task<DTE2> GetDTE2Async(this DaprDebuggerPackage serviceProvider)
		{
			return (DTE2) await serviceProvider.GetServiceAsync(typeof(DTE));
		}

		internal static async Task UpdateUserInterfaceAsync(this DaprDebuggerPackage serviceProvider)
		{
			await serviceProvider.JoinableTaskFactory.SwitchToMainThreadAsync();

			var ui = (IVsUIShell) await serviceProvider.GetServiceAsync(typeof(IVsUIShell));

			ui?.UpdateCommandUI(0);
		}
	}
}
