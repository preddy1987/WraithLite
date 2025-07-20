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

            // 1) Connect
            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            // 2) Challenge
            Debug.WriteLine(">>> [SGE] Sending: K\\n");
            await writer.WriteAsync("K\n");
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] Received challenge: [{challenge}]");

            // 3) Hash (Ruby‑style)
            var keyBytes = Encoding.ASCII.GetBytes(challenge);
            var hashBytes = new byte[password.Length];
            for (int i = 0; i < password.Length; i++)
            {
                int p = (password[i] - 32) & 0xFF;
                int k = keyBytes[i % keyBytes.Length];
                int h = (p ^ k) + 32;
                hashBytes[i] = (byte)(h & 0xFF);
            }
            var hash = Encoding.ASCII.GetString(hashBytes);
            Debug.WriteLine($">>> [SGE] Computed hash: [{hash}]");

            // 4) Auth (A <user> <hash> GS 1)
            var authCmd = $"A\t{username}\t{hash}\tGS\t1\n";
            Debug.WriteLine($">>> [SGE] Sending AUTH: [{authCmd.Replace(hash, "****")}]");
            await writer.WriteAsync(authCmd);
            var aResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] AUTH response: [{aResp}]");
            if (!aResp.StartsWith("A\tGSIV"))
                throw new Exception($"Auth failed: {aResp}");

            // 5) List products (M)
            Debug.WriteLine(">>> [SGE] Sending: M\\n (list products)");
            await writer.WriteAsync("M\n");
            string mLine;
            do
            {
                mLine = await reader.ReadLineAsync();
                Debug.WriteLine($">>> [SGE] M-line: [{mLine}]");
            } while (mLine != null && !mLine.StartsWith("M\t"));

            // 6) Find numeric slot of “GemStone IV”
            var parts = mLine.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            int slot = -1;
            for (int idx = 1, count = 1; idx < parts.Length; idx += 2, count++)
            {
                if (parts[idx + 1].Equals("GemStone IV", StringComparison.OrdinalIgnoreCase))
                {
                    slot = count;
                    break;
                }
            }
            if (slot < 1)
                throw new Exception("Could not find GemStone IV in product list.");

            Debug.WriteLine($">>> [SGE] Selecting slot #{slot}");
            // 7) Select game (N <slot>)
            await writer.WriteAsync($"N {slot}\n");
            var nResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] N response: [{nResp}]");
            if (!nResp.StartsWith("N\t"))
                throw new Exception($"Game select failed: {nResp}");

            // 8) Confirm subscription/payment (F, G, P)
            foreach (var cmd in new[] { "F", "G", "P" })
            {
                Debug.WriteLine($">>> [SGE] Sending: {cmd} {slot}\\n");
                await writer.WriteAsync($"{cmd} {slot}\n");
                var resp = await reader.ReadLineAsync();
                Debug.WriteLine($">>> [SGE] {cmd} response: [{resp}]");
                if (!resp.StartsWith($"{cmd}\t"))
                    throw new Exception($"{cmd} failed: {resp}");
            }

            // 9) List characters (C)
            Debug.WriteLine(">>> [SGE] Sending: C\\n (list characters)");
            await writer.WriteAsync("C\n");
            var cHeader = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] C header: [{cHeader}]");

            var chars = new List<string>();
            string line;
            while ((line = await reader.ReadLineAsync()) != null && line.StartsWith("\t"))
            {
                Debug.WriteLine($">>> [SGE] Char entry: [{line}]");
                chars.Add(line.Trim());
            }
            if (chars.Count == 0)
                throw new Exception("No characters returned.");

            // 10) Login first character (L <charId> STORM)
            var charId = chars[0].Split('\t', StringSplitOptions.RemoveEmptyEntries)[0];
            Debug.WriteLine($">>> [SGE] Logging in char ID: {charId}");
            await writer.WriteAsync($"L {charId} STORM\n");
            var lResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] LOGIN response: [{lResp}]");

            // 11) Parse host, port, key
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
            // Ruby does: hash_bytes = password.bytes.each_with_index.map { |pw, i|
            //    ((pw-32) ^ key_bytes[i % key_bytes.size]) + 32 } and then pack("C*")
            var keyBytes = Encoding.ASCII.GetBytes(key);
            var result = new byte[password.Length];

            for (int i = 0; i < password.Length; i++)
            {
                int p = (password[i] - 32) & 0xFF;          // bring into 0–95 range
                int k = keyBytes[i % keyBytes.Length];     // wrap the key
                int h = (p ^ k) + 32;                      // XOR then shift back
                result[i] = (byte)(h & 0xFF);              // allow full 0–255 overflow
            }

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
