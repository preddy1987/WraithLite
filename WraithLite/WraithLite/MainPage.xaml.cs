using Microsoft.Maui.Controls;
using System;
using System.Threading.Tasks;
using WraithLite.Services;

namespace WraithLite
{
    /// <summary>
    /// Main game screen for WraithLite.
    ///
    /// This page is responsible for:
    /// - Running the login flow (SGE login + sessionKey retrieval)
    /// - Establishing a game socket connection
    /// - Rendering incoming game text into three output panes:
    ///     * Story (primary stream)
    ///     * Thoughts (mental / system-like messages)
    ///     * Speech (dialog / say/ask messages)
    /// - Handling command input and sending it to the server
    ///
    /// IMPORTANT UI NOTE:
    /// In MAUI, ScrollView auto-scrolling can be tricky because content measurement occurs asynchronously.
    /// We use a 2-pass "scroll to bottom" strategy to reliably land at the true bottom after new lines arrive.
    /// </summary>
    public partial class MainPage : ContentPage
    {
        // -------------------------
        // Connection / Auth state
        // -------------------------

        /// <summary>
        /// Handles the underlying game connection + login sequence.
        /// </summary>
        private readonly GameClient _client = new();

        /// <summary>
        /// Tracks whether a game connection is active (prevents reconnect spam).
        /// </summary>
        private bool _isConnected;

        /// <summary>
        /// User credentials captured from the login modal.
        /// (You may later replace this with secure storage.)
        /// </summary>
        private string _username;
        private string _password;

        // -------------------------
        // Auto-scroll controllers
        // -------------------------

        /// <summary>
        /// Auto-scroll + scroll-lock state for the Story pane.
        /// </summary>
        private readonly AutoScroller _storyScroller = new();

        /// <summary>
        /// Auto-scroll + scroll-lock state for the Thoughts pane.
        /// </summary>
        private readonly AutoScroller _thoughtsScroller = new();

        /// <summary>
        /// Auto-scroll + scroll-lock state for the Speech pane.
        /// </summary>
        private readonly AutoScroller _speechScroller = new();

        // -------------------------
        // Output retention (optional but recommended)
        // -------------------------

        /// <summary>
        /// To keep performance stable over long sessions, cap the number of labels retained per pane.
        /// If you want "infinite scrollback", you can raise this, but MAUI will eventually slow down
        /// as the visual tree grows.
        /// </summary>
        private const int MaxLinesPerPane = 3000;

        /// <summary>
        /// Initializes the page and loads the XAML layout.
        /// </summary>
        public MainPage()
        {
            InitializeComponent();
        }

        // ==========================================================
        // UI Event Handlers (wired from XAML)
        // ==========================================================

        /// <summary>
        /// "Play" button handler.
        ///
        /// Shows the login modal, performs SGE login, then connects to the game server.
        /// On success, incoming text is routed through <see cref="OnGameOutputReceived"/>.
        /// </summary>
        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (_isConnected)
                return;

            try
            {
                // Show login modal and await credentials via event callback.
                // (This pattern keeps your existing UI flow unchanged.)
                var loginPage = new LoginModalPage();

                loginPage.LoginCompleted += async (s, args) =>
                {
                    _username = args.Username;
                    _password = args.Password;

                    try
                    {
                        // 1) SGE Login => host/port/sessionKey
                        var (host, port, sessionKey) = await _client.FullSgeLoginAsync(_username, _password);

                        // 2) Connect to the game socket and start receiving lines
                        await _client.ConnectToGameAsync(host, port, sessionKey, OnGameOutputReceived);

                        _isConnected = true;
                        await AppendToStoryAsync(">>> Connected to game server.");
                    }
                    catch (Exception ex)
                    {
                        // Render connection/login errors to Story so the user sees them immediately.
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

        /// <summary>
        /// "Send" / Entry Completed handler.
        ///
        /// Sends the typed command to the server, then echoes it locally to the Story pane.
        /// (Echoing locally creates the familiar MUD-client feel.)
        /// </summary>
        private async void OnCommandEntered(object sender, EventArgs e)
        {
            var command = CommandEntry.Text;

            if (string.IsNullOrWhiteSpace(command))
                return;

            try
            {
                await _client.SendCommandAsync(command);

                // Echo the command in the Story pane (common MUD UX).
                await AppendToStoryAsync($"> {command}");
            }
            catch (Exception ex)
            {
                await AppendToStoryAsync($"ERROR: {ex.Message}");
            }
            finally
            {
                // Clear the input for the next command.
                CommandEntry.Text = string.Empty;
            }
        }

        /// <summary>
        /// "Lich" button handler.
        ///
        /// Placeholder for future Lich integration / script control.
        /// </summary>
        private async void OnLichClickedAsync(object sender, EventArgs e)
        {
            await AppendToStoryAsync("Lich integration not implemented yet.");
        }

        /// <summary>
        /// Scrolled event for the Story ScrollView.
        ///
        /// Purpose:
        /// - Enables "scroll lock": if the user scrolls up, auto-follow stops.
        /// - If the user scrolls back near the bottom, auto-follow re-enables.
        ///
        /// We ignore scroll events caused by our own programmatic auto-scroll to avoid feedback loops.
        /// </summary>
        private void OnStoryScrollScrolled(object sender, ScrolledEventArgs e)
        {
            if (_storyScroller.IsProgrammaticScroll)
                return;

            if (sender is ScrollView sv)
                _storyScroller.UpdateAutoScrollFlagFromUserScroll(sv, e.ScrollY);
        }

        /// <summary>
        /// Scrolled event for the Thoughts ScrollView (same behavior as Story).
        /// </summary>
        private void OnThoughtsScrollScrolled(object sender, ScrolledEventArgs e)
        {
            if (_thoughtsScroller.IsProgrammaticScroll)
                return;

            if (sender is ScrollView sv)
                _thoughtsScroller.UpdateAutoScrollFlagFromUserScroll(sv, e.ScrollY);
        }

        /// <summary>
        /// Scrolled event for the Speech ScrollView (same behavior as Story).
        /// </summary>
        private void OnSpeechScrollScrolled(object sender, ScrolledEventArgs e)
        {
            if (_speechScroller.IsProgrammaticScroll)
                return;

            if (sender is ScrollView sv)
                _speechScroller.UpdateAutoScrollFlagFromUserScroll(sv, e.ScrollY);
        }

        // ==========================================================
        // Game Output Pipeline
        // ==========================================================

        /// <summary>
        /// Callback invoked by the networking layer for each received output line.
        ///
        /// IMPORTANT:
        /// - This is often called on a background thread.
        /// - UI changes must occur on the UI thread.
        /// - We route the line into one of three panes based on simple heuristics.
        ///
        /// Future improvements:
        /// - Replace heuristics with richer parsing (e.g., StormFront tags, channel markers, ANSI, etc.)
        /// - Add batching to reduce UI churn during heavy output bursts.
        /// </summary>
        private void OnGameOutputReceived(string line)
        {
            Dispatcher.Dispatch(async () =>
            {
                // Very simple routing logic for now:
                if (line.Contains("thoughtfully", StringComparison.OrdinalIgnoreCase))
                {
                    await AppendToThoughtsAsync(line);
                }
                else if (line.Contains("says", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("asks", StringComparison.OrdinalIgnoreCase))
                {
                    await AppendToSpeechAsync(line);
                }
                else
                {
                    await AppendToStoryAsync(line);
                }
            });
        }

        // ==========================================================
        // Output Append Helpers (Story / Thoughts / Speech)
        // ==========================================================

        /// <summary>
        /// Appends a single line to the Story pane, then auto-scrolls (if enabled).
        /// </summary>
        private Task AppendToStoryAsync(string line) =>
            AppendLineAsync(StoryOutputStack, StoryScroll, _storyScroller, line);

        /// <summary>
        /// Appends a single line to the Thoughts pane, then auto-scrolls (if enabled).
        /// </summary>
        private Task AppendToThoughtsAsync(string line) =>
            AppendLineAsync(ThoughtsOutputStack, ThoughtsScroll, _thoughtsScroller, line);

        /// <summary>
        /// Appends a single line to the Speech pane, then auto-scrolls (if enabled).
        /// </summary>
        private Task AppendToSpeechAsync(string line) =>
            AppendLineAsync(SpeechOutputStack, SpeechScroll, _speechScroller, line);

        /// <summary>
        /// Core append function shared by all panes.
        ///
        /// This method:
        /// - Creates a new Label for the incoming line
        /// - Adds it to the output stack
        /// - Trims old lines to avoid infinite UI growth
        /// - Auto-scrolls to bottom if "follow tail" is enabled
        ///
        /// DESIGN NOTE:
        /// This is intentionally simple and "MUD-client-like".
        /// Later, if you want very large scrollback, you can move to CollectionView virtualization.
        /// </summary>
        private async Task AppendLineAsync(
            VerticalStackLayout stack,
            ScrollView scroll,
            AutoScroller scroller,
            string line)
        {
            if (stack is null)
                return;

            // Create one UI element per line (simple, readable, and MUD-like).
            // If performance becomes a concern, we can later batch lines or virtualize with CollectionView.
            var label = new Label
            {
                Text = line,
                FontFamily = "Courier New",
                FontSize = 14,
                TextColor = Colors.White
            };

            stack.Children.Add(label);

            // Keep the visual tree from growing without limit.
            // This prevents slowdowns after long sessions.
            if (stack.Children.Count > MaxLinesPerPane)
            {
                // Remove from the top (oldest line) to keep recent output.
                stack.Children.RemoveAt(0);
            }

            // If the user is at the bottom (auto-scroll enabled), follow tail.
            if (scroller.AutoScrollEnabled && scroll is not null)
            {
                // Use the 2-pass scroll to reliably hit true bottom in MAUI.
                await scroller.ScrollToBottomTwoPassAsync(scroll, animated: false);
            }
        }

        // ==========================================================
        // AutoScroller (per-pane scroll + scroll-lock controller)
        // ==========================================================

        /// <summary>
        /// Encapsulates "follow tail" logic for a ScrollView:
        ///
        /// 1) Auto-scroll is enabled when the user is near the bottom.
        /// 2) Auto-scroll is disabled when the user scrolls up (scroll lock).
        /// 3) Programmatic scrolls are flagged so we can ignore Scrolled events caused by ourselves.
        ///
        /// MAUI QUIRK:
        /// ScrollView.ContentSize updates can lag behind child layout updates, so a single scroll attempt
        /// can land slightly above the true bottom. A 2-pass scroll is a reliable workaround.
        /// </summary>
        private sealed class AutoScroller
        {
            /// <summary>
            /// When true, we auto-scroll to bottom as new lines arrive.
            /// When false, the user has scrolled up and we respect their position (scroll lock).
            /// </summary>
            public bool AutoScrollEnabled { get; private set; } = true;

            private bool _isProgrammaticScroll;

            /// <summary>
            /// True while we are performing a programmatic scroll (so Scrolled events can ignore it).
            /// </summary>
            public bool IsProgrammaticScroll => _isProgrammaticScroll;

            /// <summary>
            /// Performs a 2-pass scroll-to-bottom operation.
            ///
            /// Why 2-pass?
            /// - MAUI often measures/layouts new content in stages.
            /// - After adding a Label, the ScrollView.ContentSize may not be updated immediately.
            /// - The first pass gets "close"; the second pass usually lands exactly at bottom.
            /// </summary>
            public async Task ScrollToBottomTwoPassAsync(ScrollView scrollView, bool animated = false)
            {
                if (scrollView is null)
                    return;

                _isProgrammaticScroll = true;
                try
                {
                    // Pass 1: yield control so MAUI can measure/layout the newly added child.
                    await Task.Yield();
                    var maxY1 = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
                    await scrollView.ScrollToAsync(0, maxY1, animated);

                    // Pass 2: after another tick, ContentSize is often updated again.
                    await Task.Yield();
                    var maxY2 = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);
                    await scrollView.ScrollToAsync(0, maxY2, animated);
                }
                finally
                {
                    // Small delay lets the scroll settle and prevents "scroll lock" toggling mid-scroll.
                    await Task.Delay(10);
                    _isProgrammaticScroll = false;
                }
            }

            /// <summary>
            /// Updates AutoScrollEnabled based on the user's scroll position.
            ///
            /// If the user scrolls up, AutoScrollEnabled becomes false.
            /// If the user scrolls back near the bottom, AutoScrollEnabled becomes true again.
            ///
            /// The threshold is intentionally forgiving to account for:
            /// - font height differences
            /// - rounding
            /// - platform-specific scroll physics
            /// </summary>
            public void UpdateAutoScrollFlagFromUserScroll(ScrollView scrollView, double scrollY, double threshold = 60)
            {
                if (scrollView is null)
                    return;

                var scrollSpace = Math.Max(0, scrollView.ContentSize.Height - scrollView.Height);

                // Enable auto-scroll if:
                // - the content fits inside the view (no scrolling needed), OR
                // - the user is near the bottom (within the threshold).
                AutoScrollEnabled = scrollSpace <= 0 || scrollY >= (scrollSpace - threshold);
            }
        }
    }
}
