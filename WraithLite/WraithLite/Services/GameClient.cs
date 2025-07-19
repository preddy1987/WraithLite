// GameClient.cs
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WraithLite.Services
{
    public class GameClient
    {
        private StreamWriter _writer;

        public async Task<string> GetGameTokenAsync(string username, string password)
        {
            using var client = new TcpClient();
            await client.ConnectAsync("sge.play.net", 7900);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.ASCII);

            var loginString = $"A\t{username}\t{password}\tGS\t1\n";
            await writer.WriteAsync(loginString);

            var result = await reader.ReadLineAsync();
            if (result?.StartsWith("A\t") == true)
            {
                var parts = result.Split('\t');
                string gameHost = parts[4];
                int gamePort = int.Parse(parts[5]);
                string gameKey = parts[6];
                return $"{gameHost}:{gamePort}:{gameKey}";
            }

            throw new Exception("Failed to authenticate or retrieve game token.");
        }

        public async Task ConnectToGameAsync(string host, int port, string key, Action<string> onGameOutput)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port);
            using var stream = client.GetStream();
            _writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.ASCII);

            await _writer.WriteLineAsync(key);

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line != null)
                        onGameOutput(line);
                }
            });
        }

        public async Task SendCommandAsync(string command)
        {
            if (_writer != null)
            {
                await _writer.WriteLineAsync(command);
            }
        }
    }
}
