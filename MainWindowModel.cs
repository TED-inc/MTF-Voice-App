using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.CoreAudioApi;

namespace MTFVoiceTools;

public class MainWindowModel : INotifyPropertyChanged
{
    public ObservableCollection<MMDevice> Items { get; } = new();
    private MMDevice? _selectedItem;
    public MMDevice? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem != value)
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}