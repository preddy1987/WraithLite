using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WraithLite.Services
{
    public sealed class GameClient : IDisposable
    {
        private TcpClient _gameTcpClient;
        private NetworkStream _gameStream;
        private CancellationTokenSource _readLoopCts;

        private readonly SemaphoreSlim _sendLock = new(1, 1);

        private StreamWriter _logWriter;
        private readonly SemaphoreSlim _logLock = new(1, 1);

        private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

        public bool IsConnected => _gameStream != null;

        // ==========================================================
        // SGE Login (unchanged)
        // ==========================================================

        public async Task<(string host, int port, string sessionKey)> FullSgeLoginAsync(
            string username,
            string password,
            string preferredCharacterName = null)
        {
            Debug.WriteLine(">>> [SGE] Starting full SGE handshake (forced GS3)");

            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);

            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Latin1);
            using var writer = new StreamWriter(stream, Latin1) { AutoFlush = true };

            await writer.WriteLineAsync("K");
            var challenge = await reader.ReadLineAsync();
            Debug.WriteLine($">>> Challenge: {Escape(challenge)}");

            var keyBytes = Latin1.GetBytes(challenge);
            var hashBytes = new byte[password.Length];
            for (int i = 0; i < password.Length; i++)
            {
                int p = (password[i] - 32) & 0xFF;
                int k = keyBytes[i % keyBytes.Length];
                hashBytes[i] = (byte)(((p ^ k) + 32) & 0xFF);
            }

            var hash = Latin1.GetString(hashBytes);

            await writer.WriteLineAsync($"A\t{username}\t{hash}");
            var aResp = await reader.ReadLineAsync();
            if (aResp == null || !aResp.StartsWith("A\t"))
                throw new Exception($"Authentication failed: {aResp}");

            const string shard = "GS3";
            await writer.WriteLineAsync($"N\t{shard}");
            var nResp = await reader.ReadLineAsync();
            if (nResp == null || !nResp.StartsWith("N\t"))
                throw new Exception($"Game select failed: {nResp}");

            foreach (var cmd in new[] { "F", "G", "P" })
            {
                await writer.WriteLineAsync($"{cmd}\t{shard}");
                var resp = await reader.ReadLineAsync();
                if (resp == null)
                    throw new Exception($"{cmd} check failed: <null>");
                if (!resp.StartsWith($"{cmd}\t") && cmd != "P")
                    throw new Exception($"{cmd} check failed: {resp}");
            }

            await writer.WriteLineAsync("C");
            var cHeader = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(cHeader))
                throw new Exception("Character list response was empty.");

            var parts = cHeader.Split('\t', StringSplitOptions.None);
            if (parts.Length < 6 || !int.TryParse(parts[1], out var charCount) || charCount == 0)
                throw new Exception($"No characters found on shard {shard}");

            var entries = new List<(string Id, string Name)>();
            for (int i = 5; i + 1 < parts.Length; i += 2)
                entries.Add((parts[i], parts[i + 1]));

            var chosen = (!string.IsNullOrEmpty(preferredCharacterName))
                ? entries.FirstOrDefault(e => e.Name.Equals(preferredCharacterName, StringComparison.OrdinalIgnoreCase))
                : default;

            if (string.IsNullOrEmpty(chosen.Id))
                chosen = entries[0];

            await writer.WriteLineAsync($"L\t{chosen.Id}\tSTORM");
            var lResp = await reader.ReadLineAsync();
            if (lResp == null || !lResp.StartsWith("L\t"))
                throw new Exception($"Character login failed: {lResp}");

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

            return (host, port, sessionKey);
        }

        // ==========================================================
        // Connect + ONE read loop (robust CR/LF + prompt handling)
        // ==========================================================

        public async Task ConnectToGameAsync(string host, int port, string sessionKey, Action<string> onOutput)
        {
            if (onOutput is null)
                throw new ArgumentNullException(nameof(onOutput));

            Disconnect();

            _readLoopCts = new CancellationTokenSource();

            _gameTcpClient = new TcpClient { NoDelay = true };
            await _gameTcpClient.ConnectAsync(host, port);
            _gameStream = _gameTcpClient.GetStream();

            await OpenLogAsync();

            // Handshake: KEY then blank line
            await SendRawLineAsync(sessionKey, _readLoopCts.Token);
            await SendRawLineAsync(string.Empty, _readLoopCts.Token);

            _ = Task.Run(() => ReadLoopAsync(onOutput, _readLoopCts.Token), _readLoopCts.Token);
        }

        private async Task ReadLoopAsync(Action<string> onOutput, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await _gameStream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (n <= 0)
                        break;

                    var rawText = Latin1.GetString(buffer, 0, n);

                    await WriteLogLineAsync($"{Timestamp()} IN : {rawText.Replace("\r", "\\r").Replace("\n", "\\n")}");

                    sb.Append(rawText);

                    // Split on CR, LF, or CRLF
                    while (TryTakeLine(sb, out var line))
                    {
                        // Emit every logical “line”
                        onOutput(line);
                    }

                    // StormFront/GS often ends output with a bare prompt ">" with NO newline.
                    // If that's all that's left in the buffer, emit it immediately.
                    if (sb.Length == 1 && sb[0] == '>')
                    {
                        sb.Clear();
                        onOutput(">");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($">>> [GAME] Read loop exception: {ex}");
                await WriteLogLineAsync($"{Timestamp()} ERR: Read loop exception: {ex}");
            }
            finally
            {
                await WriteLogLineAsync($"{Timestamp()} INF: Read loop ended.");
            }
        }

        // Handles \n, \r, or \r\n without losing content
        private static bool TryTakeLine(StringBuilder sb, out string line)
        {
            line = null;
            if (sb.Length == 0) return false;

            int idx = -1;
            for (int i = 0; i < sb.Length; i++)
            {
                char c = sb[i];
                if (c == '\n' || c == '\r')
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
                return false;

            line = sb.ToString(0, idx);

            // Consume delimiter(s)
            int consume = 1;
            if (sb[idx] == '\r' && idx + 1 < sb.Length && sb[idx + 1] == '\n')
                consume = 2;

            sb.Remove(0, idx + consume);

            return true;
        }

        // ==========================================================
        // Sending
        // ==========================================================

        public async Task SendCommandAsync(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            if (_gameStream == null)
                throw new InvalidOperationException("Not connected to game.");

            if (!await _sendLock.WaitAsync(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("Send lock timeout (previous send likely stuck).");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                var clean = command.Trim();
                await WriteLogLineAsync($"{Timestamp()} OUT: {Escape(clean)}");

                await SendRawLineAsync(clean, cts.Token);
            }
            catch (OperationCanceledException)
            {
                await WriteLogLineAsync($"{Timestamp()} ERR: Send timed out.");
                throw new TimeoutException("Send timed out.");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendRawLineAsync(string line, CancellationToken ct)
        {
            if (_gameStream == null)
                throw new InvalidOperationException("Not connected to game.");

            var clean = (line ?? string.Empty).TrimEnd('\r', '\n');
            var payload = Latin1.GetBytes(clean + "\r\n");
            await _gameStream.WriteAsync(payload, 0, payload.Length, ct);
            await _gameStream.FlushAsync(ct);
        }

        // ==========================================================
        // Logging (Documents\WraithLite)
        // ==========================================================

        private async Task OpenLogAsync()
        {
            await CloseLogAsync();

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dir = Path.Combine(docs, "WraithLite");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"gs_stream_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _logWriter = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };

            await WriteLogLineAsync($"# WraithLite stream log started {DateTime.Now:O}");
            await WriteLogLineAsync($"# Encoding: Latin1 (wire) -> UTF8 (log)");
            await WriteLogLineAsync($"# ------------------------------------------------------------");
        }

        private async Task WriteLogLineAsync(string line)
        {
            if (_logWriter == null) return;

            await _logLock.WaitAsync();
            try { await _logWriter.WriteLineAsync(line); }
            finally { _logLock.Release(); }
        }

        private async Task CloseLogAsync()
        {
            var w = _logWriter;
            _logWriter = null;
            if (w == null) return;

            try
            {
                await _logLock.WaitAsync();
                try { await w.FlushAsync(); w.Dispose(); }
                finally { _logLock.Release(); }
            }
            catch { }
        }

        private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        // ==========================================================
        // Cleanup
        // ==========================================================

        public void Disconnect()
        {
            try { _readLoopCts?.Cancel(); } catch { }

            try { _gameStream?.Dispose(); } catch { }
            try { _gameTcpClient?.Close(); } catch { }

            _gameStream = null;
            _gameTcpClient = null;

            try { _readLoopCts?.Dispose(); } catch { }
            _readLoopCts = null;

            try { CloseLogAsync().GetAwaiter().GetResult(); } catch { }
        }

        public void Dispose()
        {
            Disconnect();
            _sendLock.Dispose();
            _logLock.Dispose();
        }

        private static string Escape(string s) =>
            s?.Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n") ?? "<null>";
    }
}
