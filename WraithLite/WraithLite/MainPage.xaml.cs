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

        // Flags to track if user is scrolled to bottom in each ScrollView (for auto-scroll logic)
        private bool _storyAutoScroll = true;
        private bool _thoughtsAutoScroll = true;
        private bool _speechAutoScroll = true;

        // Routing regexes must run on stripped text
        private static readonly Regex BracketChannelLine =
            new Regex(@"^\[(?<chan>[^\]]+)\]\s+(?<name>[^:]+):\s+(?<msg>.*)$", RegexOptions.Compiled);
        private static readonly Regex ThoughtHeader =
            new Regex(@"^You hear the (?<chan>.+?) thoughts of (?<name>.+?) echo in your mind:$",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private (string Channel, string Speaker)? _pendingThoughtHeader;
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
                        _ansi.Reset(); // reset styles per session
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

            if (string.IsNullOrWhiteSpace(command) || _isSendingCommand) return;
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

        // ScrollView Scrolled event handlers – update flags if user is at bottom
        private void OnStoryScrollScrolled(object sender, ScrolledEventArgs e)
        {
            double contentHeight = StoryOutputStack.Height;
            double scrollViewHeight = StoryScroll.Height;
            // If content fits or scrolled to bottom (within ~20px), consider at bottom
            if (contentHeight <= scrollViewHeight || e.ScrollY >= contentHeight - scrollViewHeight - 20)
                _storyAutoScroll = true;
            else
                _storyAutoScroll = false;
        }

        private void OnThoughtsScrollScrolled(object sender, ScrolledEventArgs e)
        {
            double contentHeight = ThoughtsOutputStack.Height;
            double scrollViewHeight = ThoughtsScroll.Height;
            if (contentHeight <= scrollViewHeight || e.ScrollY >= contentHeight - scrollViewHeight - 20)
                _thoughtsAutoScroll = true;
            else
                _thoughtsAutoScroll = false;
        }

        private void OnSpeechScrollScrolled(object sender, ScrolledEventArgs e)
        {
            double contentHeight = SpeechOutputStack.Height;
            double scrollViewHeight = SpeechScroll.Height;
            if (contentHeight <= scrollViewHeight || e.ScrollY >= contentHeight - scrollViewHeight - 20)
                _speechAutoScroll = true;
            else
                _speechAutoScroll = false;
        }

        private void OnGameOutputReceived(string rawLine)
        {
            if (rawLine is null) return;
            // Dispatch to the UI thread
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
            // Strip ANSI for routing decisions (but use original ANSI for output)
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
                // Prepend channel and speaker to the thought line
                await AppendToThoughtsAsync($"[{chan}] {speaker}: {lineWithAnsi}");
                return;
            }
            var m = BracketChannelLine.Match(plain);
            if (m.Success)
            {
                var msgPlain = m.Groups["msg"].Value;
                var headerMatch = ThoughtHeader.Match(msgPlain.Trim());
                if (headerMatch.Success)
                {
                    _pendingThoughtHeader = (
                        headerMatch.Groups["chan"].Value.Trim(),
                        headerMatch.Groups["name"].Value.Trim()
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
            // Default pane
            await AppendToStoryAsync(lineWithAnsi);
        }

        private async Task AppendToStoryAsync(string line)
        {
            await AppendLineAsync(StoryOutputStack, line);
            if (_storyAutoScroll)
            {
                // Wait for layout to update, then scroll to the last element (bottom)
                await Task.Delay(10);
                var last = StoryOutputStack.Children.LastOrDefault();
                if (last != null)
                {
                    await StoryScroll.ScrollToAsync((Element)last, ScrollToPosition.End, false);
                }
            }
        }

        private async Task AppendToThoughtsAsync(string line)
        {
            await AppendLineAsync(ThoughtsOutputStack, line);
            if (_thoughtsAutoScroll)
            {
                await Task.Delay(10);
                var last = ThoughtsOutputStack.Children.LastOrDefault();
                if (last != null)
                {
                    await ThoughtsScroll.ScrollToAsync((Element)last, ScrollToPosition.End, false);
                }
            }
        }

        private async Task AppendToSpeechAsync(string line)
        {
            await AppendLineAsync(SpeechOutputStack, line);
            if (_speechAutoScroll)
            {
                await Task.Delay(10);
                var last = SpeechOutputStack.Children.LastOrDefault();
                if (last != null)
                {
                    await SpeechScroll.ScrollToAsync((Element)last, ScrollToPosition.End, false);
                }
            }
        }

        private Task AppendLineAsync(VerticalStackLayout stack, string line)
        {
            if (stack == null) return Task.CompletedTask;
            line ??= string.Empty;
            var formatted = _ansi.ParseToFormattedString(line);
            // If line is only ANSI codes with no visible text, skip adding
            if (formatted.Spans.Count == 0 || formatted.Spans.All(s => string.IsNullOrWhiteSpace(s.Text)))
                return Task.CompletedTask;

            // Create a new Label for the line and add to the stack
            stack.Children.Add(new Label
            {
                FormattedText = formatted,
                FontFamily = "Courier New",
                FontSize = 14
            });
            // Trim top if exceeding max lines to avoid infinite growth
            if (stack.Children.Count > MaxLinesPerPane)
            {
                stack.Children.RemoveAt(0);
            }
            return Task.CompletedTask;
        }
    }
}
