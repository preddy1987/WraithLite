using Microsoft.Maui.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WraithLite.Services;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        private readonly GameClient _client = new();
        private bool _isConnected = false;

        public MainPage()
        {
            InitializeComponent();
            SendButton.Clicked += OnSendClicked;
            ConnectButton.Clicked += OnConnectClicked;
            LichButton.Clicked += OnLichClicked;
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (_isConnected) return;

            try
            {
                // These could be moved to text entries for dynamic input
                var (host, port, sessionKey) = await _client.FullSgeLoginAsync("preddy777", "avamae1212");

                await _client.ConnectToGameAsync(host, port, sessionKey, OnGameOutputReceived);

                AppendToStory(">>> Connected to game server.");
                _isConnected = true;
            }
            catch (Exception ex)
            {
                AppendToStory($"ERROR: {ex.Message}");
            }
        }

        private async void OnSendClicked(object sender, EventArgs e)
        {
            var command = CommandEntry.Text;
            if (!string.IsNullOrWhiteSpace(command))
            {
                await _client.SendCommandAsync(command);
                AppendToStory($"> {command}");
                CommandEntry.Text = string.Empty;
            }
        }

        private void OnLichClicked(object sender, EventArgs e)
        {
            AppendToStory("Lich integration not implemented yet.");
            // Placeholder for launching or connecting to lich5 integration
        }

        private void OnGameOutputReceived(string line)
        {
            // You can route more precisely with smarter parsing
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (line.Contains("thoughtfully"))
                    AppendToThoughts(line);
                else if (line.Contains("says") || line.Contains("asks"))
                    AppendToSpeech(line);
                else
                    AppendToStory(line);
            });
        }

        private void AppendToStory(string line)
        {
            StoryOutput.Text += line + Environment.NewLine;
        }

        private void AppendToThoughts(string line)
        {
            ThoughtsOutput.Text += line + Environment.NewLine;
        }

        private void AppendToSpeech(string line)
        {
            SpeechOutput.Text += line + Environment.NewLine;
        }
    }
}
