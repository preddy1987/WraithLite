using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;   // for MainThread
using WraithLite.Services;
using Process = System.Diagnostics.Process;

namespace WraithLite
{
    public partial class MainPage : ContentPage
    {
        readonly GameClient _client = new GameClient();
        Process _lichProcess;
        bool _lichRunning;

        public MainPage()
        {
            InitializeComponent();
        }

        // Called when the user taps "Connect"
        async void OnConnectClicked(object sender, EventArgs e)
        {
            ConnectButton.IsEnabled = false;
            LichButton.IsEnabled = false;

            GameOutput.Text += "Connecting to SGE…\n";
            try
            {
                var (host, port, key) = await _client.FullSgeLoginAsync(
                    UsernameEntry.Text, PasswordEntry.Text,CharacterName.Text);

                GameOutput.Text += $"SGE host={host}, port={port}, key={key}\n";
                GameOutput.Text += "Connecting to game server…\n";

                await _client.ConnectToGameAsync(host, port, key, line =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                        GameOutput.Text += line + "\n");
                });

                GameOutput.Text += "Connected to game – streaming output below:\n";
            }
            catch (Exception ex)
            {
                GameOutput.Text += $"ERROR during SGE login: {ex.Message}\n";
            }
            finally
            {
                ConnectButton.IsEnabled = true;
                LichButton.IsEnabled = true;
            }
        }

        // Called when the user taps "Lich" or "Stop Lich"
        void OnLichClicked(object sender, EventArgs e)
        {
            if (!_lichRunning)
            {
                // start Lich5 headlessly
                ConnectButton.IsEnabled = false;
                LichButton.IsEnabled = false;

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ruby",  // or full path to ruby.exe
                        Arguments =
                          $"lich.rbw --client-mode --frontend=dumb --game=GS --login={UsernameEntry.Text} --password={PasswordEntry.Text}",
                        WorkingDirectory = @"C:\Users\pREDDY\Desktop\Lich5",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    _lichProcess = Process.Start(psi);
                    _lichProcess.EnableRaisingEvents = true;

                    // When Lich exits on its own
                    _lichProcess.Exited += (s, ev) =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _lichRunning = false;
                            LichButton.Text = "Lich";
                            GameOutput.Text += "[Lich] Process exited\n";
                            ConnectButton.IsEnabled = true;
                            LichButton.IsEnabled = true;
                        });
                    };

                    _lichProcess.OutputDataReceived += (s, ev) =>
                    {
                        if (!string.IsNullOrEmpty(ev.Data))
                            MainThread.BeginInvokeOnMainThread(() =>
                                GameOutput.Text += ev.Data + "\n");
                    };
                    _lichProcess.ErrorDataReceived += (s, ev) =>
                    {
                        if (!string.IsNullOrEmpty(ev.Data))
                            MainThread.BeginInvokeOnMainThread(() =>
                                GameOutput.Text += "[Lich ERR] " + ev.Data + "\n");
                    };

                    _lichProcess.BeginOutputReadLine();
                    _lichProcess.BeginErrorReadLine();

                    _lichRunning = true;
                    LichButton.Text = "Stop Lich";
                }
                catch (Exception ex)
                {
                    GameOutput.Text += $"[Error] Could not launch Lich: {ex.Message}\n";
                    ConnectButton.IsEnabled = true;
                }
                finally
                {
                    LichButton.IsEnabled = true;
                }
            }
            else
            {
                // stop Lich
                try { if (!_lichProcess.HasExited) _lichProcess.Kill(); }
                catch { /* ignore */ }
                _lichProcess.Dispose();
                _lichProcess = null;
                _lichRunning = false;
                LichButton.Text = "Lich";
            }
        }

        // Called when the user presses Enter in the command box
        async void OnCommandEntered(object sender, EventArgs e)
        {
            var cmd = CommandEntry.Text;
            CommandEntry.Text = "";

            if (_lichRunning && _lichProcess != null)
            {
                try
                {
                    await _lichProcess.StandardInput.WriteLineAsync(cmd);
                }
                catch (Exception ex)
                {
                    GameOutput.Text += $"[Error] Failed to send to Lich: {ex.Message}\n";
                }
            }
            else
            {
                try
                {
                    await _client.SendCommandAsync(cmd);
                }
                catch (Exception ex)
                {
                    GameOutput.Text += $"[Error] Failed to send command: {ex.Message}\n";
                }
            }
        }
    }
}
