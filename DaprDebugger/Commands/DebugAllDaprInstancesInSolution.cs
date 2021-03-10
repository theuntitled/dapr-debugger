using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Management;
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
		private readonly AsyncPackage package;

		/// <summary>
		///     Initializes a new instance of the <see cref="DebugAllDaprInstancesInSolution" /> class.
		///     Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		/// <param name="dte2">A <see cref="DTE2" /> instance, not null.</param>
		private DebugAllDaprInstancesInSolution(AsyncPackage package, OleMenuCommandService commandService, DTE2 dte2)
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

				Utilities.GetDebugOutputPane(dte2)
				         .OutputString($"Mode changed to {dte2.Mode}\n");

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

		/// <summary>
		///     Gets the service provider from the owner package.
		/// </summary>
		private IAsyncServiceProvider ServiceProvider => package;

		private List<DebuggableInstance> Instances { get; } = new List<DebuggableInstance>();

		private bool KillingProcesses { get; set; }

		/// <summary>
		///     Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync(AsyncPackage package)
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
			var dte2 = await ServiceProvider.GetDTE2Async();

			var projects = Utilities.GetCodeProjectsInSolution(dte2);

			foreach (var project in projects)
			{
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
					continue;
				}

				var projectFile = new XmlDocument();

				projectFile.Load(projectFileName);

				var daprAppIdNode = projectFile.SelectSingleNode("//Project/PropertyGroup/DaprAppId");
				var daprAppPortNode = projectFile.SelectSingleNode("//Project/PropertyGroup/DaprAppPort");
				var daprAppProtocolNode = projectFile.SelectSingleNode("//Project/PropertyGroup/DaprAppProtocol");

				var appId = daprAppIdNode?.InnerText;
				var appPort = daprAppPortNode?.InnerText;
				var appProtocol = daprAppProtocolNode?.InnerText;

				if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appPort))
				{
					continue;
				}

				Utilities.GetDebugOutputPane(dte2)
				         .OutputString($"Registering dapr instance for project {project.Name}\n");

				var arguments = new List<string>
				{
					$"--app-id {appId}",
					$"--app-port {appPort}"
				};

				if (!string.IsNullOrEmpty(appProtocol))
				{
					arguments.Add($"--app-protocol {appProtocol}");
				}

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


				process.Exited += (_sender, _args) =>
				{
					Utilities.GetDebugOutputPane(dte2)
					         .OutputString($"Dapr process ({project.Name}) exited with code {process.ExitCode}\n");

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

			if (Instances.Any(item => !item.IsRunning))
			{
				KillProcesses();

				// TODO: Alert?

				return;
			}

			var notAttached = Instances.Where(item => !item.IsAttached)
			                           .ToList();

			Utilities.GetDebugOutputPane(dte2)
			         .OutputString("Waiting for dotnet start.\n");

			while (notAttached.Any())
			{
				foreach (var instance in notAttached)
				{
					var process = dte2.Debugger.LocalProcesses.Cast<EnvDTE.Process>()
					                  .FirstOrDefault(item => item.Name.Contains(instance.OutputFileName));

					if (process != null)
					{
						process.Attach();

						instance.IsAttached = true;
						instance.DotNetProcessId = process.ProcessID;
					}
				}

				notAttached = Instances.Where(item => !item.IsAttached)
				                       .ToList();
			}

			Utilities.GetDebugOutputPane(dte2)
			         .OutputString("Debugging started.\n");
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
					KillProcessAndChildren(instance.DaprProcessId);
				}
			}

			Instances.Clear();

			KillingProcesses = false;
		}

		private static void KillProcessAndChildren(int processId)
		{
			if (processId <= 0)
			{
				return;
			}

			var searcher = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={processId}");

			var results = searcher.Get();

			foreach (var baseObject in results)
			{
				var managementObject = (ManagementObject) baseObject;

				KillProcessAndChildren(Convert.ToInt32(managementObject["ProcessID"]));
			}

			try
			{
				var process = Process.GetProcessById(processId);

				process.Kill();
			}
			catch (ArgumentException)
			{
				// Process already exited.
			}
		}
	}
}
