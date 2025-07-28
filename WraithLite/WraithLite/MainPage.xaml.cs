using Microsoft.Maui.Controls;
using System;
using WraithLite.Services;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        private readonly GameClient _client = new();
        private bool _isConnected = false;
        private string _username;
        private string _password;
        // Flags to control auto-scrolling behavior
        private bool _autoScrollStory = true;
        private bool _autoScrollThoughts = true;
        private bool _autoScrollSpeech = true;

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
            // This is likely invoked on a background thread, so marshal to UI thread
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
            // Only auto-scroll if user is at bottom
            if (_autoScrollStory)
            {
                StoryScroll?.ScrollToAsync(label, ScrollToPosition.End, true);
            }
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
            if (_autoScrollThoughts)
            {
                ThoughtsScroll?.ScrollToAsync(label, ScrollToPosition.End, true);
            }
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
            if (_autoScrollSpeech)
            {
                SpeechScroll?.ScrollToAsync(label, ScrollToPosition.End, true);
            }
        }

        // Event handler for StoryScroll Scrolled event
        private void OnStoryScrollScrolled(object sender, ScrolledEventArgs e)
        {
            if (sender is ScrollView scrollView)
            {
                double scrollSpace = scrollView.ContentSize.Height - scrollView.Height;
                // If near the bottom (or content smaller than view), enable auto-scroll. Otherwise, disable it.
                _autoScrollStory = scrollSpace <= 0 || e.ScrollY >= scrollSpace - 10;
            }
        }

        // Event handler for ThoughtsScroll Scrolled event
        private void OnThoughtsScrollScrolled(object sender, ScrolledEventArgs e)
        {
            if (sender is ScrollView scrollView)
            {
                double scrollSpace = scrollView.ContentSize.Height - scrollView.Height;
                _autoScrollThoughts = scrollSpace <= 0 || e.ScrollY >= scrollSpace - 10;
            }
        }

        // Event handler for SpeechScroll Scrolled event
        private void OnSpeechScrollScrolled(object sender, ScrolledEventArgs e)
        {
            if (sender is ScrollView scrollView)
            {
                double scrollSpace = scrollView.ContentSize.Height - scrollView.Height;
                _autoScrollSpeech = scrollSpace <= 0 || e.ScrollY >= scrollSpace - 10;
            }
        }
    }
}
