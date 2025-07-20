using System.Diagnostics;
using WraithLite.Services;
using WraithLite.ViewModels;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        private readonly GameClient _client = new();
        Process _lichProcess;
        bool _lichRunning = false;

        //private async void OnConnectClicked(object sender, EventArgs e)
        //{
        //    try
        //    {
        //        GameOutput.Text += "Connecting to SGE...\n";

        //        var (host, port, key) = await _client.FullSgeLoginAsync("preddy777", "avamae1212");
        //        await _client.ConnectToGameAsync(host, port, key, OnGameOutput);
        //        //var token = await _client.GetGameTokenAsync("preddy777", "avamae1212");
        //        //Debug.WriteLine($"Token: {token}");
        //        //GameOutput.Text += $"Got token: {token}\n";

        //        //var parts = token.Split(':');
        //        //await _client.ConnectToGameAsync(parts[0], int.Parse(parts[1]), parts[2], OnGameOutput);
        //    }
        //    catch (Exception ex)
        //    {
        //        GameOutput.Text += $"ERROR: {ex.Message}\n";
        //    }
        //}

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            GameOutput.Text += "Connecting to SGE…\n";

            try
            {
                // 1) Challenge & hash
                GameOutput.Text += "Requesting challenge key…\n";
                var (host, port, key) = await _client.FullSgeLoginAsync("preddy777", "avamae1212");

                // 2) Got back host/port/key
                GameOutput.Text += $"Received SGE host={host}, port={port}, key={key}\n";

                // 3) Connect to game server
                GameOutput.Text += "Connecting to game server…\n";
                await _client.ConnectToGameAsync(host, port, key, OnGameOutput);
                GameOutput.Text += "Connected to game – streaming output below:\n";
            }
            catch (Exception ex)
            {
                GameOutput.Text += $"ERROR during SGE login: {ex.Message}\n";
            }
        }

        async void OnLichClicked(object sender, EventArgs e)
        {
            if (!_lichRunning)
            {
                // Start headless Lich5
                var psi = new ProcessStartInfo
                {
                    FileName = @"C:\Users\pREDDY\Desktop\Lich5\headless.bat",
                    Arguments = $"--login={UsernameEntry.Text} --password={PasswordEntry.Text}",
                    WorkingDirectory = @"C:\Users\pREDDY\Desktop\Lich5",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _lichProcess = Process.Start(psi);
                _lichProcess.OutputDataReceived += (s, ev) => {
                    if (ev.Data != null)
                        MainThread.BeginInvokeOnMainThread(() => GameOutput.Text += ev.Data + "\n");
                };
                _lichProcess.ErrorDataReceived += (s, ev) => {
                    if (ev.Data != null)
                        MainThread.BeginInvokeOnMainThread(() => GameOutput.Text += "[ERR] " + ev.Data + "\n");
                };

                _lichProcess.BeginOutputReadLine();
                _lichProcess.BeginErrorReadLine();

                _lichRunning = true;
                LichButton.Text = "Stop Lich";
            }
            else
            {
                // Stop Lich5
                try
                {
                    _lichProcess.Kill();
                }
                catch { /* ignore */ }

                _lichProcess.Dispose();
                _lichProcess = null;
                _lichRunning = false;
                LichButton.Text = "Lich";
            }
        }

        private void OnGameOutput(string line)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                GameOutput.Text += line + "\n";
            });
        }
        private async void OnCommandEntered(object sender, EventArgs e)
        {
            var cmd = CommandEntry.Text;
            CommandEntry.Text = "";
            await _client.SendCommandAsync(cmd);
        }

        public MainPage()
        {
            InitializeComponent();
            BindingContext = new MainViewModel();
        }

        void SendCommand(object sender, EventArgs e)
        {
            if (BindingContext is MainViewModel vm)
            {
                vm.SendCommand();
            }
        }
    }

}
