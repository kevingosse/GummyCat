using Avalonia.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Diagnostics.NETCore.Client;

namespace GummyCat;

public partial class ProcessPickerDialog : Window
{
    public ProcessPickerDialog()
    {
        AddHandler(KeyDownEvent, OnPreviewKeyDown!, RoutingStrategies.Tunnel);

        DataContext = this;

        var processes = DiagnosticsClient.GetPublishedProcesses()
            .Select(pid =>
            {
                try
                {
                    return Process.GetProcessById(pid);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            });

        Processes = new(
            processes
                .Where(p => p is not null && p.Id != Environment.ProcessId)
                .Select(p => new Models.TargetProcess(p!))
                .OrderByDescending(p => p.StartTime));

        InitializeComponent();
    }

    public ObservableCollection<Models.TargetProcess> Processes { get; set; }

    private void GridProcesses_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (GridProcesses.SelectedItem is not null)
        {
            Close(GridProcesses.SelectedItem);
        }
    }

    private void ButtonCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ButtonAttach_Click(object? sender, RoutedEventArgs e)
    {
        Close(GridProcesses.SelectedItem);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;

            if (GridProcesses.SelectedItem is not null)
            {
                Close(GridProcesses.SelectedItem);
            }
        }
    }
}
