using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace NexZeus
{
    public class ProcessGroupInfo : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;

        private string _category = "Background Processes";
        public string Category
        {
            get => _category;
            set { _category = value; OnChanged(nameof(Category)); }
        }

        public ObservableCollection<ProcessInfo> Instances { get; set; } = new();

        private int _count;
        public int Count
        {
            get => Instances.Count;
            set { _count = value; OnChanged(nameof(Count)); }
        }

        public double TotalRamMB => System.Math.Round(Instances.Sum(i => i.RamMB), 1);

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnChanged(nameof(IsExpanded)); }
        }

        private string _selectedAction = string.Empty;
        public string SelectedAction
        {
            get => _selectedAction;
            set
            {
                _selectedAction = value;
                OnChanged(nameof(SelectedAction));
                OnChanged(nameof(IsSuspendSelected));
                OnChanged(nameof(IsResumeSelected));
            }
        }

        public bool IsSuspendSelected => SelectedAction == "Suspend";
        public bool IsResumeSelected => SelectedAction == "Resume";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}