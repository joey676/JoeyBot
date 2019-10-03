using RLBotDotNet;
using System.IO;
using System;

namespace RLBotCSharpExample
{
    class Program
    {
        static void Main()
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            // Read the port from port.cfg.
            const string file = "port.cfg";
            string text = File.ReadAllLines(file)[0];
            int port = int.Parse(text);

            // BotManager is a generic which takes in your bot as its T type.
            BotManager<JoeyBot.JoeyBot> botManager = new BotManager<JoeyBot.JoeyBot>(120);
            // Start the server on the port given in the port.cfg file.
            botManager.Start(port);
        }
    }
}
