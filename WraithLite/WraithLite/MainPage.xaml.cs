using Microsoft.Maui.Controls;
using System;
using System.Diagnostics;
using WraithLite.Services;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        private readonly GameClient _client = new();
        private bool _isConnected = false;
        private string _username;
        private string _password;

        public MainPage()
        {
            InitializeComponent();
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (_isConnected) return;

            try
            {
                // Show login modal and await credentials
                var loginPage = new LoginModalPage();
                loginPage.LoginCompleted += async (s, args) =>
                {
                    _username = args.Username;
                    _password = args.Password;

                    try
                    {
                        var (host, port, sessionKey) = await _client.FullSgeLoginAsync(_username, _password);
                        await _client.ConnectToGameAsync(host, port, sessionKey, OnGameOutputReceived);

                        AppendToStory(">>> Connected to game server.");
                        _isConnected = true;
                    }
                    catch (Exception ex)
                    {
                        AppendToStory($"ERROR: {ex.Message}");
                    }
                };

                await Navigation.PushModalAsync(loginPage);
            }
            catch (Exception ex)
            {
                AppendToStory($"ERROR: {ex.Message}");
            }
        }

        private async void OnCommandEntered(object sender, EventArgs e)
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
        }

        private void OnGameOutputReceived(string line)
        {
            Dispatcher.Dispatch(() =>
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
            var label = new Label
            {
                Text = line,
                FontFamily = "Courier New",
                FontSize = 14,
                TextColor = Colors.White
            };
            StoryOutputStack?.Children.Add(label);
            StoryScroll?.ScrollToAsync(label, ScrollToPosition.End, true);
        }

        private void AppendToThoughts(string line)
        {
            var label = new Label
            {
                Text = line,
                FontFamily = "Courier New",
                FontSize = 14,
                TextColor = Colors.White
            };
            ThoughtsOutputStack?.Children.Add(label);
            ThoughtsScroll?.ScrollToAsync(label, ScrollToPosition.End, true);
        }

        private void AppendToSpeech(string line)
        {
            var label = new Label
            {
                Text = line,
                FontFamily = "Courier New",
                FontSize = 14,
                TextColor = Colors.White
            };
            SpeechOutputStack?.Children.Add(label);
            SpeechScroll?.ScrollToAsync(label, ScrollToPosition.End, true);
        }
    }
}
