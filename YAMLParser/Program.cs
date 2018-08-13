using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FauxMessages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Reflection;
using Uml.Robotics.Ros;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.CommandLineUtils;

namespace YAMLParser
{
    internal class Program
    {
        static List<MsgFile> msgsFiles = new List<MsgFile>();
        static List<SrvFile> srvFiles = new List<SrvFile>();
        static List<ActionFile> actionFiles = new List<ActionFile>();
        private static ILogger Logger { get; set; }
        const string DEFAULT_OUTPUT_FOLDERNAME = "Messages";
        static readonly string[] required_packages = {"std_msgs", "geometry_msgs", /*"actionlib_msgs",*/ "sensor_msgs"};

        public static void Main(params string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: true);

            CommandOption messageDirectories = app.Option("-m|--message-dirs", "Directories where ROS message definitions are located, separated by comma. (required)", CommandOptionType.MultipleValue);
            CommandOption assemblies = app.Option("-a|--assemblies", "Full filename of assemblies that contain additional generated RosMessages. (optional)", CommandOptionType.MultipleValue);
            CommandOption interactive = app.Option("-i|--interactive", "Run in interactive mode. Default: false", CommandOptionType.NoValue);
            // Change of output directory requires more work, since the reference to Uml.Robotics.Ros.MessageBase needs to be adjusted
            CommandOption outputDirectory = app.Option("-o|--output", "Output directory for generated message. Default: ../Messages", CommandOptionType.SingleValue);
            CommandOption runtime = app.Option("-r|--runtime", "Specify runtime, e.g. Debug or Release. Default: Debug", CommandOptionType.SingleValue);
            CommandOption projectName = app.Option("-n|--name", "Name of the generated project file. Default: Messages", CommandOptionType.SingleValue);

            app.HelpOption("-? | -h | --help");

            app.OnExecute(() =>
            {
                if (!messageDirectories.HasValue())
                {
                    Console.WriteLine("At least one directory with ROS message definitions is required.");

                    return 1;
                }

                List<string> dirs = messageDirectories.Value().Split(',').ToList();

                Program.Run(
                    messageDirectories.HasValue() ? dirs : null,
                    assemblies.HasValue() ? assemblies.Values : null,
                    outputDirectory.HasValue() ? outputDirectory.Value() : null,
                    interactive.HasValue(),
                    runtime.HasValue() ? runtime.Value() : "Debug",
                    projectName.HasValue() ? projectName.Value() : "Messages"
                );

                return 0;
            });

            app.Execute(args);
        }

        private static void Run(List<string> messageDirs, List<string> assemblies = null, string outputdir = null, bool interactive = false, string configuration = "Debug", string projectName = "Messages")
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(
                new ConsoleLoggerProvider(
                    (string text, LogLevel logLevel) => { return logLevel >= LogLevel.Debug; }, true)
            );
            ApplicationLogging.LoggerFactory = loggerFactory;
            Logger = ApplicationLogging.CreateLogger("Program");

            string yamlparser_parent = "";
            DirectoryInfo di = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (di != null && di.Name != "YAMLParser")
            {
                di = Directory.GetParent(di.FullName);
            }
            if (di == null)
                throw new InvalidOperationException("Not started from within YAMLParser directory.");
            di = Directory.GetParent(di.FullName);
            yamlparser_parent = di.FullName;

            if (outputdir == null)
            {
                outputdir = yamlparser_parent;
                outputdir = Path.Combine(outputdir, DEFAULT_OUTPUT_FOLDERNAME);
            }

            Templates.LoadTemplateStrings(Path.Combine(yamlparser_parent, "YAMLParser", "TemplateProject"));

            MessageTypeRegistry.Default.ParseAssemblyAndRegisterRosMessages(MessageTypeRegistry.Default.GetType().GetTypeInfo().Assembly);

            if (assemblies != null)
            {
                string hints = "";
                foreach (var assembly in assemblies)
                {
                    var rosNetMessages = Assembly.LoadFile(Path.GetFullPath(assembly));
                    MessageTypeRegistry.Default.ParseAssemblyAndRegisterRosMessages(rosNetMessages);
                    hints += $@"
  <ItemGroup>
    <Reference Include=""Messages"">
      <HintPath>{assembly}</HintPath>
  
      </Reference>
  
    </ItemGroup>

  ";
                }

                Templates.MessagesProj = Templates.MessagesProj.Replace("$$HINTS$$", hints);
            }
            else
            {
                Templates.MessagesProj = Templates.MessagesProj.Replace("$$HINTS$$", "");
            }

            var paths = new List<MsgFileLocation>();
            var pathssrv = new List<MsgFileLocation>();
            var actionFileLocations = new List<MsgFileLocation>();
            Console.WriteLine("Generatinc C# classes for ROS Messages...\n");
            foreach (var messageDir in messageDirs)
            {
                string d = new DirectoryInfo(Path.GetFullPath(messageDir)).FullName;
                Console.WriteLine("Looking in " + d);
                MsgFileLocator.findMessages(paths, pathssrv, actionFileLocations, d);
            }
            // sort paths by priority
            paths = MsgFileLocator.sortMessages(paths);

            // first pass: create all msg files (and register them in static resolver dictionary)
            var baseTypes = MessageTypeRegistry.Default.GetTypeNames().ToList();
            foreach (MsgFileLocation path in paths)
            {
                var typeName = $"{path.package}/{path.basename}";
                if (baseTypes.Contains(typeName))
                {
                    Logger.LogInformation($"Skip file {path} because MessageBase already contains this message");
                }
                else
                {
                    msgsFiles.Add(new MsgFile(path));
                }
            }
            Logger.LogDebug($"Added {msgsFiles.Count} message files");

            foreach (MsgFileLocation path in pathssrv)
            {
                srvFiles.Add(new SrvFile(path));
            }

            // secend pass: parse and resolve types
            foreach (var msg in msgsFiles)
            {
                msg.ParseAndResolveTypes();
            }
            foreach (var srv in srvFiles)
            {
                srv.ParseAndResolveTypes();
            }

            var actionFileParser = new ActionFileParser(actionFileLocations);
            actionFiles = actionFileParser.GenerateRosMessageClasses();
            //var actionFiles = new List<ActionFile>();

            if (!StdMsgsProcessed()) // may seem obvious, but needed so that all other messages can build...
            {
                string resolvedPkgs = String.Join(", ", MsgFile.resolver.Keys.OrderBy(x => x.ToString()).ToArray());
                Console.WriteLine("Missing at least one of the following ROS packages: [\"" + String.Join("\", \"", required_packages) + "\"]. Exiting...");
                Console.WriteLine("resolver's keys: [" + resolvedPkgs + "]");
                return;
            }

            if (paths.Count + pathssrv.Count > 0)
            {
                MakeTempDir(outputdir);
                GenerateFiles(msgsFiles, srvFiles, actionFiles, outputdir);
                GenerateProject(msgsFiles, srvFiles, projectName, outputdir);
                BuildProject(configuration, projectName, outputdir);
            }
            else
            {
                Console.WriteLine("Usage:         YAMLParser.exe <SolutionFolder> [... other directories to search]\n      The Messages dll will be output to <SolutionFolder>/Messages/Messages.dll");
                if (interactive)
                    Console.ReadLine();
                Environment.Exit(1);
            }
            if (interactive)
            {
                Console.WriteLine("Finished. Press enter.");
                Console.ReadLine();
            }
        }

        public static bool StdMsgsProcessed()
        {
            return required_packages.All(c => MsgFile.resolver.ContainsKey(c));
        }

        private static void MakeTempDir(string outputdir)
        {
            if (!Directory.Exists(outputdir))
                Directory.CreateDirectory(outputdir);
            else
            {
                foreach (string s in Directory.GetFiles(outputdir, "*.cs"))
                {
                    try
                    {
                        File.Delete(s);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                foreach (string s in Directory.GetDirectories(outputdir))
                {
                    if (s != "Properties")
                    {
                        try
                        {
                            Directory.Delete(s, true);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }
            }
            if (!Directory.Exists(outputdir))
                Directory.CreateDirectory(outputdir);
            else
            {
                foreach (string s in Directory.GetFiles(outputdir, "*.cs"))
                {
                    try
                    {
                        File.Delete(s);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                foreach (string s in Directory.GetDirectories(outputdir))
                {
                    if (s != "Properties")
                    {
                        try
                        {
                            Directory.Delete(s, true);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }
            }
        }

        private static void GenerateFiles(List<MsgFile> files, List<SrvFile> srvfiles, List<ActionFile> actionFiles, string outputdir)
        {
            List<MsgFile> mresolved = new List<MsgFile>();
            List<SrvFile> sresolved = new List<SrvFile>();
            List<ActionFile> actionFilesResolved = new List<ActionFile>();
            while (files.Except(mresolved).Any())
            {
                Debug.WriteLine("MSG: Running for " + files.Count + "/" + mresolved.Count + "\n" + files.Except(mresolved).Aggregate("\t", (o, n) => "" + o + "\n\t" + n.Name));
                foreach (MsgFile m in files.Except(mresolved))
                {
                    string md5 = null;
                    string typename = null;
                    md5 = MD5.Sum(m);
                    typename = m.Name;
                    if (md5 != null && !md5.StartsWith("$") && !md5.EndsWith("MYMD5SUM"))
                    {
                        mresolved.Add(m);
                    }
                    else
                    {
                        Debug.WriteLine("Waiting for children of " + typename + " to have sums");
                    }
                }
                if (files.Except(mresolved).Any())
                {
                    Debug.WriteLine("MSG: Rerunning sums for remaining " + files.Except(mresolved).Count() + " definitions");
                }
            }
            while (srvfiles.Except(sresolved).Any())
            {
                Debug.WriteLine("SRV: Running for " + srvfiles.Count + "/" + sresolved.Count + "\n" + srvfiles.Except(sresolved).Aggregate("\t", (o, n) => "" + o + "\n\t" + n.Name));
                foreach (SrvFile s in srvfiles.Except(sresolved))
                {
                    string md5 = null;
                    string typename = null;
                    s.Request.Stuff.ForEach(a => s.Request.resolve(a));
                    s.Response.Stuff.ForEach(a => s.Request.resolve(a));
                    md5 = MD5.Sum(s);
                    typename = s.Name;
                    if (md5 != null && !md5.StartsWith("$") && !md5.EndsWith("MYMD5SUM"))
                    {
                        sresolved.Add(s);
                    }
                    else
                    {
                        Debug.WriteLine("Waiting for children of " + typename + " to have sums");
                    }
                }
                if (srvfiles.Except(sresolved).Any())
                {
                    Debug.WriteLine("SRV: Rerunning sums for remaining " + srvfiles.Except(sresolved).Count() + " definitions");
                }
            }
            while (actionFiles.Except(actionFilesResolved).Any())
            {
                Debug.WriteLine("SRV: Running for " + actionFiles.Count + "/" + actionFilesResolved.Count + "\n" + actionFiles.Except(actionFilesResolved).Aggregate("\t", (o, n) => "" + o + "\n\t" + n.Name));
                foreach (ActionFile actionFile in actionFiles.Except(actionFilesResolved))
                {
                    string md5 = null;
                    string typename = null;
                    actionFile.GoalMessage.Stuff.ForEach(a => actionFile.GoalMessage.resolve(a));
                    actionFile.ResultMessage.Stuff.ForEach(a => actionFile.ResultMessage.resolve(a));
                    actionFile.FeedbackMessage.Stuff.ForEach(a => actionFile.FeedbackMessage.resolve(a));
                    md5 = MD5.Sum(actionFile);
                    typename = actionFile.Name;
                    if (md5 != null && !md5.StartsWith("$") && !md5.EndsWith("MYMD5SUM"))
                    {
                        actionFilesResolved.Add(actionFile);
                    }
                    else
                    {
                        Logger.LogDebug("Waiting for children of " + typename + " to have sums");
                    }
                }
                if (actionFiles.Except(actionFilesResolved).Any())
                {
                    Logger.LogDebug("ACTION: Rerunning sums for remaining " + actionFiles.Except(actionFilesResolved).Count() + " definitions");
                }
            }
            foreach (MsgFile file in files)
            {
                file.Write(outputdir);
            }
            foreach (SrvFile file in srvfiles)
            {
                file.Write(outputdir);
            }
            foreach (ActionFile actionFile in actionFiles)
            {
                actionFile.Write(outputdir);
            }
            File.WriteAllText(Path.Combine(outputdir, "MessageTypes.cs"), ToString().Replace("FauxMessages", "Messages"));
        }

        private static void GenerateProject(List<MsgFile> files, List<SrvFile> srvfiles, string projectName, string outputdir)
        {
            string[] lines = Templates.MessagesProj.Split('\n');
            string output = "";
            for (int i = 0; i < lines.Length; i++)
            {
                output += "" + lines[i] + "\n";
                /*if (lines[i].Contains("<Compile Include="))
                {
                    foreach (MsgsFile m in files)
                    {
                        output += "\t<Compile Include=\"" + m.Name.Replace('.', '\\') + ".cs\" />\n";
                    }
                    foreach (SrvsFile m in srvfiles)
                    {
                        output += "\t<Compile Include=\"" + m.Name.Replace('.', '\\') + ".cs\" />\n";
                    }
                    output += "\t<Compile Include=\"SerializationHelper.cs\" />\n";
                    output += "\t<Compile Include=\"Interfaces.cs\" />\n";
                    output += "\t<Compile Include=\"MessageTypes.cs\" />\n";
                }*/
            }
            File.WriteAllText(Path.Combine(outputdir, projectName + ".csproj"), output);
            File.WriteAllText(Path.Combine(outputdir, ".gitignore"), "*");
        }

        private static void BuildProject(string configuration, string projectName, string outputdir)
        {
            BuildProject("BUILDING GENERATED PROJECT WITH MSBUILD!", configuration, projectName, outputdir);
        }

        static Process RunDotNet(string args)
        {
            string fn = "dotnet";
            var proc = new Process();
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.FileName = fn;
            proc.StartInfo.Arguments = args;
            proc.Start();
            return proc;
        }

        private static void BuildProject(string spam, string configuration, string projectName, string outputdir)
        {
            Console.WriteLine("\n\n" + spam);

            string output, error;

            Console.WriteLine("Running .NET dependency restorer...");
            string restoreArgs = "restore \"" + Path.Combine(outputdir, projectName) + ".csproj\"";
            var proc = RunDotNet(restoreArgs);
            output = proc.StandardOutput.ReadToEnd();
            error = proc.StandardError.ReadToEnd();
            if (output.Length > 0)
                Console.WriteLine(output);
            if (error.Length > 0)
                Console.WriteLine(error);

            Console.WriteLine("Running .NET Builder...");
            string buildArgs = "build -f netcoreapp2.1 \"" + Path.Combine(outputdir, projectName) + ".csproj\" -c " + configuration;
            proc = RunDotNet(buildArgs);

            output = proc.StandardOutput.ReadToEnd();
            error = proc.StandardError.ReadToEnd();
            if (File.Exists(Path.Combine(outputdir, "bin", configuration, projectName + ".dll")))
            {
                Console.WriteLine("\n\nGenerated DLL has been copied to:\n\t" + Path.Combine(outputdir, projectName + ".dll") + "\n\n");
                File.Copy(Path.Combine(outputdir, "bin", configuration, projectName + ".dll"), Path.Combine(outputdir, projectName + ".dll"), true);
                Thread.Sleep(100);
            }
            else
            {
                if (output.Length > 0)
                    Console.WriteLine(output);
                if (error.Length > 0)
                    Console.WriteLine(error);
                Console.WriteLine("Build was not successful");
            }
        }

        private new static string ToString()
        {
            return "";
        }
    }
}