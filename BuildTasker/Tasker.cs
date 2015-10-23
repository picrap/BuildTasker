// BuildTasker: a small library to create build tasks

namespace BuildTasker
{
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Logging;
    using Microsoft.Build.Utilities;

    public abstract class Tasker<TImplementation> : Task
        where TImplementation : Tasker<TImplementation>, new()
    {
        /// <summary>
        /// Gets the logging.
        /// </summary>
        /// <value>
        /// The logging.
        /// </value>
        public ILogging Logging { get; private set; }

        /// <summary>
        /// Gets the instance.
        /// </summary>
        /// <value>
        /// The instance.
        /// </value>
        public static TImplementation Instance { get; } = new TImplementation();

        /// <summary>
        /// Gets the wrapped task path.
        /// This is used when debugging inline task.
        /// The tast is named "*.task", so we call "*"
        /// </summary>
        /// <returns></returns>
        private string GetWrappedTaskPath()
        {
            var thisPath = GetType().Assembly.Location;
            var wrappedTaskPath = Path.Combine(Path.GetDirectoryName(thisPath), Path.GetFileNameWithoutExtension(thisPath));
            if (File.Exists(wrappedTaskPath))
                return wrappedTaskPath;
            return null;
        }

        /// <summary>
        /// Target task entry point
        /// </summary>
        /// <returns>
        /// true for success
        /// </returns>
        public override bool Execute()
        {
            var wrappedTaskPath = GetWrappedTaskPath();
            Logging = new TaskLogging(this);
            // see if the task is just a stub, which is the case if we have a wrapped task
            // (this allows to build and debug)
            if (wrappedTaskPath == null)
            {
                Run();
            }
            else
            {
                // run the application as a command-line application
                var process = new Process
                {
                    StartInfo =
                    {
                        FileName = wrappedTaskPath,
                        Arguments = CreateArguments(),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardInput = true,
                    }
                };
                process.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                        Logging.Write(e.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();
            }
            return true;
        }

        /// <summary>
        /// Creates the arguments from property values, in order to send them to sub process.
        /// </summary>
        /// <returns></returns>
        private string CreateArguments()
        {
            var arguments = new StringBuilder();
            foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => (p.GetMethod?.IsPublic ?? false) && (p.SetMethod?.IsPublic ?? false)))
            {
                arguments.AppendFormat(" \"{0}={1}\"", property.Name, property.GetValue(this));
            }
            return arguments.ToString();
        }

        /// <summary>
        /// This method has to be invoked from application Main.
        /// </summary>
        /// <param name="args">The arguments.</param>
        protected void Run(string[] args)
        {
            Logging = new ConsoleLogging();
            // arguments
            foreach (var arg in args)
            {
                var trimmedArg = arg.StartsWith("\"") && arg.EndsWith("\"") ? arg.Substring(1, arg.Length - 2) : arg;
                var equalIndex = trimmedArg.IndexOf('=');
                if (equalIndex < 0)
                    continue;
                var propertyName = trimmedArg.Substring(0, equalIndex);
                var propertyValue = trimmedArg.Substring(equalIndex + 1);
                var property = GetType().GetProperty(propertyName);
                if (property == null)
                    continue;
                property.SetValue(this, propertyValue);
            }
            // and now, run
            Run();
        }

        /// <summary>
        /// User entry point.
        /// </summary>
        public abstract void Run();
    }
}
