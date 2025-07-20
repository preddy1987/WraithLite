using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SgeTester
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: SgeTester <username> <password>");
                return;
            }
            var username = args[0];
            var password = args[1];

            try
            {
                var (host, port, key) = await FullSgeLoginAsync("preddy777", "avamae1212");
                Console.WriteLine($"\n✅ Handshake succeeded!");
                Console.WriteLine($"Host      : {host}");
                Console.WriteLine($"Port      : {port}");
                Console.WriteLine($"SessionKey: {key}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ ERROR: {ex.Message}");
            }
        }

        static async Task<(string host, int port, string sessionKey)> FullSgeLoginAsync(
            string username, string password)
        {
            Console.WriteLine(">>> Starting SGE handshake with forced GS3\n");
            var enc = Encoding.GetEncoding("ISO-8859-1");
            using var client = new TcpClient();
            await client.ConnectAsync("eaccess.play.net", 7900);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, enc);
            using var writer = new StreamWriter(stream, enc) { AutoFlush = true };

            // Helper to show control chars
            string ShowRaw(string s) => s
                .Replace("\t", "\\t")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            // Step 1: Challenge
            Console.WriteLine(">> SEND: K");
            writer.WriteLine("K");
            var challenge = await reader.ReadLineAsync();
            Console.WriteLine("<< RECV: " + ShowRaw(challenge));

            // Step 2: Hash
            var keyBytes = enc.GetBytes(challenge);
            var hashBytes = new byte[password.Length];
            for (int i = 0; i < password.Length; i++)
            {
                int p = (password[i] - 32) & 0xFF;
                int k = keyBytes[i % keyBytes.Length];
                hashBytes[i] = (byte)(((p ^ k) + 32) & 0xFF);
            }
            var hash = enc.GetString(hashBytes);
            Console.WriteLine(">> HASH: " + ShowRaw(hash));

            // Step 3: Auth
            Console.WriteLine($">> SEND: A\\t{username}\\t{hash}");
            writer.WriteLine($"A\t{username}\t{hash}");
            var aResp = await reader.ReadLineAsync();
            Console.WriteLine("<< RECV: " + ShowRaw(aResp));
            if (!aResp.StartsWith("A\t"))
                throw new Exception("Auth failed");

            // Step 4: Force GS3
            const string shard = "GS3";
            Console.WriteLine($">> SEND: N {shard}");
            writer.WriteLine($"N {shard}");
            var nResp = await reader.ReadLineAsync();
            Console.WriteLine("<< RECV: " + ShowRaw(nResp));
            if (!nResp.StartsWith("N\t"))
                throw new Exception("Game select failed");

            // Step 5: F G P
            foreach (var cmd in new[] { "F", "G", "P" })
            {
                Console.WriteLine($">> SEND: {cmd} {shard}");
                writer.WriteLine($"{cmd} {shard}");
                var resp = await reader.ReadLineAsync();
                Console.WriteLine("<< RECV: " + ShowRaw(resp));
                if (!resp.StartsWith(cmd + "\t") && cmd != "P")
                    throw new Exception($"{cmd} check failed");
            }

            // Step 6: List characters
            Console.WriteLine(">> SEND: C");
            writer.WriteLine("C");
            var cHeader = await reader.ReadLineAsync();
            Console.WriteLine("<< RECV: " + ShowRaw(cHeader));
            var parts = cHeader.Split('\t');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var count) || count < 1)
                throw new Exception("No characters found");

            // Read the first character
            var charLine = await reader.ReadLineAsync();
            Console.WriteLine("<< RECV: " + ShowRaw(charLine));
            var charId = charLine.Trim().Split('\t')[0];

            // Step 7: Login character
            Console.WriteLine($">> SEND: L {charId} STORM");
            writer.WriteLine($"L {charId} STORM");
            var lResp = await reader.ReadLineAsync();
            Console.WriteLine("<< RECV: " + ShowRaw(lResp));

            // Parse final response
            var tok = lResp.Split('\t');
            return (tok[3], int.Parse(tok[4]), tok[5]);
        }
    }
}
