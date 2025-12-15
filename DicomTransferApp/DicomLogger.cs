
using System;
using System.IO;
using System.Text;

namespace DicomTransferApp
{
    public static class DicomLogger
    {
        private static readonly string LogDirectory;
        private static readonly string LogFilePath;

        static DicomLogger()
        {
            // Create a logs directory in the application's base directory
            LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(LogDirectory);

            // Create a log file with current date
            LogFilePath = Path.Combine(LogDirectory, $"DicomTransfer_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        public static void LogError(string message, Exception ex = null)
        {
            try
            {
                var logMessage = new StringBuilder();
                logMessage.AppendLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logMessage.AppendLine($"Message: {message}");

                if (ex != null)
                {
                    logMessage.AppendLine($"Exception Type: {ex.GetType().Name}");
                    logMessage.AppendLine($"Exception Message: {ex.Message}");
                    logMessage.AppendLine($"Stack Trace: {ex.StackTrace}");
                }

                logMessage.AppendLine(new string('-', 50));

                File.AppendAllText(LogFilePath, logMessage.ToString());
            }
            catch (Exception logEx)
            {
                Console.WriteLine($"Logging failed: {logEx.Message}");
                Console.WriteLine($"Original error: {message}");
            }
        }

        public static void LogInformation(string message)
        {
            try
            {
                var logMessage = new StringBuilder();
                logMessage.AppendLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logMessage.AppendLine($"Message: {message}");
                logMessage.AppendLine(new string('-', 50));

                File.AppendAllText(LogFilePath, logMessage.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logging failed: {ex.Message}");
                Console.WriteLine($"Original message: {message}");
            }
        }

        public static string GetCurrentLogFilePath()
        {
            return LogFilePath;
        }
    }
}
