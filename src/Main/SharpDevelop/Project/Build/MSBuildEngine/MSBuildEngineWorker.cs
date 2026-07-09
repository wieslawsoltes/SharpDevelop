// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.BuildWorker;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace ICSharpCode.SharpDevelop.Project
{
	class MSBuildEngineWorker : IMSBuildLoggerContext
	{
		readonly IMSBuildEngine parentBuildEngine;
		readonly FileName projectFileName;
		readonly IProject project;
		readonly SolutionFormatVersion projectMinimumSolutionVersion;
		ProjectBuildOptions options;
		IBuildFeedbackSink feedbackSink;
		List<string> additionalTargetFiles;
		
		internal MSBuildEngineWorker(IMSBuildEngine parentBuildEngine, IProject project, ProjectBuildOptions options, IBuildFeedbackSink feedbackSink, List<string> additionalTargetFiles)
		{
			this.parentBuildEngine = parentBuildEngine;
			this.project = project;
			this.projectFileName = project.FileName;
			this.projectMinimumSolutionVersion = project.MinimumSolutionVersion;
			this.options = options;
			this.feedbackSink = feedbackSink;
			this.additionalTargetFiles = additionalTargetFiles;
		}
		
		const EventTypes ControllableEvents = EventTypes.Message | EventTypes.TargetStarted | EventTypes.TargetFinished
			| EventTypes.TaskStarted | EventTypes.TaskFinished | EventTypes.Unknown;
		
		public IProject Project {
			get { return project; }
		}
		public FileName ProjectFileName {
			get { return projectFileName; }
		}
		
		public bool ReportMessageEvents { get; set; }
		public bool ReportTargetStartedEvents { get; set; }
		public bool ReportTargetFinishedEvents { get; set; }
		public bool ReportAllTaskStartedEvents { get; set; }
		public bool ReportAllTaskFinishedEvents { get; set; }
		public bool ReportUnknownEvents { get; set; }
		
		HashSet<string> interestingTasks = new HashSet<string>();
		string temporaryFileName;
		
		public ISet<string> InterestingTasks {
			get { return interestingTasks; }
		}
		
		readonly EventSource eventSource = new EventSource();
		List<ILogger> loggers = new List<ILogger>();
		IMSBuildChainedLoggerFilter loggerChain;
		
		internal Task<bool> RunBuildAsync(CancellationToken cancellationToken)
		{
			Dictionary<string, string> globalProperties = new Dictionary<string, string>();
			globalProperties.AddRange(SD.MSBuildEngine.GlobalBuildProperties);
			
			foreach (KeyValuePair<string, string> pair in options.Properties) {
				LoggingService.Debug("Setting property " + pair.Key + " to '" + pair.Value + "'");
				globalProperties[pair.Key] = pair.Value;
			}
			globalProperties["Configuration"] = options.Configuration;
			if (options.Platform == "Any CPU")
				globalProperties["Platform"] = "AnyCPU";
			else
				globalProperties["Platform"] = options.Platform;

#if LIBREWPF
			ConfigureLibreWpfTargetFrameworkReferenceAssemblies(globalProperties);
#endif
			
			InterestingTasks.AddRange(parentBuildEngine.CompileTaskNames);
			
			loggers.Add(new SharpDevelopLogger(this));
			if (options.BuildOutputVerbosity == BuildOutputVerbosity.Diagnostic) {
				this.ReportMessageEvents = true;
				this.ReportAllTaskFinishedEvents = true;
				this.ReportAllTaskStartedEvents = true;
				this.ReportTargetFinishedEvents = true;
				this.ReportTargetStartedEvents = true;
				this.ReportUnknownEvents = true;
				loggers.Add(new SDConsoleLogger(feedbackSink, LoggerVerbosity.Diagnostic));
				globalProperties["MSBuildTargetsVerbose"] = "true";
			}
			//loggers.Add(new BuildLogFileLogger(project.FileName + ".log", LoggerVerbosity.Diagnostic));
			foreach (IMSBuildAdditionalLogger loggerProvider in parentBuildEngine.AdditionalMSBuildLoggers) {
				loggers.Add(loggerProvider.CreateLogger(this));
			}
			
			loggerChain = new EndOfChain(this);
			foreach (IMSBuildLoggerFilter loggerFilter in parentBuildEngine.MSBuildLoggerFilters) {
				loggerChain = loggerFilter.CreateFilter(this, loggerChain) ?? loggerChain;
			}
			
			WriteAdditionalTargetsToTempFile(globalProperties);
			
			BuildJob job = new BuildJob();
			job.ProjectFileName = projectFileName;
			job.Target = options.Target.TargetName;
			
			// First remove the flags for the controllable events.
			job.EventMask = EventTypes.All & ~ControllableEvents;
			// Add back active controllable events.
			if (ReportMessageEvents)
				job.EventMask |= EventTypes.Message;
			if (ReportTargetStartedEvents)
				job.EventMask |= EventTypes.TargetStarted;
			if (ReportTargetFinishedEvents)
				job.EventMask |= EventTypes.TargetFinished;
			if (ReportAllTaskStartedEvents)
				job.EventMask |= EventTypes.TaskStarted;
			if (ReportAllTaskFinishedEvents)
				job.EventMask |= EventTypes.TaskFinished;
			if (ReportUnknownEvents)
				job.EventMask |= EventTypes.Unknown;
			
			if (!(ReportAllTaskStartedEvents && ReportAllTaskFinishedEvents)) {
				// just some TaskStarted & TaskFinished events should be reported
				job.InterestingTaskNames.AddRange(InterestingTasks);
			}
			foreach (var pair in globalProperties) {
				job.Properties.Add(pair.Key, pair.Value);
			}
			
			foreach (ILogger logger in loggers) {
				logger.Initialize(eventSource);
			}
			
			tcs = new TaskCompletionSource<bool>();
#if LIBREWPF
			RunBuildInProcessAsync(job, cancellationToken);
#else
			if (projectMinimumSolutionVersion <= SolutionFormatVersion.VS2008) {
				if (DotnetDetection.IsDotnet35SP1Installed()) {
					BuildWorkerManager.MSBuild35.RunBuildJob(job, loggerChain, OnDone, cancellationToken);
				} else {
					loggerChain.HandleError(new BuildError(job.ProjectFileName, ".NET 3.5 SP1 is required to build this project."));
					tcs.SetResult(false);
				}
			} else {
				if (DotnetDetection.IsBuildTools2015Installed()) {
					BuildWorkerManager.MSBuild140.RunBuildJob(job, loggerChain, OnDone, cancellationToken);
				} else if (DotnetDetection.IsBuildTools2013Installed()) {
					BuildWorkerManager.MSBuild120.RunBuildJob(job, loggerChain, OnDone, cancellationToken);
				} else {
					BuildWorkerManager.MSBuild40.RunBuildJob(job, loggerChain, OnDone, cancellationToken);
				}
			}
#endif
			return tcs.Task;
		}
		
		TaskCompletionSource<bool> tcs;

#if LIBREWPF
		void ConfigureLibreWpfTargetFrameworkReferenceAssemblies(Dictionary<string, string> globalProperties)
		{
			string sharpDevelopBinPath = Path.GetDirectoryName(typeof(MSBuildEngineWorker).Assembly.Location);
			if (!string.IsNullOrEmpty(sharpDevelopBinPath) && Directory.Exists(sharpDevelopBinPath)) {
				string referencePath;
				globalProperties.TryGetValue("ReferencePath", out referencePath);
				globalProperties["ReferencePath"] = AppendMSBuildPath(referencePath, sharpDevelopBinPath);
				globalProperties["SharpDevelopBinPath"] = sharpDevelopBinPath;

				string resourcesExtensionsPath = Path.Combine(sharpDevelopBinPath, "System.Resources.Extensions.dll");
				if (File.Exists(resourcesExtensionsPath)) {
					globalProperties["GenerateResourceUsePreserializedResources"] = "true";
					globalProperties["LibreWpfSystemResourcesExtensionsPath"] = resourcesExtensionsPath;
				}
			}

			string netCoreReferencePath = FindNetCoreReferencePath();
			if (!string.IsNullOrEmpty(netCoreReferencePath)) {
				globalProperties["LibreWpfNetCoreReferencePath"] = netCoreReferencePath;
			}

			string existingFrameworkPathOverride;
			if (globalProperties.TryGetValue("FrameworkPathOverride", out existingFrameworkPathOverride)
			    && Directory.Exists(existingFrameworkPathOverride))
			{
				return;
			}

			string targetFrameworkVersion = GetLibreWpfTargetFrameworkVersion();
			if (string.IsNullOrEmpty(targetFrameworkVersion))
				return;
			globalProperties["LibreWpfTargetFrameworkVersion"] = targetFrameworkVersion;

			string packageMoniker = ToNuGetFrameworkReferenceAssemblyMoniker(targetFrameworkVersion);
			if (string.IsNullOrEmpty(packageMoniker))
				return;

			string packageRoot = FindNuGetPackageRoot();
			if (string.IsNullOrEmpty(packageRoot))
				return;

			string packageDirectory = Path.Combine(packageRoot, "microsoft.netframework.referenceassemblies." + packageMoniker);
			if (!Directory.Exists(packageDirectory))
				return;

			foreach (string versionDirectory in Directory.GetDirectories(packageDirectory).OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)) {
				string targetFrameworkRootPath = Path.Combine(versionDirectory, "build");
				string frameworkPath = Path.Combine(targetFrameworkRootPath, ".NETFramework", targetFrameworkVersion);
				if (Directory.Exists(frameworkPath)) {
					globalProperties["TargetFrameworkRootPath"] = targetFrameworkRootPath;
					globalProperties["FrameworkPathOverride"] = frameworkPath;
					LoggingService.Debug("LibreWPF MSBuild using reference assemblies: " + frameworkPath);
					return;
				}
			}
		}

		string GetLibreWpfTargetFrameworkVersion()
		{
			CompilableProject compilableProject = project as CompilableProject;
			string targetFrameworkVersion = compilableProject != null ? NormalizeTargetFrameworkVersion(compilableProject.TargetFrameworkVersion) : null;
			if (!string.IsNullOrEmpty(targetFrameworkVersion))
				return targetFrameworkVersion;

			return ReadTargetFrameworkVersionFromProject(projectFileName);
		}

		static string ReadTargetFrameworkVersionFromProject(string projectFileName)
		{
			if (string.IsNullOrEmpty(projectFileName) || !File.Exists(projectFileName))
				return null;

			using (XmlReader reader = XmlReader.Create(projectFileName)) {
				while (reader.Read()) {
					if (reader.NodeType != XmlNodeType.Element)
						continue;

					if (string.Equals(reader.LocalName, "TargetFrameworkVersion", StringComparison.Ordinal)) {
						return NormalizeTargetFrameworkVersion(reader.ReadElementContentAsString());
					}

					if (string.Equals(reader.LocalName, "TargetFramework", StringComparison.Ordinal)) {
						string targetFrameworkVersion = NormalizeTargetFrameworkVersion(reader.ReadElementContentAsString());
						if (!string.IsNullOrEmpty(targetFrameworkVersion))
							return targetFrameworkVersion;
					}
				}
			}

			return null;
		}

		static string NormalizeTargetFrameworkVersion(string value)
		{
			if (string.IsNullOrEmpty(value))
				return null;

			value = value.Trim();
			int versionIndex = value.IndexOf("Version=", StringComparison.OrdinalIgnoreCase);
			if (versionIndex >= 0) {
				value = value.Substring(versionIndex + "Version=".Length).Trim();
			}

			if (value.Length > 0 && (value[0] == 'v' || value[0] == 'V'))
				return "v" + value.Substring(1);

			if (!value.StartsWith("net", StringComparison.OrdinalIgnoreCase))
				return null;

			string digits = value.Substring(3);
			if (digits.Length < 2 || digits.Any(ch => !char.IsDigit(ch)))
				return null;

			StringBuilder builder = new StringBuilder("v");
			builder.Append(digits[0]);
			builder.Append('.');
			builder.Append(digits[1]);
			if (digits.Length > 2) {
				builder.Append('.');
				builder.Append(digits.Substring(2));
			}
			return builder.ToString();
		}

		static string FindNuGetPackageRoot()
		{
			string packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (!string.IsNullOrEmpty(packageRoot) && Directory.Exists(packageRoot))
				return Path.GetFullPath(packageRoot);

			string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (string.IsNullOrEmpty(userProfile))
				return null;

			packageRoot = Path.Combine(userProfile, ".nuget", "packages");
			return Directory.Exists(packageRoot) ? packageRoot : null;
		}

		static string FindNetCoreReferencePath()
		{
			string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
			string referencePath = FindNetCoreReferencePath(dotnetRoot);
			if (!string.IsNullOrEmpty(referencePath))
				return referencePath;

			string coreLibPath = typeof(object).Assembly.Location;
			if (string.IsNullOrEmpty(coreLibPath))
				return null;

			DirectoryInfo directory = Directory.GetParent(coreLibPath);
			while (directory != null) {
				referencePath = FindNetCoreReferencePath(directory.FullName);
				if (!string.IsNullOrEmpty(referencePath))
					return referencePath;
				directory = directory.Parent;
			}

			return null;
		}

		static string FindNetCoreReferencePath(string dotnetRoot)
		{
			if (string.IsNullOrEmpty(dotnetRoot))
				return null;

			string packRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
			if (!Directory.Exists(packRoot))
				return null;

			foreach (string versionDirectory in Directory.GetDirectories(packRoot).OrderByDescending(GetVersionDirectoryVersion)) {
				string net10ReferencePath = Path.Combine(versionDirectory, "ref", "net10.0");
				if (Directory.Exists(net10ReferencePath))
					return net10ReferencePath;
			}

			return null;
		}

		static Version GetVersionDirectoryVersion(string path)
		{
			Version version;
			return Version.TryParse(Path.GetFileName(path), out version) ? version : new Version(0, 0);
		}

		static string ToNuGetFrameworkReferenceAssemblyMoniker(string targetFrameworkVersion)
		{
			if (targetFrameworkVersion.Length < 2 || targetFrameworkVersion[0] != 'v')
				return null;

			StringBuilder builder = new StringBuilder("net");
			for (int i = 1; i < targetFrameworkVersion.Length; i++) {
				char ch = targetFrameworkVersion[i];
				if (char.IsDigit(ch))
					builder.Append(ch);
			}

			return builder.Length > 3 ? builder.ToString() : null;
		}

		static string AppendMSBuildPath(string currentValue, string path)
		{
			if (string.IsNullOrEmpty(currentValue))
				return path;
			return currentValue + ";" + path;
		}

		void RunBuildInProcessAsync(BuildJob job, CancellationToken cancellationToken)
		{
			Task.Run(delegate {
				bool success = false;
				Process process = null;
				try {
					using (cancellationToken.Register(delegate {
						if (process != null && !process.HasExited)
							process.Kill();
					})) {
						string[] targets = string.IsNullOrEmpty(job.Target) ? new string[0] : new[] { job.Target };
						TraceLibreWpfBuildJob(job, targets);
						process = StartDotnetMSBuild(job, targets);
						process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
							HandleDotnetMSBuildLine(e.Data, false);
						};
						process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
							HandleDotnetMSBuildLine(e.Data, true);
						};
						process.BeginOutputReadLine();
						process.BeginErrorReadLine();
						process.WaitForExit();
						success = process.ExitCode == 0;
						if (!success)
							loggerChain.HandleError(new BuildError(job.ProjectFileName, "dotnet msbuild exited with code " + process.ExitCode + ".") { ParentProject = project });
					}
				} catch (Exception ex) {
					loggerChain.HandleError(new BuildError(job.ProjectFileName, ex.Message) {
						ParentProject = project
					});
					LoggingService.Warn("LibreWPF in-process MSBuild failed.", ex);
				} finally {
					OnDone(success);
				}
			});
		}

		Process StartDotnetMSBuild(BuildJob job, string[] targets)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo("dotnet") {
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(job.ProjectFileName)
			};
			startInfo.ArgumentList.Add("msbuild");
			startInfo.ArgumentList.Add(job.ProjectFileName);
			if (targets.Length > 0)
				startInfo.ArgumentList.Add("/t:" + string.Join(";", targets));
			startInfo.ArgumentList.Add("/nologo");
			startInfo.ArgumentList.Add("/v:minimal");
			startInfo.ArgumentList.Add("/nr:false");
			foreach (KeyValuePair<string, string> pair in job.Properties) {
				startInfo.ArgumentList.Add("/p:" + pair.Key + "=" + EscapeMSBuildCommandLinePropertyValue(pair.Value));
			}

			if (IsLibreWpfBuildTraceEnabled()) {
				Console.WriteLine("LibreWPF MSBuild external command: dotnet " + string.Join(" ", startInfo.ArgumentList.Select(QuoteArgument)));
			}

			Process process = new Process { StartInfo = startInfo };
			process.Start();
			return process;
		}

		static string EscapeMSBuildCommandLinePropertyValue(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			return value
				.Replace("%", "%25")
				.Replace(";", "%3B")
				.Replace("\"", "%22");
		}

		static string QuoteArgument(string value)
		{
			if (string.IsNullOrEmpty(value))
				return "\"\"";
			if (value.IndexOfAny(new[] { ' ', '\t', '\n', '"' }) < 0)
				return value;
			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}

		static readonly Regex DotnetMSBuildDiagnosticPattern = new Regex(@"^(?<file>.+)\((?<line>\d+),(?<column>\d+)(?:,\d+,\d+)?\): (?<kind>warning|error) (?<code>[A-Z]+\d+): (?<message>.*?)(?: \[[^\]]+\])?$", RegexOptions.Compiled);

		void HandleDotnetMSBuildLine(string line, bool isErrorStream)
		{
			if (string.IsNullOrEmpty(line))
				return;

			Match match = DotnetMSBuildDiagnosticPattern.Match(line);
			if (match.Success) {
				BuildError error = new BuildError(
					match.Groups["file"].Value,
					int.Parse(match.Groups["line"].Value),
					int.Parse(match.Groups["column"].Value),
					match.Groups["code"].Value,
					match.Groups["message"].Value);
				error.IsWarning = string.Equals(match.Groups["kind"].Value, "warning", StringComparison.OrdinalIgnoreCase);
				error.ParentProject = project;
				loggerChain.HandleError(error);
			} else if (isErrorStream) {
				loggerChain.HandleError(new BuildError(projectFileName, line) { ParentProject = project });
			} else {
				loggerChain.HandleBuildEvent(new BuildMessageEventArgs(line, "", "dotnet msbuild", MessageImportance.High));
			}
		}

		void ReportBuildResultFailure(BuildJob job, Microsoft.Build.Execution.BuildResult result)
		{
			if (result == null) {
				loggerChain.HandleError(new BuildError(job.ProjectFileName, "MSBuild returned no build result.") {
					ParentProject = project
				});
				return;
			}

			if (result.Exception != null) {
				loggerChain.HandleError(new BuildError(job.ProjectFileName, result.Exception.Message) {
					ParentProject = project
				});
			}

			foreach (KeyValuePair<string, TargetResult> pair in result.ResultsByTarget) {
				TargetResult targetResult = pair.Value;
				if (targetResult != null && IsLibreWpfBuildTraceEnabled()) {
					Console.WriteLine("LibreWPF MSBuild target result: " + pair.Key + "=" + targetResult.ResultCode);
				}
				if (targetResult != null && targetResult.Exception != null) {
					loggerChain.HandleError(new BuildError(job.ProjectFileName, pair.Key + ": " + targetResult.Exception.Message) {
						ParentProject = project
					});
				}
			}
		}

		static bool IsLibreWpfBuildTraceEnabled()
		{
			return string.Equals(Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_MSBUILD_TRACE"), "1", StringComparison.Ordinal);
		}

		static string CreateLibreWpfBuildTraceLogPath(string projectFileName)
		{
			string path = Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_MSBUILD_LOG");
			if (!string.IsNullOrEmpty(path))
				return path;

			string projectName = Path.GetFileNameWithoutExtension(projectFileName);
			if (string.IsNullOrEmpty(projectName))
				projectName = "project";
			return Path.Combine(Path.GetTempPath(), "sharpdevelop-librewpf-msbuild-" + projectName + "-" + Guid.NewGuid().ToString("N") + ".log");
		}

		static void TraceLibreWpfBuildJob(BuildJob job, string[] targets)
		{
			if (!IsLibreWpfBuildTraceEnabled())
				return;

			Console.WriteLine("LibreWPF MSBuild project: " + job.ProjectFileName);
			Console.WriteLine("LibreWPF MSBuild targets: " + string.Join(",", targets));
			string[] propertyNames = {
				"Configuration",
				"Platform",
				"BuildingSolutionFile",
				"BuildingInsideVisualStudio",
				"MsTestToolsTargets",
				"TargetFrameworkRootPath",
				"FrameworkPathOverride",
				"LibreWpfTargetFrameworkVersion",
				"ReferencePath",
				"SharpDevelopBinPath",
				"LibreWpfNetCoreReferencePath",
				"LibreWpfSystemResourcesExtensionsPath",
				"GenerateResourceUsePreserializedResources"
			};
			foreach (string propertyName in propertyNames) {
				string value;
				if (job.Properties.TryGetValue(propertyName, out value)) {
					Console.WriteLine("LibreWPF MSBuild property " + propertyName + "=" + value);
				}
			}
		}

		sealed class LoggerChainForwarder : ILogger
		{
			readonly IMSBuildChainedLoggerFilter loggerChain;
			Microsoft.Build.Framework.IEventSource eventSource;

			public LoggerChainForwarder(IMSBuildChainedLoggerFilter loggerChain)
			{
				this.loggerChain = loggerChain;
			}

			public LoggerVerbosity Verbosity { get; set; }
			public string Parameters { get; set; }

			public void Initialize(Microsoft.Build.Framework.IEventSource eventSource)
			{
				this.eventSource = eventSource;
				eventSource.AnyEventRaised += OnAnyEventRaised;
			}

			public void Shutdown()
			{
				if (eventSource != null) {
					eventSource.AnyEventRaised -= OnAnyEventRaised;
					eventSource = null;
				}
			}

			void OnAnyEventRaised(object sender, Microsoft.Build.Framework.BuildEventArgs e)
			{
				loggerChain.HandleBuildEvent(e);
			}
		}

		sealed class LibreWpfBuildTraceLogger : ILogger
		{
			readonly string path;
			StreamWriter writer;

			public LibreWpfBuildTraceLogger(string path)
			{
				this.path = path;
			}

			public LoggerVerbosity Verbosity { get; set; }
			public string Parameters { get; set; }

			public void Initialize(Microsoft.Build.Framework.IEventSource eventSource)
			{
				writer = new StreamWriter(path, false, Encoding.UTF8);
				writer.AutoFlush = true;
				Console.WriteLine("LibreWPF MSBuild event log: " + path);
				eventSource.AnyEventRaised += OnAnyEventRaised;
			}

			public void Shutdown()
			{
				if (writer != null) {
					writer.Dispose();
					writer = null;
				}
			}

			void OnAnyEventRaised(object sender, Microsoft.Build.Framework.BuildEventArgs e)
			{
				if (writer == null)
					return;

				writer.Write(e.Timestamp.ToString("O"));
				writer.Write(" ");
				writer.Write(e.GetType().Name);

				BuildErrorEventArgs error = e as BuildErrorEventArgs;
				if (error != null) {
					writer.Write(" ERROR ");
					WriteLocation(error.File, error.LineNumber, error.ColumnNumber, error.Code, error.ProjectFile);
				}

				BuildWarningEventArgs warning = e as BuildWarningEventArgs;
				if (warning != null) {
					writer.Write(" WARNING ");
					WriteLocation(warning.File, warning.LineNumber, warning.ColumnNumber, warning.Code, warning.ProjectFile);
				}

				TargetStartedEventArgs targetStarted = e as TargetStartedEventArgs;
				if (targetStarted != null) {
					writer.Write(" target=");
					writer.Write(targetStarted.TargetName);
					writer.Write(" project=");
					writer.Write(targetStarted.ProjectFile);
				}

				TargetFinishedEventArgs targetFinished = e as TargetFinishedEventArgs;
				if (targetFinished != null) {
					writer.Write(" target=");
					writer.Write(targetFinished.TargetName);
					writer.Write(" succeeded=");
					writer.Write(targetFinished.Succeeded);
					writer.Write(" project=");
					writer.Write(targetFinished.ProjectFile);
				}

				TaskStartedEventArgs taskStarted = e as TaskStartedEventArgs;
				if (taskStarted != null) {
					writer.Write(" task=");
					writer.Write(taskStarted.TaskName);
					writer.Write(" project=");
					writer.Write(taskStarted.ProjectFile);
				}

				TaskFinishedEventArgs taskFinished = e as TaskFinishedEventArgs;
				if (taskFinished != null) {
					writer.Write(" task=");
					writer.Write(taskFinished.TaskName);
					writer.Write(" succeeded=");
					writer.Write(taskFinished.Succeeded);
					writer.Write(" project=");
					writer.Write(taskFinished.ProjectFile);
				}

				if (!string.IsNullOrEmpty(e.Message)) {
					writer.Write(" message=");
					writer.Write(e.Message.Replace('\r', ' ').Replace('\n', ' '));
				}

				writer.WriteLine();
			}

			void WriteLocation(string file, int line, int column, string code, string projectFile)
			{
				writer.Write(file);
				writer.Write("(");
				writer.Write(line);
				writer.Write(",");
				writer.Write(column);
				writer.Write(") ");
				writer.Write(code);
				writer.Write(" project=");
				writer.Write(projectFile);
			}
		}
#endif
		
		void OnDone(bool success)
		{
			foreach (ILogger logger in loggers) {
				logger.Shutdown();
			}
			tcs.SetResult(success);
		}
		
		void WriteAdditionalTargetsToTempFile(Dictionary<string, string> globalProperties)
		{
			// Using projects with in-memory modifications doesn't work with parallel build.
			// As a work-around, we'll write our modifications to a file and force MSBuild to include that file using a custom property.
			temporaryFileName = Path.GetTempFileName();
			using (XmlWriter w = new XmlTextWriter(temporaryFileName, Encoding.UTF8)) {
				const string xmlNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
				w.WriteStartElement("Project", xmlNamespace);
				
				foreach (string import in additionalTargetFiles) {
					w.WriteStartElement("Import", xmlNamespace);
					w.WriteAttributeString("Project", MSBuildInternals.Escape(import));
					w.WriteEndElement();
				}
				
				if (globalProperties.ContainsKey("BuildingInsideVisualStudio")) {
					// When we set BuildingInsideVisualStudio, MSBuild skips its own change detection
					// because in Visual Studio, the host compiler does the change detection.
					// We override the target '_ComputeNonExistentFileProperty' which is responsible
					// for recompiling each time - our _ComputeNonExistentFileProperty does nothing,
					// which re-enables the MSBuild's usual change detection.
					w.WriteStartElement("Target", xmlNamespace);
					w.WriteAttributeString("Name", "_ComputeNonExistentFileProperty");
					w.WriteEndElement();
				}
				
				// 'MsTestToolsTargets' is preferred because it's at the end of the MSBuild 3.5 and 4.0 target file,
				// but on MSBuild 2.0 we need to fall back to 'CodeAnalysisTargets'.
				string hijackedProperty = "MsTestToolsTargets";
				if (projectMinimumSolutionVersion == SolutionFormatVersion.VS2005)
					hijackedProperty = "CodeAnalysisTargets";
				
				// because we'll replace the hijackedProperty, manually write the corresponding include
				if (globalProperties.ContainsKey(hijackedProperty)) {
					// global properties passed to MSBuild are not be evaluated (and are not escaped),
					// so we need to escape them for writing them into an MSBuild file
					w.WriteStartElement("Import", xmlNamespace);
					w.WriteAttributeString("Project", MSBuildInternals.Escape(globalProperties[hijackedProperty]));
					w.WriteEndElement();
				}
#if LIBREWPF
				if (globalProperties.ContainsKey("LibreWpfSystemResourcesExtensionsPath")) {
					w.WriteStartElement("ItemGroup", xmlNamespace);
					w.WriteStartElement("Reference", xmlNamespace);
					w.WriteAttributeString("Include", "System.Resources.Extensions");
					w.WriteStartElement("HintPath", xmlNamespace);
					w.WriteString("$(LibreWpfSystemResourcesExtensionsPath)");
					w.WriteEndElement();
					w.WriteStartElement("Private", xmlNamespace);
					w.WriteString("false");
					w.WriteEndElement();
					w.WriteEndElement();
					w.WriteEndElement();
				}
				if (globalProperties.ContainsKey("LibreWpfNetCoreReferencePath")
				    && globalProperties.ContainsKey("SharpDevelopBinPath"))
				{
					w.WriteStartElement("Target", xmlNamespace);
					w.WriteAttributeString("Name", "LibreWpfRetargetReferences");
					w.WriteAttributeString("BeforeTargets", "CoreCompile");

					w.WriteStartElement("ItemGroup", xmlNamespace);
					w.WriteStartElement("ReferencePathWithRefAssemblies", xmlNamespace);
					w.WriteAttributeString("Remove", "@(ReferencePathWithRefAssemblies)");
					w.WriteEndElement();
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(LibreWpfNetCoreReferencePath)/*.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/ICSharpCode*.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/AvalonDock.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/PresentationCore.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/PresentationFramework.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/PresentationUI.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/ReachFramework.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/System.Xaml.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/System.Drawing.Common.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/System.Windows.Forms.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/WindowsBase.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/WindowsFormsIntegration.dll");
					WriteLibreWpfReferencePath(w, xmlNamespace, "$(SharpDevelopBinPath)/System.Resources.Extensions.dll");
					w.WriteEndElement();

					w.WriteEndElement();
				}
#endif
				w.WriteEndElement();
				
				// inject our imports at the end of 'Microsoft.Common.Targets' by replacing the hijackedProperty.
				globalProperties[hijackedProperty] = temporaryFileName;
			}
			
			#if DEBUG
			LoggingService.Debug(File.ReadAllText(temporaryFileName));
			#endif
#if LIBREWPF
			if (IsLibreWpfBuildTraceEnabled()) {
				Console.WriteLine("LibreWPF MSBuild temporary targets: " + temporaryFileName);
				Console.WriteLine(File.ReadAllText(temporaryFileName));
			}
#endif
		}

#if LIBREWPF
		static void WriteLibreWpfReferencePath(XmlWriter w, string xmlNamespace, string include)
		{
			w.WriteStartElement("ReferencePathWithRefAssemblies", xmlNamespace);
			w.WriteAttributeString("Include", include);
			w.WriteEndElement();
		}
#endif
		
		public void OutputTextLine(string message)
		{
			feedbackSink.ReportMessage(message);
		}
		
		public void ReportError(BuildError error)
		{
			feedbackSink.ReportError(error);
		}
		
		sealed class EndOfChain : IMSBuildChainedLoggerFilter
		{
			readonly MSBuildEngineWorker engine;
			
			public EndOfChain(MSBuildEngineWorker engine)
			{
				this.engine = engine;
			}
			
			public void HandleError(BuildError error)
			{
				engine.ReportError(error);
			}
			
			public void HandleBuildEvent(Microsoft.Build.Framework.BuildEventArgs e)
			{
				engine.eventSource.ForwardEvent(e);
			}
		}
		
		sealed class SharpDevelopLogger : ILogger
		{
			readonly MSBuildEngineWorker engine;
			
			public SharpDevelopLogger(MSBuildEngineWorker engine)
			{
				this.engine = engine;
			}
			
			string activeTaskName;
			
			void OnTaskStarted(object sender, TaskStartedEventArgs e)
			{
				activeTaskName = e.TaskName;
				if (engine.parentBuildEngine.CompileTaskNames.Contains(e.TaskName)) {
					engine.OutputTextLine(StringParser.Parse("${res:MainWindow.CompilerMessages.CompileVerb} " + Path.GetFileNameWithoutExtension(e.ProjectFile)));
				}
			}
			
			void OnError(object sender, BuildErrorEventArgs e)
			{
				AppendError(e.File, e.LineNumber, e.ColumnNumber, e.Code, e.Message, e.ProjectFile, e.Subcategory, e.HelpKeyword, false);
			}
			
			void OnWarning(object sender, BuildWarningEventArgs e)
			{
				AppendError(e.File, e.LineNumber, e.ColumnNumber, e.Code, e.Message, e.ProjectFile, e.Subcategory, e.HelpKeyword, true);
			}
			
			void AppendError(string file, int lineNumber, int columnNumber, string code, string message, string projectFile, string subcategory, string helpKeyword, bool isWarning)
			{
				if (string.IsNullOrEmpty(file) || string.Equals(file, activeTaskName, StringComparison.OrdinalIgnoreCase)) {
					file = "";
				} else if (FileUtility.IsValidPath(file)) {
					bool isShortFileName = file == Path.GetFileNameWithoutExtension(file);
					if (engine.ProjectFileName != null) {
						string projectDirectory = Path.GetDirectoryName(engine.ProjectFileName);
						if (!string.IsNullOrEmpty(projectDirectory)) {
							file = Path.Combine(projectDirectory, file);
						}
					}
					if (isShortFileName && !File.Exists(file)) {
						file = "";
					}
#if !LIBREWPF
					//TODO: Do we have to check for other SDKs here.
					else if (!string.IsNullOrEmpty(file)
					         && file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
					         && ((!string.IsNullOrEmpty(FileUtility.NetFrameworkInstallRoot)
					              && FileUtility.IsBaseDirectory(FileUtility.NetFrameworkInstallRoot, file))
					             || (!string.IsNullOrEmpty(FileUtility.ApplicationRootPath)
					                 && FileUtility.IsBaseDirectory(FileUtility.ApplicationRootPath, file))))
					{
						file = "";
					}
#endif
				}
				BuildError error = new BuildError(file, lineNumber, columnNumber, code, message);
				error.IsWarning = isWarning;
				error.Subcategory = subcategory;
				error.HelpKeyword = helpKeyword;
				error.ParentProject = engine.Project;
				engine.loggerChain.HandleError(error);
			}
			
			#region ILogger interface implementation
			public LoggerVerbosity Verbosity { get; set; }
			public string Parameters { get; set; }
			
			public void Initialize(IEventSource eventSource)
			{
				eventSource.TaskStarted     += OnTaskStarted;
				
				eventSource.ErrorRaised     += OnError;
				eventSource.WarningRaised   += OnWarning;
			}
			
			public void Shutdown()
			{
				if (engine.temporaryFileName != null) {
					if (!string.Equals(Environment.GetEnvironmentVariable("LIBREWPF_SHARPDEVELOP_KEEP_MSBUILD_TEMP"), "1", StringComparison.Ordinal)) {
						File.Delete(engine.temporaryFileName);
					}
					engine.temporaryFileName = null;
				}
			}
			#endregion
		}
	}
}
