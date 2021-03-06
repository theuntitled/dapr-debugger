using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace DaprDebugger.Commands
{
	/// <summary>
	///     Command handler
	/// </summary>
	internal sealed class RestartDependencies
	{
		/// <summary>
		///     Command ID.
		/// </summary>
		public const int CommandId = 0x0104;

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
		private readonly DaprDebuggerPackage package;

		/// <summary>
		///     Initializes a new instance of the <see cref="RestartDependencies" /> class.
		///     Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		private RestartDependencies(DaprDebuggerPackage package, OleMenuCommandService commandService)
		{
			this.package = package ?? throw new ArgumentNullException(nameof(package));

			commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

			var menuCommandId = new CommandID(CommandSet, CommandId);

			_menuItem = new MenuCommand(Execute, menuCommandId)
			{
				Visible = false
			};

			package.DaprDependencyManager.RestartMenuItem = _menuItem;

			commandService.AddCommand(_menuItem);
		}

		/// <summary>
		///     Gets the instance of the command.
		/// </summary>
		public static RestartDependencies Instance { get; private set; }

		/// <summary>
		///     Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync(DaprDebuggerPackage package)
		{
			// Switch to the main thread - the call to AddCommand in RestartDependencies's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

			var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

			Instance = new RestartDependencies(package, commandService);
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
			await package.DaprDependencyManager.RestartAsync();
		}
	}
}
