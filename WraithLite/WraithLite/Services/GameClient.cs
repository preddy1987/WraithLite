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

        public async Task<(string host, int port, string sessionKey)> FullSgeLoginAsync(
            string username, string password)
        {
            Debug.WriteLine(">>> [SGE] Starting full SGE handshake");

            using var client = new TcpClient();
            Debug.WriteLine(">>> [SGE] Connecting to eaccess.play.net:7900");
            await client.ConnectAsync("eaccess.play.net", 7900);

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            // 1) Challenge
            Debug.WriteLine(">>> [SGE] Sending: K\\n");
            await writer.WriteAsync("K\n");
            await writer.FlushAsync();
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] Received challenge: [{challenge}]");

            // 2) Account auth
            var hash = HashPassword(password, challenge);
            Debug.WriteLine($">>> [SGE] Computed hash: [{hash}]");
            var authCmd = $"A\t{username}\t{hash}\n";
            Debug.WriteLine($">>> [SGE] Sending AUTH: [{authCmd.Replace(hash, "****")}]");
            await writer.WriteAsync(authCmd);
            await writer.FlushAsync();
            var aResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] AUTH response: [{aResp}]");
            if (!aResp.StartsWith("A\t"))
                throw new Exception($"Account auth failed: {aResp}");

            // 3) List products
            Debug.WriteLine(">>> [SGE] Sending: M\\n (list products)");
            await writer.WriteAsync("M\n");
            await writer.FlushAsync();

            // read until header “M\t…”
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                Debug.WriteLine($">>> [SGE] M-line: [{line}]");
                if (line.StartsWith("M\t")) break;
            }

            // 4) Determine the numeric index of “GemStone IV”
            // parts: [ "M", code1, desc1, code2, desc2, … ]
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            int instanceIndex = -1;
            for (int idx = 1, count = 1; idx < parts.Length; idx += 2, ++count)
            {
                var desc = parts[idx + 1];
                if (desc.Equals("GemStone IV", StringComparison.OrdinalIgnoreCase))
                {
                    instanceIndex = count;
                    break;
                }
            }
            if (instanceIndex < 1)
                instanceIndex = 6; // fallback to the usual Prime slot

            Debug.WriteLine($">>> [SGE] Sending: N {instanceIndex}\\n (select slot #{instanceIndex})");
            await writer.WriteAsync($"N {instanceIndex}\n");
            await writer.FlushAsync();
            var nResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] N response: [{nResp}]");

            // 5) Confirm subscription/payment (F, G, P)
            foreach (var cmd in new[] { "F", "G", "P" })
            {
                Debug.WriteLine($">>> [SGE] Sending: {cmd} {instanceIndex}\\n");
                await writer.WriteAsync($"{cmd} {instanceIndex}\n");
                await writer.FlushAsync();
                var resp = await reader.ReadLineAsync();
                Debug.WriteLine($">>> [SGE] {cmd} response: [{resp}]");
            }

            // 6) List characters
            Debug.WriteLine(">>> [SGE] Sending: C\\n (list characters)");
            await writer.WriteAsync("C\n");
            await writer.FlushAsync();
            var cHeader = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] C header: [{cHeader}]");

            var chars = new List<string>();
            while ((line = await reader.ReadLineAsync()) != null && line.StartsWith("\t"))
            {
                Debug.WriteLine($">>> [SGE] Char entry: [{line}]");
                chars.Add(line.Trim());
            }
            if (chars.Count == 0)
                throw new Exception("No characters found in C response.");

            // 7) Pick first character
            var charId = chars[0].Split('\t', StringSplitOptions.RemoveEmptyEntries)[0];
            Debug.WriteLine($">>> [SGE] Selected character ID: {charId}");

            // 8) Login character
            var loginCmd = $"L {charId} STORM\n";
            Debug.WriteLine($">>> [SGE] Sending LOGIN: [{loginCmd}]");
            await writer.WriteAsync(loginCmd);
            await writer.FlushAsync();

            var lResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] LOGIN response: [{lResp}]");
            var f = lResp.Split('\t');
            var host = f[3];
            var port = int.Parse(f[4]);
            var sessionKey = f[5];

            Debug.WriteLine($">>> [SGE] Handshake complete: host={host}, port={port}, key={sessionKey}");
            return (host, port, sessionKey);
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
        private static string HashPassword(string password, string key)
        {
            // get raw bytes of the key just like Ruby.bytes
            var keyBytes = Encoding.ASCII.GetBytes(key);
            var result = new byte[password.Length];

            for (int i = 0; i < password.Length; i++)
            {
                // pw_byte and key_byte are 0–255
                int p = (password[i] - 32) & 0xFF;
                int k = keyBytes[i % keyBytes.Length];

                // XOR then add 32, wrap mod 256
                int h = (p ^ k) + 32;
                result[i] = (byte)(h & 0xFF);
            }

            // pack bytes back into a string
            return Encoding.ASCII.GetString(result);
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
