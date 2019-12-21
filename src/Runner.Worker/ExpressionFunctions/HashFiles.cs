using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Minimatch;
using System.IO;
using System.Security.Cryptography;
using GitHub.DistributedTask.Expressions2.Sdk;
using GitHub.DistributedTask.Pipelines.ContextData;
using GitHub.DistributedTask.Pipelines.ObjectTemplating;
using GitHub.Runner.Common;
using GitHub.Runner.Sdk;
using System.Reflection;
using System.Threading;
using System.Runtime.Serialization;

namespace GitHub.Runner.Worker.Handlers
{
    public class FunctionTrace : ITraceWriter
    {
        private GitHub.DistributedTask.Expressions2.ITraceWriter _trace;

        public FunctionTrace(GitHub.DistributedTask.Expressions2.ITraceWriter trace)
        {
            _trace = trace;
        }
        public void Info(string message)
        {
            _trace.Info(message);
        }

        public void Verbose(string message)
        {
            _trace.Info(message);
        }
    }

    [DataContract]
    public class ScriptOutput
    {
        [DataMember]
        public List<string> Files { get; set; }

        [DataMember]
        public List<string> Logs { get; set; }
    }

    public sealed class HashFiles : Function
    {
        protected sealed override Object EvaluateCore(
            EvaluationContext context,
            out ResultMemory resultMemory)
        {
            resultMemory = null;

            // hashFiles() only works on the runner and only works with files under GITHUB_WORKSPACE
            // Since GITHUB_WORKSPACE is set by runner, I am using that as the fact of this code runs on server or runner.
            if (context.State is DistributedTask.ObjectTemplating.TemplateContext templateContext &&
                templateContext.ExpressionValues.TryGetValue(PipelineTemplateConstants.GitHub, out var githubContextData) &&
                githubContextData is DictionaryContextData githubContext &&
                githubContext.TryGetValue(PipelineTemplateConstants.Workspace, out var workspace) == true &&
                workspace is StringContextData workspaceData)
            {
                string searchRoot = workspaceData.Value;
                string pattern = Parameters[0].Evaluate(context).ConvertToString();

                // Convert slashes on Windows
                if (s_isWindows)
                {
                    pattern = pattern.Replace('\\', '/');
                }

                // Root the pattern
                if (!Path.IsPathRooted(pattern))
                {
                    var patternRoot = s_isWindows ? searchRoot.Replace('\\', '/').TrimEnd('/') : searchRoot.TrimEnd('/');
                    pattern = string.Concat(patternRoot, "/", pattern);
                }

                // Get all files
                context.Trace.Info($"Search root directory: '{searchRoot}'");
                context.Trace.Info($"Search pattern: '{pattern}'");

                string binDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string runnerRoot = new DirectoryInfo(binDir).Parent.FullName;

                string node = Path.Combine(runnerRoot, "externals", "node12", "bin", "node");
                if (s_isWindows)
                {
                    node = node + ".exe";
                }
                string findFilesScript = Path.Combine(binDir, "findFiles.js");
                string stdErr = null;
                string stdOut = null;
                var p = new ProcessInvoker(new FunctionTrace(context.Trace));
                p.ErrorDataReceived += ((_, data) => { stdErr = data.Data; });
                p.OutputDataReceived += ((_, data) => { stdOut = data.Data; });
                int exitCode = p.ExecuteAsync(workingDirectory: searchRoot,
                                              fileName: node,
                                              arguments: findFilesScript,
                                              environment: null,
                                              requireExitCodeZero: false,
                                              cancellationToken: new CancellationTokenSource(TimeSpan.FromMinutes(30)).Token).GetAwaiter().GetResult();

                ScriptOutput output = null;
                if (!string.IsNullOrEmpty(stdErr))
                {
                    output = StringUtil.ConvertFromJson<ScriptOutput>(stdErr);
                    if (output.Logs?.Count > 0)
                    {
                        foreach (var log in output.Logs)
                        {
                            context.Trace.Info(log);
                        }
                    }
                }

                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"hashFiles('{ExpressionUtility.StringEscape(pattern)}') failed. Fail to discover files under directory '{searchRoot}'");
                }

                var files = output?.Files?.Select(x => s_isWindows ? x.Replace('\\', '/') : x)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                if (files.Count == 0)
                {
                    throw new ArgumentException($"hashFiles('{ExpressionUtility.StringEscape(pattern)}') failed. Directory '{searchRoot}' is empty");
                }
                else
                {
                    context.Trace.Info($"Found {files.Count} files");
                }

                // Match
                var matcher = new Minimatcher(pattern, s_minimatchOptions);
                files = matcher.Filter(files)
                    .Select(x => s_isWindows ? x.Replace('/', '\\') : x)
                    .ToList();
                if (files.Count == 0)
                {
                    throw new ArgumentException($"hashFiles('{ExpressionUtility.StringEscape(pattern)}') failed. Search pattern '{pattern}' doesn't match any file under '{searchRoot}'");
                }
                else
                {
                    context.Trace.Info($"{files.Count} matches to hash");
                }

                // Hash each file
                List<byte> filesSha256 = new List<byte>();
                foreach (var file in files)
                {
                    context.Trace.Info($"Hash {file}");
                    using (SHA256 sha256hash = SHA256.Create())
                    {
                        using (var fileStream = File.OpenRead(file))
                        {
                            filesSha256.AddRange(sha256hash.ComputeHash(fileStream));
                        }
                    }
                }

                // Hash the hashes
                using (SHA256 sha256hash = SHA256.Create())
                {
                    var hashBytes = sha256hash.ComputeHash(filesSha256.ToArray());
                    StringBuilder hashString = new StringBuilder();
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        hashString.Append(hashBytes[i].ToString("x2"));
                    }
                    var result = hashString.ToString();
                    context.Trace.Info($"Final hash result: '{result}'");
                    return result;
                }
            }
            else
            {
                throw new InvalidOperationException("'hashfiles' expression function is only supported under runner context.");
            }
        }

        private static readonly bool s_isWindows = Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX;

        // Only support basic globbing (* ? and []) and globstar (**)
        private static readonly Options s_minimatchOptions = new Options
        {
            Dot = true,
            NoBrace = true,
            NoCase = s_isWindows,
            NoComment = true,
            NoExt = true,
            NoNegate = true,
        };
    }
}