using System.Diagnostics;
using System.Text;

namespace WraithLite.Services;

public class LichService
{
    private Process _lichProcess;
    private Action<string> _onOutput;

    public LichService(Action<string> onOutput)
    {
        _onOutput = onOutput;
    }

    public void StartLich()
    {
        var lichPath = @"C:\Users\pREDDY\Desktop\Lich5\lich.rbw"; // Update to your actual Lich5 path
        var rubyExe = @"C:\Ruby4Lich5\bin\rubyw.exe"; // Update to your actual ruby path
        
        _lichProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = rubyExe,
                Arguments = $"\"{lichPath}\" --login=Aytum --client-mode --frontend=dumb --game=GS",
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _lichProcess.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                _onOutput?.Invoke(e.Data);
        };

        _lichProcess.Start();
        _lichProcess.BeginOutputReadLine();
    }

    public void Send(string text)
    {
        if (_lichProcess != null && !_lichProcess.HasExited)
        {
            _lichProcess.StandardInput.WriteLine(text);
        }
    }
}