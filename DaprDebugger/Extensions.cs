using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace DaprDebugger
{
	internal static class Extensions
	{
		internal static async Task<DTE2> GetDTE2Async(this IAsyncServiceProvider serviceProvider)
		{
			return (DTE2) await serviceProvider.GetServiceAsync(typeof(DTE));
		}
	}
}
