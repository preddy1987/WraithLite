
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

        public async Task<(string host, int port, string sessionKey)> FullSgeLoginAsync(string username, string password)
        {
            Debug.WriteLine(">>> [SGE] Starting full SGE handshake");

            var latin1 = Encoding.GetEncoding("ISO-8859-1");
            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, latin1);
            using var writer = new StreamWriter(stream, latin1) { AutoFlush = true };

            await writer.WriteAsync("K\n");
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> Challenge: {challenge}");

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
            Debug.WriteLine($">>> Hash: {hash}");

            await writer.WriteAsync($"A\t{username}\t{hash}\n");
            var aResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> AUTH response: {aResp}");

            await writer.WriteAsync("M\n");
            string mLine;
            do
            {
                mLine = await reader.ReadLineAsync();
                Debug.WriteLine($">>> M-line: {mLine}");
            } while (mLine != null && !mLine.StartsWith("M\t"));

            var parts = mLine.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            string shard = "GS3";
            Debug.WriteLine($">>> Found GemStone IV at code={shard}");
            await writer.WriteAsync($"N\t{shard}\n");
            var nResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> N response: {nResp}");

            foreach (var cmd in new[] { "F", "G", "P" })
            {
                await writer.WriteAsync($"{cmd}\t{shard}\n");
                var resp = await reader.ReadLineAsync();
                Debug.WriteLine($">>> {cmd} response: {resp}");
            }

            await writer.WriteAsync("C\n");
            var cHeader = await reader.ReadLineAsync();
            Debug.WriteLine($">>> C header: {cHeader}");

            string selectedCharId = null;
            string selectedCharName = null;
            string line;
            while ((line = await reader.ReadLineAsync()) != null && line.StartsWith("\t"))
            {
                Debug.WriteLine($">>> Char line: {line}");
                var fields = line.Trim().Split('\t');
                if (fields.Length >= 2)
                {
                    selectedCharId = fields[0];
                    selectedCharName = fields[1];
                    break;
                }
            }
            if (selectedCharId == null)
                throw new Exception("No characters found");

            Debug.WriteLine($">>> Selected Character: {selectedCharId} ({selectedCharName})");
            await writer.WriteAsync($"L\t{selectedCharId}\tSTORM\n");
            var lResp = await reader.ReadLineAsync();
            Debug.WriteLine($">>> LOGIN response: {lResp}");

            var tokens = lResp.Split('\t');
            var host = tokens[8].Split('=')[1];
            var port = int.Parse(tokens[9].Split('=')[1]);
            var sessionKey = tokens[10].Split('=')[1];
            return (host, port, sessionKey);
        }

        public async Task ConnectToGameAsync(string host, int port, string sessionKey, Action<string> onOutput)
        {
            var client = new TcpClient();
            await client.ConnectAsync(host, port);
            var stream = client.GetStream();
            var latin1 = Encoding.GetEncoding("ISO-8859-1");
            var reader = new StreamReader(stream, latin1);
            _writer = new StreamWriter(stream, latin1) { AutoFlush = true };

            await _writer.WriteLineAsync(sessionKey);
            await _writer.WriteLineAsync();

            _ = Task.Run(async () =>
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    Debug.WriteLine($">>> Game Line: {line}");
                    onOutput(line);
                }
            });
        }

        public Task SendCommandAsync(string command)
        {
            if (_writer == null)
                throw new InvalidOperationException("Not connected to game.");
            return _writer.WriteLineAsync(command);
        }
    }
}
