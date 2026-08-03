using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace NexZeus
{
    public class ProcessGroupInfo : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "Background Processes";
        public ObservableCollection<ProcessInfo> Instances { get; set; } = [];

        public double TotalRamMB => Instances.Sum(p => p.RamMB);
        public int InstanceCount => Instances.Count;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                SelectedAction = value ? "Suspend" : "Resume";
            }
        }

        private string _selectedAction = "None";
        public string SelectedAction
        {
            get => _selectedAction;
            set { _selectedAction = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedAction))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}