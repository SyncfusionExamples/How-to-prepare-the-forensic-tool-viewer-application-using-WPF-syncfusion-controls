using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ForensicViewer.Infrastructure
{
    /// <summary>Base class for ViewModels: INotifyPropertyChanged plumbing.</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Sets the field and raises PropertyChanged; returns false when unchanged.</summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
