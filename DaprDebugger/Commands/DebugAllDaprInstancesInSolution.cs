using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using DaprDebugger.Models;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Process = System.Diagnostics.Process;
using Task = System.Threading.Tasks.Task;

namespace DaprDebugger.Commands
{
	/// <summary>
	///     Command handler
	/// </summary>
	internal sealed class DebugAllDaprInstancesInSolution
	{
		/// <summary>
		///     Command ID.
		/// </summary>
		public const int CommandId = 0x0100;

		/// <summary>
		///     Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("7c886e37-96d2-4c02-9ea5-c6b2f59bff2b");

		/// <summary>
		///     Reference to the <see cref="DTEEvents" />.
		/// </summary>
		// ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
		private readonly DTEEvents _dteEvents;

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
		///     Initializes a new instance of the <see cref="DebugAllDaprInstancesInSolution" /> class.
		///     Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		/// <param name="dte2">A <see cref="DTE2" /> instance, not null.</param>
		private DebugAllDaprInstancesInSolution(DaprDebuggerPackage package, OleMenuCommandService commandService, DTE2 dte2)
		{
			this.package = package ?? throw new ArgumentNullException(nameof(package));

			commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

			var menuCommandId = new CommandID(CommandSet, CommandId);

			_menuItem = new MenuCommand(Execute, menuCommandId);

			commandService.AddCommand(_menuItem);

			_dteEvents = dte2.Events.DTEEvents;

			_dteEvents.ModeChanged += lastMode =>
			{
				_menuItem.Enabled = dte2.Mode == vsIDEMode.vsIDEModeDesign;

				if (dte2.Mode == vsIDEMode.vsIDEModeDesign && lastMode == vsIDEMode.vsIDEModeDebug)
				{
					KillProcesses();
				}
			};
		}

		/// <summary>
		///     Gets the instance of the command.
		/// </summary>
		public static DebugAllDaprInstancesInSolution Instance { get; private set; }

		private List<DebuggableInstance> Instances { get; } = new List<DebuggableInstance>();

		private bool KillingProcesses { get; set; }

		/// <summary>
		///     Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync(DaprDebuggerPackage package)
		{
			// Switch to the main thread - the call to AddCommand in AttachToAllDaprInstancesInSolutions's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

			var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

			var dte2 = await package.GetDTE2Async();

			Instance = new DebugAllDaprInstancesInSolution(package, commandService, dte2);
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
			await package.JoinableTaskFactory.SwitchToMainThreadAsync();

			var dte2 = await package.GetDTE2Async();

			var projects = Utilities.GetCodeProjectsInSolution(dte2);

			var outputPane = Utilities.GetDebugOutputPane(dte2);

			outputPane.Clear();
			outputPane.OutputString("Checking projects in solution for dapr configuration.\n");

			foreach (var project in projects)
			{
				outputPane.OutputString($"Checking for dapr in project {project.Name}\n");

				var properties = project.Properties.Cast<Property>()
				                        .ToList();

				var projectOutputFileName = properties
				                            .FirstOrDefault(item => item.Name == "OutputFileName")
				                            ?.Value
				                            ?.ToString()
				                            .Replace(".dll", ".exe");

				var projectFileName = properties
				                      .FirstOrDefault(item => item.Name == "FullProjectFileName")
				                      ?.Value
				                      ?.ToString();

				var projectFullPath = properties
				                      .FirstOrDefault(item => item.Name == "FullPath")
				                      ?.Value
				                      ?.ToString();

				if (string.IsNullOrEmpty(projectFileName) || string.IsNullOrEmpty(projectOutputFileName) || string.IsNullOrEmpty(projectFullPath))
				{
					outputPane.OutputString($"Project not compatible: {project.Name}\n");

					continue;
				}

				var projectFile = new XmlDocument();

				projectFile.Load(projectFileName);

				var appId = projectFile.SelectSingleNode("//Project/PropertyGroup/DaprAppId")?.InnerText;
				var appPort = projectFile.SelectSingleNode("//Project/PropertyGroup/DaprAppPort")?.InnerText;

				if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appPort))
				{
					continue;
				}

				outputPane.OutputString($"Registering dapr instance for project {project.Name}\n");

				var arguments = new List<string>
				{
					$"--app-id {appId}",
					$"--app-port {appPort}"
				};

				AddOptionalArgument(projectFile, arguments, "--app-ssl", "DaprAppSSL");
				AddOptionalArgument(projectFile, arguments, "--app-protocol", "DaprAppProtocol");
				AddOptionalArgument(projectFile, arguments, "--app-max-concurrency", "DaprAppMaxConcurrency");

				AddOptionalArgument(projectFile, arguments, "--dapr-grpc-port", "DaprGrpcPort");
				AddOptionalArgument(projectFile, arguments, "--dapr-http-port", "DaprHttpPort");

				AddOptionalArgument(projectFile, arguments, "--image", "DaprImage");
				AddOptionalArgument(projectFile, arguments, "--config", "DaprConfig");
				AddOptionalArgument(projectFile, arguments, "--log-level", "DaprLogLevel");
				AddOptionalArgument(projectFile, arguments, "--profile-port", "DaprProfilePort");
				AddOptionalArgument(projectFile, arguments, "--components-path", "DaprComponentsPath");
				AddOptionalArgument(projectFile, arguments, "--enable-profiling", "DaprEnableProfiling");
				AddOptionalArgument(projectFile, arguments, "--placement-host-address", "DaprPlacementHostAddress");

				var process = new Process
				{
					EnableRaisingEvents = true,
					StartInfo = new ProcessStartInfo
					{
						FileName = "dapr",
						ErrorDialog = false,
						CreateNoWindow = false,
						UseShellExecute = true,
						WorkingDirectory = projectFullPath,
						Arguments = $"run {string.Join(" ", arguments)} -- dotnet run"
					}
				};

				process.Exited += (_, _2) =>
				{
					outputPane.OutputString($"Dapr process ({project.Name}) exited with code {process.ExitCode}\n");

					KillProcesses();
				};

				process.Start();

				Instances.Add(new DebuggableInstance
				{
					AppId = appId,
					IsAttached = false,
					DotNetProcessId = -1,
					ProjectName = project.Name,
					DaprProcessId = process.Id,
					IsRunning = !process.HasExited,
					OutputFileName = projectOutputFileName
				});
			}

			outputPane.OutputString("Project scan completed.\n");

			if (Instances.Any(item => !item.IsRunning))
			{
				outputPane.OutputString("Not all processes could be started, stopping debugging session.\n");

				KillProcesses();

				// TODO: Alert?

				return;
			}

			var notAttached = Instances.Where(item => !item.IsAttached)
			                           .ToList();

			outputPane.OutputString($"Waiting for {notAttached.Count} dotnet processes start.\n");

			while (notAttached.Any())
			{
				foreach (var instance in notAttached)
				{
					var process = dte2.Debugger.LocalProcesses.Cast<EnvDTE.Process>()
					                  .FirstOrDefault(item => item.Name.Contains(instance.OutputFileName));

					outputPane.OutputString($"Searching for {instance.AppId} dotnet process.\n");

					if (process != null)
					{
						process.Attach();

						instance.IsAttached = true;
						instance.DotNetProcessId = process.ProcessID;

						outputPane.OutputString($"Attached to dotnet process for {instance.AppId}.\n");
					}
					else
					{
						outputPane.OutputString($"No dotnet process found for {instance.AppId}.\n");
					}
				}

				notAttached = Instances.Where(item => !item.IsAttached)
				                       .ToList();

				if (notAttached.Any())
				{
					await Task.Delay(250);
				}
			}

			outputPane.OutputString("Debugging started.\n");
		}

		private void AddOptionalArgument(XmlDocument projectFile, List<string> arguments, string argument, string xmlNode)
		{
			var value = projectFile.SelectSingleNode($"//Project/PropertyGroup/{xmlNode}")?.InnerText;

			if (!string.IsNullOrEmpty(value))
			{
				arguments.Add($"{argument} {value}");
			}
		}

		private void KillProcesses()
		{
			if (KillingProcesses)
			{
				return;
			}

			KillingProcesses = true;

			foreach (var instance in Instances)
			{
				if (instance.IsRunning)
				{
					Utilities.KillProcessTree(instance.DaprProcessId);
				}
			}

			Instances.Clear();

			KillingProcesses = false;
		}
	}
}
