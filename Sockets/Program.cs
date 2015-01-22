using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Sockets
{
    public static class WebSocketHelpers
    {
        private static string CreateWebsocketAcceptHeaderString(StreamReader reader)
        {
            const string magicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

            string line, key = "", responseKey = "";
            while (!string.IsNullOrEmpty(line = reader.ReadLine()))
            {
                if (line.StartsWith("Sec-WebSocket-Key:"))
                    key = line.Split(':')[1].Trim();
            }

            if (!string.IsNullOrEmpty(key))
            {
                key += magicString;
                using (var sha1 = SHA1.Create())
                {
                    responseKey = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key)));
                }
            }

            if (string.IsNullOrEmpty(responseKey)) throw new Exception();
            return responseKey;
        }

        public static void Handshake(Stream stream, string server, string port)
        {
            var writer = new StreamWriter(stream, Encoding.UTF8);
            var reader = new StreamReader(stream, Encoding.UTF8);

            writer.WriteLine("HTTP/1.1 101 Web Socket Protocol Handshake");
            writer.WriteLine("Upgrade: WebSocket");
            writer.WriteLine("Connection: Upgrade");
            writer.WriteLine("WebSocket-Origin: " + server);
            writer.WriteLine("WebSocket-Location: ws://" + server + ":" + port);
            writer.WriteLine("Sec-WebSocket-Accept: " + CreateWebsocketAcceptHeaderString(reader));
            writer.WriteLine("");
            writer.Flush();
        }

        public static void SendString(Stream stream, string s)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                const int frameSize = 64;

                var parts = bytes.Select((b, i) => new { b, i })
                    .GroupBy(x => x.i / (frameSize - 1))
                    .Select(x => x.Select(y => y.b).ToArray())
                    .ToList();

                for (int i = 0; i < parts.Count; i++)
                {
                    byte cmd = 0;
                    if (i == 0) cmd |= 1;
                    if (i == parts.Count - 1) cmd |= 0x80;

                    stream.WriteByte(cmd);
                    stream.WriteByte((byte)parts[i].Length);
                    stream.Write(parts[i], 0, parts[i].Length);
                }

                stream.Flush();
            }
            catch
            {
                stream.Close();
            }
        }

        public static string GetString(byte[] bytes)
        {
            byte data = bytes[1];
            byte length = (byte)(data & 127);
            int maskIndex = 2;
            if (length == 126) maskIndex = 4;
            else if (length == 127) maskIndex = 10;
            byte[] masks = new byte[4];

            for (int i = maskIndex; i < (maskIndex + 4); i++)
                masks[i - maskIndex] = bytes[i];

            int dataStart = maskIndex + 4;
            int messageLength = bytes.Length - dataStart;

            byte[] message = new byte[messageLength];
            for (int i = 0; i < messageLength; i++)
                message[i] = (byte)(bytes[dataStart + i] ^ masks[i % 4]);

            return Encoding.UTF8.GetString(message);
        }

        public delegate void ConnectionHandler(Stream stream, byte[] data);
        public static Thread CreateConnectionHandlerThread(TcpListener listener, ConnectionHandler handler, byte[] buffer = null)
        {
            var addressSplit = listener.LocalEndpoint.ToString().Split(':');

            var thread = new Thread(() =>
            {
                TcpClient client = listener.AcceptTcpClient();
                NetworkStream stream = client.GetStream();
                Handshake(stream, addressSplit[0], addressSplit[1]);

                while (true)
                {
                    try
                    {
                        while (!stream.DataAvailable) {}
                    }
                    catch (ObjectDisposedException)
                    {
                        Thread.CurrentThread.Abort();
                    }

                    var bytes = buffer ?? new byte[client.Available];
                    stream.Read(bytes, 0, bytes.Length);
                    handler(stream, bytes);
                }
            });

            thread.Start();
            return thread;
        }
    }

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
