using System;
using System.ComponentModel.Design;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace DaprDebugger.Commands
{
	/// <summary>
	///     Command handler
	/// </summary>
	internal sealed class AttachToAllDaprInstancesInSolution
	{
		/// <summary>
		///     Command ID.
		/// </summary>
		public const int CommandId = 0x0101;

		/// <summary>
		///     Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("7c886e37-96d2-4c02-9ea5-c6b2f59bff2b");

		/// <summary>
		///     The menu item.
		/// </summary>
		// ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
		private readonly MenuCommand _menuItem;

		/// <summary>
		///     VS Package that provides this command, not null.
		/// </summary>
		private readonly AsyncPackage package;

		/// <summary>
		///     Initializes a new instance of the <see cref="AttachToAllDaprInstancesInSolution" /> class.
		///     Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		private AttachToAllDaprInstancesInSolution(AsyncPackage package, OleMenuCommandService commandService)
		{
			this.package = package ?? throw new ArgumentNullException(nameof(package));

			commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

			var menuCommandId = new CommandID(CommandSet, CommandId);

			_menuItem = new MenuCommand(Execute, menuCommandId);

			commandService.AddCommand(_menuItem);
		}

		/// <summary>
		///     Gets the instance of the command.
		/// </summary>
		public static AttachToAllDaprInstancesInSolution Instance { get; private set; }

		/// <summary>
		///     Gets the service provider from the owner package.
		/// </summary>
		private IAsyncServiceProvider ServiceProvider => package;

		/// <summary>
		///     Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync(AsyncPackage package)
		{
			// Switch to the main thread - the call to AddCommand in AttachToDapr's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

			var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

			Instance = new AttachToAllDaprInstancesInSolution(package, commandService);
		}

		/// <summary>
		///     This function is the callback used to execute the command when the menu item is clicked.
		///     See the constructor to see how the menu item is associated with this function using
		///     OleMenuCommandService service and MenuCommand class.
		/// </summary>
		/// <param name="sender">Event sender.</param>
		/// <param name="e">Event args.</param>
		private async void Execute(object sender, EventArgs e)
		{
			var dte2 = await ServiceProvider.GetDTE2Async();

			var processList = dte2.Debugger.LocalProcesses.Cast<Process>()
			                      .ToList();

			var projects = Utilities.GetCodeProjectsInSolution(dte2);

			var targetNames = projects.Select(GetOutputFileName)
			                          .Where(name => !string.IsNullOrEmpty(name))
			                          .ToList();

			foreach (var target in targetNames)
			{
				var projectProcess = processList.FirstOrDefault(item => item.Name.Contains(target.Replace(".dll", ".exe")));

				projectProcess?.Attach();
			}
		}

		private string GetOutputFileName(Project project)
		{
			return project?.Properties?.Cast<Property>()
			              .FirstOrDefault(property => property.Name == "OutputFileName")
			              ?.Value
			              ?.ToString();
		}
	}
}
