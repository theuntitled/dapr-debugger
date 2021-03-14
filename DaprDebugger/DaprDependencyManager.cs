using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using EnvDTE;
using Process = System.Diagnostics.Process;

namespace DaprDebugger
{
	public class DaprDependencyManager
	{
		private readonly DaprDebuggerPackage _package;

		private readonly Dictionary<string, int> _processes;

		public DaprDependencyManager(DaprDebuggerPackage package)
		{
			_package = package;

			_processes = new Dictionary<string, int>();
		}

		private bool KillingProcesses { get; set; }

		public MenuCommand StopMenuItem { get; set; }
		public MenuCommand StartMenuItem { get; set; }
		public MenuCommand RestartMenuItem { get; set; }

		public async Task StartAsync()
		{
			await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

			StartMenuItem.Enabled = false;

			var dte2 = await _package.GetDTE2Async();

			var projects = Utilities.GetCodeProjectsInSolution(dte2);

			var outputPane = Utilities.GetDebugOutputPane(dte2);

			outputPane.Clear();
			outputPane.OutputString("Checking projects in solution for dapr dependencies.\n");

			foreach (var project in projects)
			{
				var properties = project.Properties.Cast<Property>()
				                        .ToList();

				var projectFileName = properties
				                      .FirstOrDefault(item => item.Name == "FullProjectFileName")
				                      ?.Value
				                      ?.ToString();

				var projectFullPath = properties
				                      .FirstOrDefault(item => item.Name == "FullPath")
				                      ?.Value
				                      ?.ToString();

				if (string.IsNullOrEmpty(projectFileName) || string.IsNullOrEmpty(projectFullPath))
				{
					outputPane.OutputString($"Project not compatible: {project.Name}\n");

					continue;
				}

				var projectFile = new XmlDocument();

				projectFile.Load(projectFileName);

				var dependencyNodeList = projectFile.SelectNodes("//Project/PropertyGroup/DaprDockerDependency");

				if (dependencyNodeList == null || dependencyNodeList.Count == 0)
				{
					continue;
				}

				for (var i = 0; i < dependencyNodeList.Count; i++)
				{
					var node = dependencyNodeList[i];

					var parts = node.InnerText.Split(':');

					if (parts.Length != 2)
					{
						outputPane.OutputString("Missing name attribute on dependency\n");

						continue;
					}

					var key = parts.First();
					var script = parts.Last();

					outputPane.OutputString($"Starting dependency: {key}\n");

					var process = new Process
					{
						EnableRaisingEvents = true,
						StartInfo = new ProcessStartInfo
						{
							FileName = "powershell.exe",
							ErrorDialog = false,
							CreateNoWindow = false,
							UseShellExecute = true,
							WorkingDirectory = projectFullPath,
							Arguments = Path.Combine(projectFullPath, script)
						}
					};

					process.Exited += (_, _2) =>
					{
						outputPane.OutputString($"Dependency process ({key}) exited with code {process.ExitCode}\n");

						KillProcesses();
					};

					process.Start();

					_processes.Add(key, process.Id);
				}
			}

			if (_processes.Any())
			{
				StartMenuItem.Visible = false;

				StopMenuItem.Visible = true;
				RestartMenuItem.Visible = true;
			}
			
			StartMenuItem.Enabled = true;

			await _package.UpdateUserInterfaceAsync();
		}

		public async Task StopAsync()
		{
			KillProcesses();

			StartMenuItem.Visible = true;

			StopMenuItem.Visible = false;
			RestartMenuItem.Visible = false;

			await _package.UpdateUserInterfaceAsync();
		}

		public async Task RestartAsync()
		{
			KillProcesses();
			await StartAsync();
		}

		private void KillProcesses()
		{
			if (KillingProcesses)
			{
				return;
			}

			KillingProcesses = true;

			foreach (var process in _processes)
			{
				Utilities.KillProcessTree(process.Value);
			}

			_processes.Clear();

			KillingProcesses = false;
		}
	}
}
