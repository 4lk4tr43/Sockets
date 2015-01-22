using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Sockets
{
    class Program
    {
        static void Main(string[] args)
        {
            List<Thread> connections = new List<Thread>();
            
            TcpListener server = new TcpListener(IPAddress.Parse("127.0.0.1"), 1234);
            server.Start();
            connections.Add(WebSocketHelpers.CreateConnectionHandlerThread(server, (s, b) =>
            {
                Console.WriteLine(WebSocketHelpers.GetString(b));
                WebSocketHelpers.SendString(s, "Server Responded");
            }));

            Console.WriteLine("Server started...");
            System.Diagnostics.Process.Start("index.html");

            Console.WriteLine("Close Server with any key");
            Console.ReadKey();
            foreach (Thread thread in connections) thread.Abort();
        }
    }
}
