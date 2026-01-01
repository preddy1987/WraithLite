using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WraithLite.Services;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        private readonly GameClient _client = new();
        private bool _isConnected;

        private string _username;
        private string _password;

        private const int MaxLinesPerPane = 3000;
        private bool _isSendingCommand;

        // Routing regexes must run on stripped text
        private static readonly Regex BracketChannelLine =
            new Regex(@"^\[(?<chan>[^\]]+)\]\s+(?<name>[^:]+):\s+(?<msg>.*)$",
                      RegexOptions.Compiled);

        private static readonly Regex ThoughtHeader =
            new Regex(@"^You hear the (?<chan>.+?) thoughts of (?<name>.+?) echo in your mind:$",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private (string Channel, string Speaker)? _pendingThoughtHeader;

        // ANSI stream parser (stateful)
        private readonly AnsiStreamParser _ansi = new(new AnsiStreamParser.Options
        {
            DefaultColor = Colors.White,
            ParseBareBracketCodes = true
        });

        public MainPage()
        {
            InitializeComponent();
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (_isConnected) return;

            try
            {
                var loginPage = new LoginModalPage();

                loginPage.LoginCompleted += async (s, args) =>
                {
                    _username = args.Username;
                    _password = args.Password;

                    try
                    {
                        var (host, port, sessionKey) = await _client.FullSgeLoginAsync(_username, _password);

                        _ansi.Reset(); // important: reset styles per session

                        await _client.ConnectToGameAsync(host, port, sessionKey, OnGameOutputReceived);

                        _isConnected = true;
                        await AppendToStoryAsync(">>> Connected to game server.");
                    }
                    catch (Exception ex)
                    {
                        await AppendToStoryAsync($"ERROR: {ex.Message}");
                    }
                };

                await Navigation.PushModalAsync(loginPage);
            }
            catch (Exception ex)
            {
                await AppendToStoryAsync($"ERROR: {ex.Message}");
            }
        }

        private async void OnCommandEntered(object sender, EventArgs e)
        {
            var command = CommandEntry.Text?.Trim();
            CommandEntry.Text = string.Empty;
            CommandEntry.Focus();

            if (string.IsNullOrWhiteSpace(command)) return;
            if (_isSendingCommand) return;

            _isSendingCommand = true;
            try
            {
                await AppendToStoryAsync($"> {command}");
                await _client.SendCommandAsync(command);
            }
            catch (Exception ex)
            {
                await AppendToStoryAsync($"ERROR sending '{command}': {ex.Message}");
            }
            finally
            {
                _isSendingCommand = false;
            }
        }

        private async void OnLichClickedAsync(object sender, EventArgs e)
        {
            await AppendToStoryAsync("Lich integration not implemented yet.");
        }

        // These MUST exist because your XAML references them
        private void OnStoryScrollScrolled(object sender, ScrolledEventArgs e) { }
        private void OnThoughtsScrollScrolled(object sender, ScrolledEventArgs e) { }
        private void OnSpeechScrollScrolled(object sender, ScrolledEventArgs e) { }

        private void OnGameOutputReceived(string rawLine)
        {
            if (rawLine is null) return;

            Dispatcher.Dispatch(async () =>
            {
                try
                {
                    var line = rawLine.TrimEnd('\r', '\n');
                    await RouteLineToPaneAsync(line);
                }
                catch (Exception ex)
                {
                    await AppendToStoryAsync($"[UI error] {ex}");
                }
            });
        }

        private async Task RouteLineToPaneAsync(string lineWithAnsi)
        {
            // Strip ONLY for routing decisions
            var plain = _ansi.StripSgr(lineWithAnsi);

            if (plain.Length == 0)
            {
                await AppendToStoryAsync(lineWithAnsi);
                return;
            }

            if (plain.Trim() == ">")
            {
                _pendingThoughtHeader = null;
                await AppendToStoryAsync(lineWithAnsi);
                return;
            }

            if (_pendingThoughtHeader is not null)
            {
                var (chan, speaker) = _pendingThoughtHeader.Value;
                _pendingThoughtHeader = null;

                // Keep original ANSI content for rendering
                await AppendToThoughtsAsync($"[{chan}] {speaker}: {lineWithAnsi}");
                return;
            }

            var m = BracketChannelLine.Match(plain);
            if (m.Success)
            {
                var msgPlain = m.Groups["msg"].Value;

                var header = ThoughtHeader.Match(msgPlain.Trim());
                if (header.Success)
                {
                    _pendingThoughtHeader = (
                        header.Groups["chan"].Value.Trim(),
                        header.Groups["name"].Value.Trim()
                    );
                    return;
                }

                await AppendToThoughtsAsync(lineWithAnsi);
                return;
            }

            var th = ThoughtHeader.Match(plain.Trim());
            if (th.Success)
            {
                _pendingThoughtHeader = (
                    th.Groups["chan"].Value.Trim(),
                    th.Groups["name"].Value.Trim()
                );
                return;
            }

            if (plain.Contains(" says", StringComparison.OrdinalIgnoreCase) ||
                plain.Contains(" asks", StringComparison.OrdinalIgnoreCase))
            {
                await AppendToSpeechAsync(lineWithAnsi);
                return;
            }

            await AppendToStoryAsync(lineWithAnsi);
        }

        private Task AppendToStoryAsync(string line) => AppendLineAsync(StoryOutputStack, line);
        private Task AppendToThoughtsAsync(string line) => AppendLineAsync(ThoughtsOutputStack, line);
        private Task AppendToSpeechAsync(string line) => AppendLineAsync(SpeechOutputStack, line);

        private Task AppendLineAsync(VerticalStackLayout stack, string line)
        {
            if (stack is null) return Task.CompletedTask;

            line ??= string.Empty;

            var formatted = _ansi.ParseToFormattedString(line);

            // If only SGR changes, don't create empty labels
            if (formatted.Spans.Count == 0 || formatted.Spans.All(s => string.IsNullOrWhiteSpace(s.Text)))
                return Task.CompletedTask;

            stack.Children.Add(new Label
            {
                FormattedText = formatted,
                FontFamily = "Courier New",
                FontSize = 14
            });

            if (stack.Children.Count > MaxLinesPerPane)
                stack.Children.RemoveAt(0);

            return Task.CompletedTask;
        }
    }
}
