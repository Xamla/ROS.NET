using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

#if NETCORE
using System.Runtime.Loader;
#endif
using Microsoft.Extensions.Logging;

using Uml.Robotics.XmlRpc;
using std_msgs = Messages.std_msgs;
using System.IO;
using System.Threading.Tasks;
using Xamla.Robotics.Ros.Async;

namespace Uml.Robotics.Ros
{
    /// <summary>
    /// A static class for global variables, initializations and shutdowns.
    /// </summary>
    public static class ROS
    {
        private static ILogger logger = ApplicationLogging.CreateLogger(nameof(ROS));

        private static ICallbackQueue globalCallbackQueue;
        private static readonly object startMutex = new object();

        public static TimerManager timerManager = new TimerManager();

        private static Task shutdownTask;
        private static bool started;
        private static bool atExitRegistered;
        private static InitOptions initOptions;
        private static volatile bool _ok;
        internal static bool shuttingDown;
        internal static bool initialized;

        public static ICallbackQueue GlobalCallbackQueue =>
            globalCallbackQueue;

        /// <summary>
        ///     Means of setting ROS_MASTER_URI programatically before Init is called
        ///     Order of precedence: __master:=... > this variable > User Environment Variable > System Environment Variable
        /// </summary>
        public static string ROS_MASTER_URI { get; set; }

        /// <summary>
        ///     Means of setting ROS_HOSTNAME directly before Init is called
        ///     Order of precedence: __hostname:=... > this variable > User Environment Variable > System Environment Variable
        /// </summary>
        public static string ROS_HOSTNAME { get; set; }

        /// <summary>
        ///     Means of setting ROS_IP directly before Init is called
        ///     Order of precedence: __ip:=... > this variable > User Environment Variable > System Environment Variable
        /// </summary>
        public static string ROS_IP { get; set; }


        /// <summary>
        /// General global sleep time in miliseconds for spin operations.
        /// </summary>
        public const int WallDuration = 10;

        public static NodeHandle GlobalNodeHandle;
        private static object shuttingDownMutex = new object();

        private static TimeSpan lastSimTime;                // last sim time time
        private static TimeSpan lastSimTimeReceived;        // last sim time received time (wall)

        private const string ROSOUT_FMAT = "{0} {1}";
        private const string ROSOUT_DEBUG_PREFIX = "[Debug]";
        private const string ROSOUT_INFO_PREFIX  = "[Info ]";
        private const string ROSOUT_WARN_PREFIX  = "[Warn ]";
        private const string ROSOUT_ERROR_PREFIX = "[Error]";
        private const string ROSOUT_FATAL_PREFIX = "[FATAL]";

        private static readonly Dictionary<RosOutAppender.ROSOUT_LEVEL, string> ROSOUT_PREFIX =
            new Dictionary<RosOutAppender.ROSOUT_LEVEL, string> {
                { RosOutAppender.ROSOUT_LEVEL.DEBUG, ROSOUT_DEBUG_PREFIX },
                { RosOutAppender.ROSOUT_LEVEL.INFO, ROSOUT_INFO_PREFIX },
                { RosOutAppender.ROSOUT_LEVEL.WARN, ROSOUT_WARN_PREFIX },
                { RosOutAppender.ROSOUT_LEVEL.ERROR, ROSOUT_ERROR_PREFIX },
                { RosOutAppender.ROSOUT_LEVEL.FATAL, ROSOUT_FATAL_PREFIX }
            };

        public static bool ShuttingDown
        {
            get { return shuttingDown; }
        }

        /// <summary>
        ///     True if ROS is ok, false if not
        /// </summary>
        public static bool OK
        {
            get { return _ok; }
        }

        /// <summary>
        /// Convert a DateTime struct into a std_msgs/Time message
        /// </summary>
        /// <param name="time"> DateTime to convert </param>
        /// <returns> containing secs, nanosecs since 1/1/1970 </returns>
        public static std_msgs.Time ToTimeMessage(this DateTime time)
        {
            return GetTime<std_msgs.Time>(time.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)));
        }

        /// <summary>
        /// Convert a std_msgs/Time mesage into a DateTime struct
        /// </summary>
        /// <param name="time"> std_msgs.Time to convert </param>
        /// <returns> a DateTime </returns>
        public static DateTime ToDateTime(this std_msgs.Time time)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0).Add(new TimeSpan(time.data.Ticks));
        }

        /// <summary>
        /// Convert std_msgs/Duration into TimeSpan struct
        /// </summary>
        /// <param name="time"> std_msgs.Duration to convert </param>
        /// <returns> a TimeSpan </returns>
        public static TimeSpan ToTimeSpan(this std_msgs.Duration duration)
        {
            return new TimeSpan(duration.data.Ticks);
        }

        public static T GetTime<T>(TimeSpan ts) where T : RosMessage, new()
        {
            T test = Activator.CreateInstance(typeof(T), GetTime(ts)) as T;
            return test;
        }

        /// <summary>
        /// Convert TimeSpan to TimeData
        /// </summary>
        /// <param name="timestamp"> The timespan to convert to seconds/nanoseconds </param>
        /// <returns> a time struct </returns>
        public static TimeData GetTime(TimeSpan timestamp)
        {
            if (lastSimTimeReceived != default(TimeSpan))
            {
                timestamp = timestamp.Subtract(lastSimTimeReceived).Add(lastSimTime);
            }
            return TimeData.FromTicks(timestamp.Ticks);
        }

        /// <summary>
        /// Gets the current time as std_msgs/Time
        /// </summary>
        /// <returns> </returns>
        public static std_msgs.Time GetTime()
        {
            return ToTimeMessage(DateTime.UtcNow);
        }

        private static void SimTimeCallback(TimeSpan ts)
        {
            lastSimTime = ts;
            lastSimTimeReceived = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0));
        }

        /// <summary>
        /// Non-creatable marker class
        /// </summary>
        public class ONLY_AUTO_PARAMS
        {
            private ONLY_AUTO_PARAMS() {}
        }

        public delegate void WriteDelegate(object format, params object[] args);

        /// <summary>
        ///     ROS_INFO(...)
        /// </summary>
        public static WriteDelegate Info(ONLY_AUTO_PARAMS CAPTURE_CALL_SITE = null, [CallerMemberName] string memberName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            return (format, args) => _rosout(format, args, RosOutAppender.ROSOUT_LEVEL.INFO, new CallerInfo { MemberName = memberName, FilePath = filePath, LineNumber = lineNumber });
        }

        /// <summary>
        ///     ROS_DEBUG(...) (formatted)
        /// </summary>
        public static WriteDelegate Debug(ONLY_AUTO_PARAMS CAPTURE_CALL_SITE = null, [CallerMemberName] string memberName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            return (format, args) => _rosout(format, args, RosOutAppender.ROSOUT_LEVEL.DEBUG, new CallerInfo { MemberName = memberName, FilePath = filePath, LineNumber = lineNumber });
        }

        /// <summary>
        ///     ROS_INFO(...) (formatted)
        /// </summary>
        public static WriteDelegate Error(ONLY_AUTO_PARAMS CAPTURE_CALL_SITE = null, [CallerMemberName] string memberName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            return (format, args) => _rosout(format, args, RosOutAppender.ROSOUT_LEVEL.ERROR, new CallerInfo { MemberName = memberName, FilePath = filePath, LineNumber = lineNumber });
        }

        /// <summary>
        ///     ROS_WARN(...) (formatted)
        /// </summary>
        public static WriteDelegate Warn(ONLY_AUTO_PARAMS CAPTURE_CALL_SITE = null, [CallerMemberName] string memberName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            return (format, args) => _rosout(format, args, RosOutAppender.ROSOUT_LEVEL.WARN, new CallerInfo { MemberName = memberName, FilePath = filePath, LineNumber = lineNumber });
        }

        private static void _rosout(object format, object[] args, RosOutAppender.ROSOUT_LEVEL level, CallerInfo callerInfo)
        {
            using (logger.BeginScope(nameof(_rosout)))
            {
                if (format == null)
                    throw new ArgumentNullException(nameof(format));

                string text = (args == null || args.Length == 0) ? format.ToString() : string.Format((string)format, args);

                bool printit = true;
                if (level == RosOutAppender.ROSOUT_LEVEL.DEBUG)
                {
    #if !DEBUG
                    printit = false;
    #endif
                }
                if (printit)
                    logger.LogDebug(ROSOUT_FMAT, ROSOUT_PREFIX[level], text);
                RosOutAppender.Instance.Append(text, level, callerInfo);
            }
        }

        /// <summary>
        /// Set the logging factory for ROS.NET
        /// </summary>
        /// <param name="factory"> The logging factory to use for logging </param>
        public static void SetLoggerFactory(ILoggerFactory factory)
        {
            ApplicationLogging.LoggerFactory = factory;

            // recreate logger to make sure the new log settings form the factory are used
            logger = ApplicationLogging.CreateLogger(nameof(ROS));
            if (initialized)
            {
                logger.LogWarning("Logging should be configured before initializing the ROS system.");
            }
        }

        /// <summary>
        ///  Initializes ROS
        /// </summary>
        /// <param name="args"> argv - parsed for remapping args (AND PARAMS??) </param>
        /// <param name="name"> the node's name </param>
        public static void Init(string[] args, string name)
        {
            Init(args, name, 0);
        }

        /// <summary>
        /// Initializes ROS
        /// </summary>
        /// <param name="args"> argv - parsed for remapping args (AND PARAMS??) </param>
        /// <param name="name"> the node's name </param>
        /// <param name="options"> options? </param>
        public static void Init(string[] args, string name, InitOptions options)
        {
            // ROS_MASTER_URI/ROS_HOSTNAME definition precedence:
            // 1. explicitely set by program
            // 2. passed in as remap argument
            // 3. environment variable

            if (RemappingHelper.GetRemappings(ref args, out IDictionary<string, string> remapping))
                Init(remapping, name, options);
            else
                throw new InvalidOperationException("Could not initialize ROS");
        }

        /// <summary>
        /// Initializes ROS
        /// </summary>
        /// <param name="remappingArgs"> dictionary of remapping args </param>
        /// <param name="name"> node name </param>
        /// <param name="options"> options </param>
        public static void Init(IDictionary<string, string> remappingArgs, string name, InitOptions options = 0)
        {
            lock (typeof(ROS))
            {
                // register process unload and cancel (CTRL+C) event handlers
                if (!atExitRegistered)
                {
                    atExitRegistered = true;
#if NETCORE
                    AssemblyLoadContext.Default.Unloading += (AssemblyLoadContext obj) =>
                    {
                        Shutdown();
                        WaitForShutdown();
                    };
#else
                    Process.GetCurrentProcess().EnableRaisingEvents = true;
                    Process.GetCurrentProcess().Exited += (o, args) =>
                    {
                        Shutdown();
                        WaitForShutdown();
                    };
#endif

                    Console.CancelKeyPress += (o, args) =>
                    {
                        Shutdown();
                        WaitForShutdown();
                        args.Cancel = true;
                    };
                }

                // crate global callback queue
                if (globalCallbackQueue == null)
                {
                    globalCallbackQueue = new CallbackQueue();
                }

                // run the actual ROS initialization
                if (!initialized)
                {
                    MessageTypeRegistry.Default.Reset();
                    ServiceTypeRegistry.Default.Reset();
                    var msgRegistry = MessageTypeRegistry.Default;
                    var srvRegistry = ServiceTypeRegistry.Default;

                    // Load RosMessages from MessageBase assembly
                    msgRegistry.ParseAssemblyAndRegisterRosMessages(typeof(RosMessage).GetTypeInfo().Assembly);

                    // Load RosMessages from all assemblies that reference Uml.Robotics.Ros.MessageBas
#if NETCORE
                    // Load RosMessages from all assemblies that depend on MessageBase
                    var candidates = MessageTypeRegistry.GetCandidateAssemblies("Uml.Robotics.Ros.MessageBase");
                    foreach (var assembly in candidates)
                    {
                        logger.LogDebug($"Parse assembly: {assembly.Location}");
                        msgRegistry.ParseAssemblyAndRegisterRosMessages(assembly);
                        srvRegistry.ParseAssemblyAndRegisterRosServices(assembly);
                    }
#else
                    // Load RosMessages from Messages assembly
                    var msgAssembly = Assembly.LoadFrom("Uml.Robotics.Ros.dll");

                    logger.LogDebug($"Parse assembly: {msgAssembly.Location}");
                    msgRegistry.ParseAssemblyAndRegisterRosMessages(msgAssembly);
                    srvRegistry.ParseAssemblyAndRegisterRosServices(msgAssembly);

#endif

                    initOptions = options;
                    _ok = true;

                    Param.Reset();
                    SimTime.Reset();
                    RosOutAppender.Reset();

                    Network.Init(remappingArgs);
                    Master.Init(remappingArgs);
                    ThisNode.Init(name, remappingArgs, options);
                    Param.Init(remappingArgs);
                    SimTime.Instance.SimTimeEvent += SimTimeCallback;

                    lock (shuttingDownMutex)
                    {
                        switch(shutdownTask?.Status)
                        {
                            case null:
                            case TaskStatus.RanToCompletion:
                                break;
                            default:
                                throw new InvalidOperationException("ROS was not shut down correctly");
                        }
                        shutdownTask = new Task(_shutdown);
                    }
                    initialized = true;

                    GlobalNodeHandle = new NodeHandle(ThisNode.Namespace, remappingArgs);
                    RosOutAppender.Instance.Start();
                }
            }
        }

        /// <summary>
        ///     This is called when rosnode kill is invoked
        /// </summary>
        /// <param name="p"> pointer to unmanaged XmlRpcValue containing params </param>
        /// <param name="r"> pointer to unmanaged XmlRpcValue that will contain return value </param>
        private static void ShutdownCallback(XmlRpcValue parms, XmlRpcValue r)
        {
            int num_params = 0;
            if (parms.Type == XmlRpcType.Array)
                num_params = parms.Count;
            if (num_params > 1)
            {
                string reason = parms[1].GetString();
                logger.LogInformation("Shutdown request received.");
                logger.LogInformation("Reason given for shutdown: [" + reason + "]");
                Shutdown();
            }
            XmlRpcManager.ResponseInt(1, "", 0)(r);
        }

        /// <summary>
        ///     Hang the current thread until ROS shuts down
        /// </summary>
        public static void WaitForShutdown()
        {
            if (shutdownTask != null)
            {
                shutdownTask.Wait();
            }
        }

        public static event EventHandler RosStarting;
        public static event EventHandler RosStarted;
        public static event EventHandler RosShuttingDown;
        public static event EventHandler RosShutDown;

        private static void RaiseRosStarting()
        {
            try
            {
                RosStarting?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
            }
        }

        private static void RaiseRosStarted()
        {
            try
            {
                RosStarted?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
            }
        }

        private static void RaiseRosShuttingDown()
        {
            try
            {
                RosShuttingDown?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
            }
        }

        private static void RaiseRosShutDown()
        {
            try
            {
                RosShutDown?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
            }
        }

        /// <summary>
        ///     Finishes intialization This is called by the first NodeHandle when it initializes
        /// </summary>
        internal static void Start()
        {
            lock (startMutex)
            {
                if (started)
                    return;

                RaiseRosStarting();

                ServiceManager.Reset();
                XmlRpcManager.Reset();
                TopicManager.Reset();
                ConnectionManager.Reset();

                XmlRpcManager.Instance.Bind("shutdown", ShutdownCallback);
                TopicManager.Instance.Start();
                ServiceManager.Instance.Start();
                ConnectionManager.Instance.Start();
                XmlRpcManager.Instance.Start();

                shuttingDown = false;
                started = true;
                _ok = true;

                RaiseRosStarted();
            }
        }

        /// <summary>
        /// Check whether ROS.init() has been called.
        /// </summary>
        /// <returns>System.Boolean indicating whether ROS has been started.</returns>
        public static bool IsStarted()
        {
            lock (startMutex)
            {
                return started;
            }
        }

        /// <summary>
        /// Initiate a ROS shutdown
        /// </summary>
        public static Task Shutdown()
        {
            lock (shuttingDownMutex)
            {
                if (shutdownTask != null && shutdownTask.Status == TaskStatus.Created)
                {
                    shuttingDown = true;
                    _ok = false;
                    shutdownTask.Start();
                }
            }

            return shutdownTask;
        }

        /// <summary>
        /// Internal ROS deinitialization method. Called by checkForShutdown.
        /// </summary>
        private static void _shutdown()
        {
            if (started)
            {
                logger.LogInformation("ROS is shutting down.");
                RaiseRosShuttingDown();

                SimTime.Terminate();
                RosOutAppender.Terminate();
                GlobalNodeHandle.Shutdown().Wait();
                GlobalCallbackQueue.Disable();
                GlobalCallbackQueue.Clear();

                XmlRpcManager.Instance.Unbind("shutdown");
                Param.Terminate();

                TopicManager.Terminate();
                ServiceManager.Terminate();
                XmlRpcManager.Terminate();
                ConnectionManager.Terminate();

                lock (startMutex)
                {
                    started = false;
                    ResetStaticMembers();
                }

                RaiseRosShutDown();
            }
        }

        private static void ResetStaticMembers()
        {
            globalCallbackQueue = null;
            initialized = false;
            _ok = false;
            timerManager = new TimerManager();
            started = false;
            _ok = false;
            shuttingDown = false;
        }

    }


    /// <summary>
    /// Options that can be passed to the ROS.init() function.
    /// </summary>
    [Flags]
    public enum InitOptions
    {
        None = 0,
        NosigintHandler = 1 << 0,
        AnonymousName = 1 << 1,
        NoRousout = 1 << 2
    }
}
