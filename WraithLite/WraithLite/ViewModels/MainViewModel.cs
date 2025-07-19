using System.ComponentModel;
using System.Runtime.CompilerServices;
using WraithLite.Services;

namespace WraithLite.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _output = "Starting Lich5...\n";
    private string _inputText;
    private readonly LichService _lich;

    public string Output
    {
        get => _output;
        set { _output = value; OnPropertyChanged(); }
    }

    public string InputText
    {
        get => _inputText;
        set { _inputText = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        _lich = new LichService(OnLichOutput);
        _lich.StartLich();
    }

    public void SendCommand()
    {
        _lich.Send(InputText);
        InputText = "";
    }

    private void OnLichOutput(string line)
    {
        Output += line + "\n";
    }

    public event PropertyChangedEventHandler PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}