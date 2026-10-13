using System.Collections.Generic;
using System.Windows;
using ForensicViewer.Models;

namespace ForensicViewer.Services
{
    /// <summary>Abstracts dialogs so ViewModels stay UI-free and testable.</summary>
    public interface IDialogService
    {
        /// <summary>Shows a Save dialog; returns the chosen path or null when cancelled.</summary>
        string PromptSavePath(string filter, string defaultFileName);
        void ShowInfo(string title, string message);
        void ShowError(string title, string message);
    }

    /// <summary>Lets the ViewModel obtain chart images from the view without referencing controls.</summary>
    public interface IChartSnapshotProvider
    {
        IReadOnlyList<ChartSnapshot> CaptureSnapshots();
    }

    public sealed class DialogService : IDialogService
    {
        public string PromptSavePath(string filter, string defaultFileName)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = defaultFileName };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public void ShowInfo(string title, string message) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowError(string title, string message) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
