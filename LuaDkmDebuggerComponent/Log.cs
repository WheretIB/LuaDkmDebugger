using System;
using System.Diagnostics;
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

        public Log(LogLevel level)
        {
            logLevel = level;
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
                Output($"DEBUG: {text}");
        }

        public void Verbose(string text)
        {
            if (logLevel >= LogLevel.Verbose)
                Output($"INFO: {text}");
        }

        protected void Output(string text)
        {
            try
            {
                string formatted = $"{text} at {(DateTime.Now.Ticks / 10000.0) - startTime}ms";

                System.Diagnostics.Debug.WriteLine(formatted);

#if DEBUG
                if (logPath != null)
                {
                    using (var writer = File.AppendText(logPath))
                        writer.WriteLine(formatted);
                }
#endif
            }
            catch (Exception)
            {
            }
        }

        public LogLevel logLevel = LogLevel.Warning;
        public string logPath = null;
        public double startTime = DateTime.Now.Ticks / 10000.0;
    }
}
