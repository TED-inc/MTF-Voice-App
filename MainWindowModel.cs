using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.CoreAudioApi;

namespace MTFVoiceTools;

public class MainWindowModel : INotifyPropertyChanged
{
    public ObservableCollection<MMDevice> Devices { get; } = new();
    private MMDevice? _selectedDevice;
    public MMDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice != value)
            {
                _selectedDevice = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<int> Formants { get; } = new ([4, 5, 6, 7] );
    private int _selectedFormant = 7;
    public int SelectedFormant
    {
        get => _selectedFormant;
        set
        {
            if (_selectedFormant != value)
            {
                _selectedFormant = value;
                OnPropertyChanged();
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}