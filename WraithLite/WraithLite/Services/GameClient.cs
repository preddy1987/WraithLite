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
            await client.ConnectAsync("eaccess.play.net", 7900);

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            // Step 1: Request the challenge key
            Console.WriteLine("Sending K to request key...");
            await writer.WriteAsync("K\n");
            string key = await reader.ReadLineAsync();
            Console.WriteLine($"Received key: {key}");

            if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
                throw new Exception("Invalid challenge key from SGE.");

            // Step 2: Hash the password
            string hashedPassword = HashPassword(password, key);

            // Step 3: Format login request
            string loginRequest = $"A\t{username}\t{hashedPassword}\tGS\t1\n";
            Console.WriteLine($"Sending login: {loginRequest.Replace(password, "******")}");

            await writer.WriteAsync(loginRequest);

            // Step 4: Read and handle response
            string response = await reader.ReadLineAsync();
            Console.WriteLine($"SGE Response: {response}");

            if (response.StartsWith("A\t"))
            {
                var parts = response.Split('\t');
                if (parts.Length >= 7)
                {
                    var host = parts[4];
                    var port = parts[5];
                    var gameKey = parts[6];
                    return $"{host}:{port}:{gameKey}";
                }
                throw new Exception("Malformed success response from SGE.");
            }
            else if (response.StartsWith("E\t"))
            {
                throw new Exception($"Login failed: {response}");
            }
            else
            {
                throw new Exception($"Unexpected response from SGE: {response}");
            }
        }

        // Password hashing function per Simutronics spec
        private string HashPassword(string password, string key)
        {
            var hash = new StringBuilder();

            for (int i = 0; i < password.Length && i < key.Length; i++)
            {
                byte p = (byte)(password[i] - 32);
                byte k = (byte)key[i];
                byte h = (byte)((p ^ k) + 32);
                hash.Append((char)h);
            }

            return hash.ToString();
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
