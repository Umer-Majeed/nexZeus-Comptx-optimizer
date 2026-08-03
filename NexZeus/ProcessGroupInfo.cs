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
            set { _isExpanded = value; OnChanged(nameof(IsExpanded)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnChanged(nameof(IsSelected));
                // User toggled checkbox manually -> record intent for next Apply
                SelectedAction = value ? "Suspend" : "Resume";
            }
        }

        private string _selectedAction = "None";
        public string SelectedAction
        {
            get => _selectedAction;
            set { _selectedAction = value; OnChanged(nameof(SelectedAction)); }
        }

        /// <summary>
        /// Syncs the checkbox (IsSelected) to reflect the REAL current suspended state
        /// of this group's processes, without triggering a pending Suspend/Resume intent.
        /// Call this after every refresh so the UI never lies about actual process state.
        /// </summary>
        public void SyncSuspendedState()
        {
            bool allSuspended = Instances.Count > 0 && Instances.All(p => p.IsSuspended);

            _isSelected = allSuspended;
            OnChanged(nameof(IsSelected));

            _selectedAction = "None";
            OnChanged(nameof(SelectedAction));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}