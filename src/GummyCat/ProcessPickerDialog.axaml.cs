using Avalonia.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GummyCat;

public partial class ProcessPickerDialog : Window
{
    public ProcessPickerDialog()
    {
        AddHandler(KeyDownEvent, OnPreviewKeyDown!, RoutingStrategies.Tunnel);

        DataContext = this;

        var processes = Process.GetProcesses();

        foreach (var process in processes.OrderByDescending(GetProcessStartTime))
        {
            Processes.Add(new Models.TargetProcess
            {
                Name = process.ProcessName,
                Pid = process.Id
            });
        }

        InitializeComponent();
    }

    public ObservableCollection<Models.TargetProcess> Processes { get; set; } = new();

    private DateTime? GetProcessStartTime(Process process)
    {
        try
        {            
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

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
