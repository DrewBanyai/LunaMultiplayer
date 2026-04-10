using LmpCommon.Enums;
using System;

namespace LmpCommon
{
    public class BaseLogger
    {
        protected virtual LogLevels LogLevel => LogLevels.Debug;
        protected virtual bool UseUtcTime => false;

        public static bool SilenceConsole { get; set; }

        protected virtual void AfterPrint(string line)
        {
            //Implement your own after logging code
        }

        #region Private methods

        private void WriteLog(LogLevels level, string type, string message)
        {
            if (level <= LogLevel)
            {
                var output = UseUtcTime ? $"[{DateTime.UtcNow:HH:mm:ss}][{type}]: {message}" : $"[{DateTime.Now:HH:mm:ss}][{type}]: {message}";
                if (!SilenceConsole)
                    Console.WriteLine(output);
                AfterPrint(output);
            }
        }

        #endregion

        #region Public methods

        public void NetworkVerboseDebug(string message)
        {
            if (!SilenceConsole)
            {
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.ForegroundColor = ConsoleColor.Blue;
            }
            WriteLog(LogLevels.VerboseNetworkDebug, "VerboseNetwork", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void NetworkDebug(string message)
        {
            if (!SilenceConsole)
            {
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
            WriteLog(LogLevels.NetworkDebug, "NetworkDebug", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void Debug(string message)
        {
            if (!SilenceConsole)
                Console.ForegroundColor = ConsoleColor.Green;
            WriteLog(LogLevels.Debug, "Debug", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void Warning(string message)
        {
            if (!SilenceConsole)
                Console.ForegroundColor = ConsoleColor.Yellow;
            WriteLog(LogLevels.Normal, "Warning", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void Info(string message)
        {
            if (!SilenceConsole)
                Console.ForegroundColor = ConsoleColor.White;
            WriteLog(LogLevels.Normal, "Info", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void Normal(string message)
        {
            if (!SilenceConsole)
                Console.ForegroundColor = ConsoleColor.Gray;
            WriteLog(LogLevels.Normal, "LMP", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void Error(string message)
        {
            if (!SilenceConsole)
                Console.ForegroundColor = ConsoleColor.Red;
            WriteLog(LogLevels.Normal, "Error", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void Fatal(string message)
        {
            if (!SilenceConsole)
            {
                Console.BackgroundColor = ConsoleColor.Yellow;
                Console.ForegroundColor = ConsoleColor.Red;
            }
            WriteLog(LogLevels.Normal, "Fatal", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        public void ChatMessage(string message)
        {
            if (!SilenceConsole)
                Console.ForegroundColor = ConsoleColor.Cyan;
            WriteLog(LogLevels.Normal, "Chat", message);
            if (!SilenceConsole)
                Console.ResetColor();
        }

        #endregion
    }
}
