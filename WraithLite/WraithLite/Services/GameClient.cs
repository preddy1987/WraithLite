using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace WraithLite.Services
{
    public class GameClient
    {
        private StreamWriter _writer;

        public async Task<(string host, int port, string sessionKey)> FullSgeLoginAsync(
            string username,
            string password,
            string preferredCharacterName = null)
        {
            Debug.WriteLine(">>> [SGE] Starting full SGE handshake (forced GS3)");

            var latin1 = Encoding.GetEncoding("ISO-8859-1");
            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, latin1);
            using var writer = new StreamWriter(stream, latin1) { AutoFlush = true };
            _writer = writer;

            // 1) Challenge
            await writer.WriteLineAsync("K");
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> Challenge: {Escape(challenge)}");

            // 2) Hash
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
            await writer.WriteLineAsync($"A\t{username}\t{hash}");
            var aResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> AUTH response: {Escape(aResp)}");
            if (!aResp.StartsWith("A\t"))
                throw new Exception($"Authentication failed: {aResp}");

            // 4) Select GS3
            const string shard = "GS3";
            await writer.WriteLineAsync($"N\t{shard}");
            var nResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> N response: {Escape(nResp)}");
            if (!nResp.StartsWith("N\t"))
                throw new Exception($"Game select failed: {nResp}");

            // 5) Confirm F/G/P
            foreach (var cmd in new[] { "F", "G", "P" })
            {
                await writer.WriteLineAsync($"{cmd}\t{shard}");
                var resp = await reader.ReadLineAsync();
                Debug.WriteLine($">>> {cmd} response: {Escape(resp)}");
                if (!resp.StartsWith($"{cmd}\t") && cmd != "P")
                    throw new Exception($"{cmd} check failed: {resp}");
            }

            // 6) List characters (C)
            await writer.WriteLineAsync("C");
            var cHeader = await reader.ReadLineAsync();
            Debug.WriteLine($">>> C header: {Escape(cHeader)}");

            var parts = cHeader.Split('\t', StringSplitOptions.None);
            if (parts.Length < 6 || !int.TryParse(parts[1], out var charCount) || charCount == 0)
                throw new Exception($"No characters found on shard {shard}");

            // Build list of (Id,Name)
            var entries = new List<(string Id, string Name)>();
            for (int i = 5; i + 1 < parts.Length; i += 2)
                entries.Add((parts[i], parts[i + 1]));

            // Pick preferred or default
            var chosen = (!string.IsNullOrEmpty(preferredCharacterName))
                ? entries.FirstOrDefault(e =>
                    e.Name.Equals(preferredCharacterName, StringComparison.OrdinalIgnoreCase))
                : default;
            if (string.IsNullOrEmpty(chosen.Id))
                chosen = entries[0];

            Debug.WriteLine($">>> Selected Character: {chosen.Id} ({chosen.Name})");

            // 7) Login character (L)
            await writer.WriteLineAsync($"L\t{chosen.Id}\tSTORM");
            var lResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> LOGIN response: {Escape(lResp)}");
            if (!lResp.StartsWith("L\t"))
                throw new Exception($"Character login failed: {lResp}");

            // 8) Parse final L response dynamically
            //    Look for GAMEHOST=..., GAMEPORT=..., KEY=...
            var tokens = lResp.Split('\t', StringSplitOptions.None);
            string host = null;
            int port = 0;
            string sessionKey = null;

            foreach (var tok in tokens)
            {
                if (tok.StartsWith("GAMEHOST=", StringComparison.OrdinalIgnoreCase))
                    host = tok.Substring("GAMEHOST=".Length);
                else if (tok.StartsWith("GAMEPORT=", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(tok.Substring("GAMEPORT=".Length), out var p))
                    port = p;
                else if (tok.StartsWith("KEY=", StringComparison.OrdinalIgnoreCase))
                    sessionKey = tok.Substring("KEY=".Length);
            }

            if (host == null || port == 0 || sessionKey == null)
                throw new Exception($"Failed to parse final login response: {lResp}");

            Debug.WriteLine($">>> Handshake complete: {host}:{port} key={sessionKey}");
            return (host, port, sessionKey);
        }

        public async Task ConnectToGameAsync(
            string host, int port, string sessionKey,
            Action<string> onOutput)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port);
            var latin1 = Encoding.GetEncoding("ISO-8859-1");
            var stream = client.GetStream();
            var reader = new StreamReader(stream, latin1);
            _writer = new StreamWriter(stream, latin1) { AutoFlush = true };

            await _writer.WriteLineAsync(sessionKey);
            await _writer.WriteLineAsync();

            _ = Task.Run(async () =>
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                    onOutput(line);
            });
        }

        public Task SendCommandAsync(string command)
        {
            if (_writer == null)
                throw new InvalidOperationException("Not connected to game.");
            return _writer.WriteLineAsync(command);
        }

        private static string Escape(string s) =>
            s?
              .Replace("\t", "\\t")
              .Replace("\r", "\\r")
              .Replace("\n", "\\n")
            ?? "<null>";
    }
}
