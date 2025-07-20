using System.Diagnostics;
using WraithLite.Services;
using WraithLite.ViewModels;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        private readonly GameClient _client = new();

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
