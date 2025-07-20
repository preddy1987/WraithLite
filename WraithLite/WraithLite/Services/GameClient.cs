// GameClient.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WraithLite.Services
{
    public class GameClient
    {
        private StreamWriter _writer;

        public async Task<(string host, int port, string sessionKey)> FullSgeLoginAsync(string username, string password)
        {
            Debug.WriteLine(">>> [SGE] Starting full SGE handshake");

            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            // Step 1: Challenge
            await writer.WriteAsync("K\n");
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] Received challenge: [{challenge}]");

            // Step 2: Password Hash
            var hash = HashPassword(password, challenge);
            var authCmd = $"A\t{username}\t{hash}\n";
            Debug.WriteLine($">>> [SGE] Sending AUTH");
            await writer.WriteAsync(authCmd);
            var authResp = await reader.ReadLineAsync();
            if (!authResp.StartsWith("A\t"))
                throw new Exception($"SGE auth failed: {authResp}");

            // Step 3: List Products
            await writer.WriteAsync("M\n");
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("M\t"))
                    break;
            }

            // Step 4: Parse game list, get GemStone IV code
            string gemstoneCode = null;
            var parts = line.Split('\t');
            for (int i = 1; i < parts.Length - 1; i += 2)
            {
                if (parts[i + 1].Equals("GemStone IV", StringComparison.OrdinalIgnoreCase))
                {
                    gemstoneCode = parts[i];
                    break;
                }
            }

            if (string.IsNullOrEmpty(gemstoneCode))
                throw new Exception("GemStone IV not found in SGE game list.");

            // Step 5: Select game by code
            await writer.WriteAsync($"N {gemstoneCode}\n");
            var nResp = await reader.ReadLineAsync();
            if (!nResp.StartsWith("N\t"))
                throw new Exception($"Game select failed: {nResp}");

            // Step 6: Validate subscription/product
            foreach (var cmd in new[] { "F", "G", "P" })
            {
                await writer.WriteAsync($"{cmd} {gemstoneCode}\n");
                var resp = await reader.ReadLineAsync();
                if (!resp.StartsWith($"{cmd}\t") && !resp.StartsWith("P\t")) // P is special
                    throw new Exception($"{cmd} check failed: {resp}");
            }

            // Step 7: Get character list
            await writer.WriteAsync("C\n");
            var cHeader = await reader.ReadLineAsync();
            var chars = new List<string>();
            while ((line = await reader.ReadLineAsync()) != null && line.StartsWith("\t"))
                chars.Add(line.Trim());

            if (chars.Count == 0)
                throw new Exception("No characters found");

            var charId = chars[0].Split('\t')[0];

            // Step 8: Login character
            await writer.WriteAsync($"L {charId} STORM\n");
            var loginResp = await reader.ReadLineAsync();
            var tokens = loginResp.Split('\t');

            if (tokens.Length < 6)
                throw new Exception($"Bad login response: {loginResp}");

            var host = tokens[3];
            var port = int.Parse(tokens[4]);
            var key = tokens[5];

            Debug.WriteLine($">>> [SGE] Login successful: {host}:{port} key={key}");
            return (host, port, key);
        }

        public async Task<string> GetGameTokenAsync(string username, string password)
        {
            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };

            // Step 1: Request the challenge key
            Debug.WriteLine("Sending K to request key...");
            await writer.WriteAsync("K\n");
            await writer.FlushAsync(); // ensure it actually sends
            string key = await reader.ReadLineAsync();
            Debug.WriteLine($"Received key: {key}");

            if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
                throw new Exception("Invalid challenge key from SGE.");

            // Step 2: Hash the password
            string hashedPassword = HashPassword(password, key);

            // Step 3: Format login request
            string loginRequest = $"A\t{username}\t{hashedPassword}\tGS\t1\n";
            var loginBytes = Encoding.ASCII.GetBytes(loginRequest);
            Debug.WriteLine("Login bytes: " + BitConverter.ToString(loginBytes));
            Debug.WriteLine($"LOGIN STRING: [{string.Join(",", loginRequest.Select(c => (int)c))}]");
            Debug.WriteLine($"Sending login: {loginRequest.Replace(password, "******")}");

            await writer.WriteAsync(loginRequest);
            await writer.FlushAsync();

            // Step 4: Read and handle response
            string response = await reader.ReadLineAsync();
            Debug.WriteLine($"SGE Response: {response}");

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
        public static string HashPassword(string password, string challenge)
        {
            var xor = new char[password.Length];
            for (int i = 0; i < password.Length; i++)
                xor[i] = (char)(password[i] ^ challenge[i % challenge.Length]);

            var hash = Convert.ToBase64String(Encoding.ASCII.GetBytes(xor));
            return hash.Replace('\n', ' ').Trim(); // sanitize just in case
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
