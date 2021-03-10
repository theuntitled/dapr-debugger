namespace DaprDebugger.Models
{
	internal class DebuggableInstance
	{
		public string ProjectName { get; set; }

		public string AppId { get; set; }

		public int DaprProcessId { get; set; }

		public int DotNetProcessId { get; set; }

		public bool IsAttached { get; set; }

		public bool IsRunning { get; set; }

		public string OutputFileName { get; set; }
	}
}
