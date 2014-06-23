using System;
using System.IO;


namespace BtcChinaBot
{
    internal class Logger
    {
        private readonly StreamWriter _writer;

        internal Logger(string logFilePath)
        {
            var stream = new FileStream(logFilePath, FileMode.Create, FileAccess.Write);
            _writer = new StreamWriter(stream);
            _writer.WriteLine("BTCChina trading bot started at " + DateTime.Now);
            _writer.WriteLine("=============================================================================");
            _writer.Flush();
        }

        /// <summary>Append line to log file</summary>
        internal void AppendMessage(string message, bool console=true, ConsoleColor? color = null)
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
