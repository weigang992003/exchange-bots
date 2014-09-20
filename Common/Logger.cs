using System;
using System.IO;


namespace Common
{
    public class Logger
    {
        private readonly StreamWriter _writer;

        public Logger(string logFilePath)
        {
            var stream = new FileStream(logFilePath, FileMode.Create, FileAccess.Write);
            _writer = new StreamWriter(stream);
            _writer.WriteLine("Trading bot log initialized at " + DateTime.Now);
            _writer.WriteLine("=============================================================================");
            _writer.Flush();
        }

        /// <summary>Contains last response data from server, if API manager recorded them</summary>
        public string LastResponse { get; set; }

        /// <summary>Append line to log file</summary>
        public void AppendMessage(string message, bool console=true, ConsoleColor? color = null)
        {
            _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " ### " +  message);
            _writer.Flush();

            if (console)
            {
                if (null != color)
                    Console.ForegroundColor = color.Value;
                Console.WriteLine(message);
                if (null != color)
                    Console.ResetColor();
            }
        }
    }
}
