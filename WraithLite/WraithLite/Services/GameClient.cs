using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Performs the full SGE handshake (K, A, M, N, F, G, P, C, L)
        /// and returns the host, port, and session key.
        /// </summary>
        public async Task<(string host, int port, string sessionKey)> FullSgeLoginAsync(
            string username, string password)
        {
            Debug.WriteLine(">>> [SGE] Starting full SGE handshake");

            // Use Latin1 so bytes 0–255 map directly to chars
            var latin1 = Encoding.GetEncoding("ISO-8859-1");

            // 1) Connect
            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, latin1);
            using var writer = new StreamWriter(stream, latin1) { AutoFlush = true };

            // 2) Challenge
            Debug.WriteLine(">>> [SGE] Sending: K\\n");
            await writer.WriteAsync("K\n");
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] Received challenge: [{challenge}]");

            // 3) Hash password
            var keyBytes = latin1.GetBytes(challenge);
            var hashBytes = new byte[password.Length];
            for (int i = 0; i < password.Length; i++)
            {
                int p = (password[i] - 32) & 0xFF;
                int k = keyBytes[i % keyBytes.Length];
                int h = (p ^ k) + 32;
                hashBytes[i] = (byte)(h & 0xFF);
            }
            var hash = latin1.GetString(hashBytes);
            Debug.WriteLine($">>> [SGE] Computed hash: [{hash}]");

            // 4) Auth
            Debug.WriteLine(">>> [SGE] Sending AUTH");
            await writer.WriteAsync($"A\t{username}\t{hash}\n");
            var aResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] AUTH response: [{aResp}]");
            if (!aResp.StartsWith("A\t"))
                throw new Exception($"SGE auth failed: {aResp}");

            // 5) List products
            Debug.WriteLine(">>> [SGE] Sending: M\\n (list products)");
            await writer.WriteAsync("M\n");
            await writer.FlushAsync();

            //  Read until the M-line
            string mLine;
            do
            {
                mLine = await reader.ReadLineAsync();
                Debug.WriteLine($">>> [SGE] M-line: [{mLine}]");
            } while (mLine != null && !mLine.StartsWith("M\t"));
            if (mLine == null)
                throw new Exception("SGE did not return a product list.");

            // 6) Extract the instance code for "GemStone IV"
            //    mLine format: M\tCODE1\tNAME1\tCODE2\tNAME2\t…
            var parts = mLine.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            string gameCode = null;
            for (int i = 1; i + 1 < parts.Length; i += 2)
            {
                if (parts[i + 1]
                      .Equals("GemStone IV", StringComparison.OrdinalIgnoreCase))
                {
                    gameCode = parts[i];  // e.g. "GS3"
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(gameCode))
                throw new Exception("Could not find GemStone IV in product list.");

            // 7) Select game by its code
            Debug.WriteLine($">>> [SGE] Selecting game code {gameCode}");
            await writer.WriteAsync($"N {gameCode}\n");
            var nResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] N response: [{nResp}]");
            if (!nResp.StartsWith("N\t"))
                throw new Exception($"Game select failed: {nResp}");

            // 8) Confirm subscription/payment (F, G, P)
            foreach (var cmd in new[] { "F", "G", "P" })
            {
                Debug.WriteLine($">>> [SGE] Sending: {cmd} {gameCode}");
                await writer.WriteAsync($"{cmd} {gameCode}\n");
                var resp = await reader.ReadLineAsync();
                Debug.WriteLine($">>> [SGE] {cmd} response: [{resp}]");
                if (!resp.StartsWith($"{cmd}\t") && cmd != "P")
                    throw new Exception($"{cmd} check failed: {resp}");
            }

            // 9) List characters
            Debug.WriteLine(">>> [SGE] Sending: C\\n (list characters)");
            await writer.WriteAsync("C\n");
            await writer.FlushAsync();
            var cHeader = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] C header: [{cHeader}]");

            var chars = new List<string>();
            string line;
            while ((line = await reader.ReadLineAsync()) != null
                   && line.StartsWith("\t"))
            {
                Debug.WriteLine($">>> [SGE] Char entry: [{line}]");
                chars.Add(line.Trim());
            }
            if (chars.Count == 0)
                throw new Exception("No characters found.");

            // 10) Login first character
            var charId = chars[0]
                .Split('\t', StringSplitOptions.RemoveEmptyEntries)[0];
            Debug.WriteLine($">>> [SGE] Logging in char ID: {charId}");
            await writer.WriteAsync($"L {charId} STORM\n");
            var lResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> [SGE] LOGIN response: [{lResp}]");

            // 11) Parse final response
            var tokens = lResp.Split('\t');
            var host = tokens[3];
            var portNum = int.Parse(tokens[4]);
            var sessionKey = tokens[5];
            Debug.WriteLine(
                $">>> [SGE] Handshake complete: {host}:{portNum} key={sessionKey}");

            return (host, portNum, sessionKey);
        }


        /// <summary>
        /// Opens the game socket, sends the session key, and begins streaming output.
        /// </summary>
        public async Task ConnectToGameAsync(
            string host, int port, string sessionKey,
            Action<string> onOutput)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port);
            var stream = client.GetStream();
            var latin1 = Encoding.GetEncoding("ISO-8859-1");
            var reader = new StreamReader(stream, latin1);
            _writer = new StreamWriter(stream, latin1) { AutoFlush = true };

            // Send session key + extra newline
            await _writer.WriteLineAsync(sessionKey);
            await _writer.WriteLineAsync();

            // Stream game output
            _ = Task.Run(async () =>
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                    onOutput(line);
            });
        }

        /// <summary>
        /// Send a command to the live game socket.
        /// </summary>
        public Task SendCommandAsync(string command)
        {
            if (_writer == null)
                throw new InvalidOperationException("Not connected to game.");
            return _writer.WriteLineAsync(command);
        }
    }
}
