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
            Debug.WriteLine(">>> [SGE] Starting full SGE handshake (forced GS3)");

            // Latin1 for raw byte fidelity
            var latin1 = Encoding.GetEncoding("ISO-8859-1");

            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);

            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, latin1);
            using var writer = new StreamWriter(stream, latin1) { AutoFlush = true };

            // 1) Challenge
            await writer.WriteAsync("K\n");
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> Challenge: {Escape(challenge)}");

            // 2) Hash password
            var keyBytes = latin1.GetBytes(challenge);
            var hashBytes = new byte[password.Length];
            for (int i = 0; i < password.Length; i++)
            {
                int p = (password[i] - 32) & 0xFF;
                int k = keyBytes[i % keyBytes.Length];
                hashBytes[i] = (byte)(((p ^ k) + 32) & 0xFF);
            }
            var hash = latin1.GetString(hashBytes);
            Debug.WriteLine($">>> Hash: {Escape(hash)}");

            // 3) Authenticate
            await writer.WriteAsync($"A\t{username}\t{hash}\n");
            var aResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> AUTH response: {Escape(aResp)}");
            if (!aResp.StartsWith("A\t"))
                throw new Exception($"Authentication failed: {aResp}");

            // 4) Force‐select GS3
            const string shard = "GS3";
            await writer.WriteAsync($"N\t{shard}\n");
            var nResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> N response: {Escape(nResp)}");
            if (!nResp.StartsWith("N\t"))
                throw new Exception($"Game select failed: {nResp}");

            // 5) Confirm subscription/payment (F, G, P)
            foreach (var cmd in new[] { "F", "G", "P" })
            {
                await writer.WriteAsync($"{cmd}\t{shard}\n");
                var resp = await reader.ReadLineAsync();
                Debug.WriteLine($">>> {cmd} response: {Escape(resp)}");
                if (!resp.StartsWith($"{cmd}\t") && cmd != "P")
                    throw new Exception($"{cmd} check failed: {resp}");
            }

            // 6) List characters (C)
            await writer.WriteAsync("C\n");
            var cHeader = await reader.ReadLineAsync();
            Debug.WriteLine($">>> C header: {Escape(cHeader)}");
            var cols = cHeader.Split('\t', StringSplitOptions.None);
            if (cols.Length < 2 || !int.TryParse(cols[1], out var count) || count == 0)
                throw new Exception($"No characters found on shard {shard}");

            // 7) Read the first character entry
            var charLine = await reader.ReadLineAsync();   // e.g. "\t12345\tMyChar"
            Debug.WriteLine($">>> Char entry: {Escape(charLine)}");
            var charId = charLine.Trim().Split('\t', StringSplitOptions.None)[0];

            // 8) Login character (L)
            await writer.WriteAsync($"L\t{charId}\tSTORM\n");
            var lResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> LOGIN response: {Escape(lResp)}");

            // 9) Parse final response: L\tGS3\tuser\thost\tport\tsessionKey
            var tok = lResp.Split('\t', StringSplitOptions.None);
            var host = tok[3];
            var port = int.Parse(tok[4]);
            var sessionKey = tok[5];
            Debug.WriteLine($">>> Handshake complete: {host}:{port} key={sessionKey}");

            return (host, port, sessionKey);
        }

        // helper to make tabs visible in debug
        private static string Escape(string s) =>
            s?
              .Replace("\t", "\\t")
              .Replace("\r", "\\r")
              .Replace("\n", "\\n")
            ?? "<null>";



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
