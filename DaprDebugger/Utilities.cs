using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using EnvDTE;
using EnvDTE80;
using Process = System.Diagnostics.Process;

namespace DaprDebugger
{
	internal static class Utilities
	{
		internal static List<Project> GetCodeProjectsInSolution(DTE2 dte2)
		{
			var solutionProjects = dte2.Solution.Projects.Cast<Project>()
			                           .ToList();

			var projects = GetProjects(solutionProjects);

			return projects;
		}

		private static List<Project> GetProjects(List<Project> projects)
		{
			var result = new List<Project>();

			foreach (var project in projects)
			{
				if (project.Kind == ProjectKinds.vsProjectKindSolutionFolder && project.Name != "Miscellaneous Files")
				{
					if (project.ProjectItems != null && project.ProjectItems.Count > 0)
					{
						var children = new List<Project>();

						for (var i = 1; i <= project.ProjectItems.Count; i++)
						{
							var item = project.ProjectItems.Item(i);

							children.Add(item.SubProject);
						}

						result.AddRange(GetProjects(children));

						continue;
					}
				}

				result.Add(project);
			}

			return result;
		}

		internal static OutputWindowPane GetDebugOutputPane(DTE2 dte2)
		{
			return GetOutputPane(dte2, "Dapr");
		}

		internal static OutputWindowPane GetOutputPane(DTE2 dte2, string name)
		{
			var pane = dte2.ToolWindows.OutputWindow.OutputWindowPanes.Cast<OutputWindowPane>()
			               .FirstOrDefault(item => item.Name.Contains(name));

			if (pane == null)
			{
				pane = dte2.ToolWindows.OutputWindow.OutputWindowPanes.Add(name);
			}

			if (dte2.ToolWindows.OutputWindow.ActivePane.Name != name)
			{
				pane.Activate();
			}

			return pane;
		}

		internal static void KillProcessTree(int processId)
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

				KillProcessTree(Convert.ToInt32(managementObject["ProcessID"]));
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
