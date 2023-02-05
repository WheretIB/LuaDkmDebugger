using System;
using System.IO;

namespace LuaDkmDebuggerComponent
{
    public class Log
    {
        public enum LogLevel
        {
            None,
            Error,
            Warning,
            Debug,
            Verbose
        };

        public Log(LogLevel level, bool isGlobal)
        {
            logLevel = level;

            if (isGlobal)
                instance = this;
        }

        public void Error(string text)
        {
            if (logLevel >= LogLevel.Error)
                Output($"ERROR: {text}");
        }

        public void Warning(string text)
        {
            if (logLevel >= LogLevel.Warning)
                Output($"WARNING: {text}");
        }

        public void Debug(string text)
        {
            if (logLevel >= LogLevel.Debug)
                Output($"INFO: {text}");
        }

        public void Verbose(string text)
        {
            if (logLevel >= LogLevel.Verbose)
                Output($"VERBOSE: {text}");
        }

        protected void Output(string text)
        {
            try
            {
                string formatted = $"[{(DateTime.Now.Ticks / 10000.0) - startTime}] {text}";

                System.Diagnostics.Debug.WriteLine(formatted);

                if (logPath != null)
                {
                    using (var writer = File.AppendText(logPath))
                        writer.WriteLine(formatted);
                }
            }
            catch (Exception)
            {
            }
        }

        public static Log instance = null;
        public LogLevel logLevel = LogLevel.Warning;
        public string logPath = null;
        public double startTime = DateTime.Now.Ticks / 10000.0;
    }
}
