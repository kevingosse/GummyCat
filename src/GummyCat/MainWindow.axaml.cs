using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GummyCat.Models;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Analysis;
using Microsoft.Diagnostics.Tracing.Analysis.GC;
using Microsoft.Diagnostics.Tracing.Parsers;
using Newtonsoft.Json;

namespace GummyCat;

public partial class MainWindow : Window
{
    private bool _showReservedMemory = true;
    private bool _showEmptyMemory = false;
    private bool _tiledView = true;
    private List<Frame> _frames = new();
    private Frame? _activeFrame;
    private bool _playing = true;
    private readonly DispatcherTimer _playTimer;
    private Task? _listenToProcessTask;
    private CancellationTokenSource _cts = new();

    public MainWindow()
    {
        InitializeComponent();

        _playTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _playTimer.Tick += PlayTimer_Tick;

        DataContext = this;

        foreach (var value in Enum.GetValues<Generation>())
        {
            var color = Region.GetColor(value);

            PanelLegend.Children.Add(new Rectangle
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(color),
                Margin = new Thickness(15, 5, 5, 5)
            });

            PanelLegend.Children.Add(new TextBlock
            {
                Text = value.ToString(),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        PanelLegend.Children.Add(new Rectangle
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Colors.LightGray),
            Margin = new Thickness(15, 5, 5, 5)
        });

        PanelLegend.Children.Add(new TextBlock
        {
            Text = "Empty",
            VerticalAlignment = VerticalAlignment.Center
        });

        PanelLegend.Children.Add(new TextBlock
        {
            Text = "#",
            VerticalAlignment = VerticalAlignment.Center,
            Height = 20,
            Margin = new Thickness(15, 5, 5, 5),
            FontWeight = FontWeight.Bold,
            FontSize = 16
        });

        PanelLegend.Children.Add(new TextBlock
        {
            Text = "Heap index",
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    public ObservableCollection<Gc> GCs { get; } = new();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();

        if (args.Length > 1)
        {
            if (int.TryParse(args[1], out var pid))
            {
                AttachToProcess(pid, _cts.Token);
            }
            else
            {
                Load(args[1]);
            }
        }

        _playTimer.Start();
    }

    private void AttachToProcess(int pid, CancellationToken cancellationToken)
    {
        TextTarget.Text = $"Process: {pid}";
        _listenToProcessTask = Task.Factory.StartNew(() => Listen(pid, cancellationToken), TaskCreationOptions.LongRunning).Unwrap();
    }

    private void Load(string fileName)
    {
        TextTarget.Text = $"Trace: {System.IO.Path.GetFileName(fileName)}";

        var json = File.ReadAllText(fileName);

        var session = JsonConvert.DeserializeObject<Session>(json)!;

        _frames = session.Frames.ToList();

        foreach (var gc in session.GCs)
        {
            GCs.Add(gc);
        }

        if (_frames.Count == 0)
        {
            SliderFrames.Minimum = 0;
            SliderFrames.Value = 0;
            SliderFrames.Maximum = 0;
            return;
        }

        SliderFrames.Minimum = 1;
        SliderFrames.Maximum = _frames.Count;

        if (SliderFrames.Value == 1)
        {
            RefreshView(_frames[0]);
        }
        else
        {
            SliderFrames.Value = 1;
        }
    }

    private Task Listen(int pid, CancellationToken cancellationToken)
    {
        var mutex = new ManualResetEventSlim(true);

        var inspectProcessTask = Task.Factory.StartNew(() => InspectProcess(pid, mutex, cancellationToken), TaskCreationOptions.LongRunning);

        var session = CreateSession(pid);
        var source = new EventPipeEventSource(session.EventStream);

        source.NeedLoadedDotNetRuntimes();
        source.AddCallbackOnProcessStart(process =>
        {
            process.AddCallbackOnDotNetRuntimeLoad(runtime =>
            {
                runtime.GCEnd += (p, gc) => GCEnd(p, gc, mutex, cancellationToken);
            });
        });

        cancellationToken.Register(() =>
        {
            source.Dispose();
            session.Dispose();
        });

        source.Process();

        return inspectProcessTask;
    }

    private void GCEnd(TraceProcess process, TraceGC gc, ManualResetEventSlim mutex, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            GCs.Add(new(gc));
            //GCs.Insert(0, new(gc));
        },
        DispatcherPriority.Default,
        cancellationToken);

        mutex.Set();
    }

    private static EventPipeSession CreateSession(int pid)
    {
        var provider = new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.GC);

        var client = new DiagnosticsClient(pid);
        var session = client.StartEventPipeSession(provider);

        return session;
    }

    private void InspectMemoryDump(string path)
    {
        using var dataTarget = DataTarget.LoadDump(path);

        var runtime = dataTarget.ClrVersions[0].CreateRuntime();

        if (!runtime.Heap.CanWalkHeap)
        {
            return;
        }

        var subHeaps = runtime.Heap.SubHeaps.Select(s => new SubHeap(s)).ToList();

        var frame = new Frame
        {
            PrivateMemoryMb = 0,
            SubHeaps = subHeaps,
            GcNumber = GCs.Count > 0 ? GCs[0].Number : -1
        };

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            TextTarget.Text = $"Dump: {System.IO.Path.GetFileName(path)}";
            AddFrame(frame);
        });
    }

    private void InspectProcess(int pid, ManualResetEventSlim mutex, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                mutex.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            mutex.Reset();

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            using var dataTarget = DataTarget.AttachToProcess(pid, true);

            var runtime = dataTarget.ClrVersions[0].CreateRuntime();

            if (!runtime.Heap.CanWalkHeap)
            {
                continue;
            }

            using var process = Process.GetProcessById(pid);
            var privateMemoryMb = process.PrivateMemorySize64 / (1024.0 * 1024);
            var subHeaps = runtime.Heap.SubHeaps.Select(s => new SubHeap(s)).ToList();

            var frame = new Frame
            {
                PrivateMemoryMb = privateMemoryMb,
                SubHeaps = subHeaps,
                GcNumber = GCs.Count > 0 ? GCs.Last().Number : -1
            };

            Dispatcher.UIThread.InvokeAsync(() => AddFrame(frame), DispatcherPriority.Default, cancellationToken);
        }
    }

    private async Task ClearAll()
    {
        await _cts.CancelAsync();
        _cts = new();

        if (_listenToProcessTask != null)
        {
            await _listenToProcessTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        _frames.Clear();
        GCs.Clear();
        RegionsGrid.SetRegions(new List<SubHeap>());
        PanelRegions.Children.Clear();
        SliderFrames.Minimum = 0;
        SliderFrames.Value = 0;
        SliderFrames.Maximum = 0;
    }

    private void AddFrame(Frame frame)
    {
        _frames.Add(frame);

        if (SliderFrames.Value == SliderFrames.Maximum && _playing)
        {
            SliderFrames.Maximum++;
            SliderFrames.Value++;
        }
        else
        {
            SliderFrames.Maximum++;
        }

        SliderFrames.Minimum = 1;

        TextStep.Text = $"{(int)SliderFrames.Value} / {_frames.Count}";
    }

    private void RefreshView(Frame? frame = null)
    {
        frame ??= _activeFrame;

        if (frame == null)
        {
            return;
        }

        _activeFrame = frame;

        TextStep.Text = $"{(int)SliderFrames.Value} / {_frames.Count}";

        if (_activeFrame.GcNumber != -1)
        {
            ListGc.SelectedItem = GCs.FirstOrDefault(gc => gc.Number == _activeFrame.GcNumber);

            if (ListGc.SelectedItem != null)
            {
                ListGc.ScrollIntoView(ListGc.SelectedItem);
            }
        }
        else
        {
            ListGc.SelectedItem = null;
        }

        var subHeaps = frame.SubHeaps;
        var privateMemoryMb = frame.PrivateMemoryMb;

        TextNbHeaps.Text = subHeaps.Count.ToString();

        TextPrivateBytes.Text = $"{(long)privateMemoryMb} MB";

        if (_tiledView)
        {
            PanelRegions.Children.Clear();
            PanelRegions.IsVisible = false;
            RegionsGrid.IsVisible = true;
            RegionsGrid.SetRegions(subHeaps.ToList());
        }
        else
        {
            PanelRegions.IsVisible = true;
            RegionsGrid.IsVisible = false;

            var regions = PanelRegions.Children.OfType<Region>().ToList();

            foreach (var region in regions)
            {
                // First, mark all regions for deletion
                region.Tag = this;
            }

            // Then, remove the placeholders
            var placeholders = PanelRegions.Children.OfType<Grid>().ToList();
            PanelRegions.Children.RemoveAll(placeholders);

            // Now update existing regions and create new ones
            foreach (var heap in subHeaps)
            {
                foreach (var segment in heap.Segments.OrderBy(s => s.Start))
                {
                    if (segment.Flags.HasFlag((ClrSegmentFlags)32))
                    {
                        Debug.WriteLine($"Skipping decommitted region - {segment.Address:x2}");
                        continue;
                    }

                    //if (segment.Generation == Generation.Frozen)
                    //{
                    //    continue;
                    //}

                    var region = regions.FirstOrDefault(r => r.Address == segment.Start);

                    if (region == null)
                    {
                        region = new Region(segment, heap.Index, _showReservedMemory);

                        // Find where to insert it
                        int i = 0;

                        while (i < PanelRegions.Children.Count)
                        {
                            var existingRegion = (Region)PanelRegions.Children[i];

                            if (existingRegion.Address > region.Address)
                            {
                                break;
                            }

                            i++;
                        }

                        PanelRegions.Children.Insert(i, region);
                    }
                    else
                    {
                        region.Update(segment, heap.Index, _showReservedMemory);
                        region.Tag = null;
                    }
                }
            }
            
            // Remove regions that weren't marked
            PanelRegions.Children.RemoveAll(regions.Where(r => r.Tag != null));

            if (_showEmptyMemory)
            {
                // Add the placeholders
                ulong lastRegionEnd = 0;

                for (int i = 0; i < PanelRegions.Children.Count; i++)  
                {
                    var region = PanelRegions.Children[i] as Region;

                    if (region == null)
                    {
                        continue;
                    }

                    var start = region.Segment.Start;
                    var end = region.Segment.ReservedMemory.End;

                    if (lastRegionEnd != 0)
                    {
                        var diff = start - lastRegionEnd;
                        var diffInMB = ToMB(diff);

                        if (diffInMB > 0)
                        {
                            var placeholder = new Grid { Width = 120, Height = 40 };

                            placeholder.Children.Add(new Rectangle { Fill = new SolidColorBrush(Colors.LightGray), });

                            placeholder.Children.Add(new TextBlock
                            {
                                HorizontalAlignment = diffInMB < 10 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                Text = $"{diffInMB} MB",
                                FontSize = 14,
                                FontWeight = FontWeight.Bold,
                                Margin = diffInMB < 10 ? new Thickness(0) : new Thickness(5)
                            });

                            PanelRegions.Children.Insert(i + 1, placeholder);
                        }
                    }

                    lastRegionEnd = end;
                }
            }
        }
    }

    private static double ToMB(ulong length)
    {
        return Math.Round(length / (1024.0 * 1024), 2);
    }

    private void TogglePlaying(bool? newValue = null)
    {
        if (newValue == null)
        {
            newValue = !_playing;
        }

        _playing = newValue.Value;

        if (_playing)
        {
            ButtonPlay.Content = "⏸️";
            ButtonPlay.Foreground = new SolidColorBrush(Colors.DarkBlue);
            _playTimer.Start();
        }
        else
        {
            ButtonPlay.Content = "▶️";
            ButtonPlay.Foreground = new SolidColorBrush(Colors.DarkGreen);
            _playTimer.Stop();
        }
    }

    private void RadioReserved_Checked(object sender, RoutedEventArgs e)
    {
        _showReservedMemory = true;
        RefreshView();
    }

    private void RadioCommitted_Checked(object sender, RoutedEventArgs e)
    {
        _showReservedMemory = false;
        RefreshView();
    }

    private void RadioReal_Checked(object sender, RoutedEventArgs e)
    {
        _tiledView = true;
        PanelLogical.IsVisible = false;
        RefreshView();
    }

    private void RadioLogical_Checked(object sender, RoutedEventArgs e)
    {
        _tiledView = false;
        PanelLogical.IsVisible = true;
        RefreshView();
    }

    private void ToggleEmpty_Click(object sender, RoutedEventArgs e)
    {
        _showEmptyMemory = ToggleEmpty.IsChecked == true;
        RefreshView();
    }

    private void SliderFrames_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_frames.Count == 0)
        {
            return;
        }

        RefreshView(_frames[(int)e.NewValue - 1]);
    }

    private void ButtonPlay_Click(object sender, RoutedEventArgs e)
    {
        TogglePlaying();
    }

    private async void MenuOpen_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });

        if (files.Count == 0)
        {
            return;
        }

        await ClearAll();
        Load(files[0].TryGetLocalPath()!);
    }

    private void MenuQuit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void MenuSave_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            DefaultExtension = ".json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });

        if (file == null)
        {
            return;
        }

        var session = new Session { Frames = _frames, GCs = GCs };

        var json = JsonConvert.SerializeObject(session);

        await File.WriteAllTextAsync(file.TryGetLocalPath()!, json);
    }

    private async void MenuAttach_Click(object? sender, RoutedEventArgs e)
    {
        var target = await new ProcessPickerDialog().ShowDialog<TargetProcess?>(this);

        if (target != null)
        {
            await ClearAll();
            AttachToProcess(target.Pid, _cts.Token);
        }
    }

    private async void MenuDump_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Memory dumps") { Patterns = new[] { "*.dmp" } },
                new FilePickerFileType("Any file") { Patterns = new[] { "*" } },
            }
        });

        if (files.Count == 0)
        {
            return;
        }

        var file = files[0].TryGetLocalPath()!;
        await ClearAll();
        InspectMemoryDump(file);
    }

    private void PlayTimer_Tick(object? sender, EventArgs e)
    {
        if (SliderFrames.Value < SliderFrames.Maximum)
        {
            SliderFrames.Value++;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right)
        {
            if (SliderFrames.Value < SliderFrames.Maximum)
            {
                SliderFrames.Value++;
            }
        }
        else if (e.Key == Key.Left)
        {
            if (SliderFrames.Value > SliderFrames.Minimum)
            {
                SliderFrames.Value--;
            }
        }
        else if (e.Key == Key.Space)
        {
            TogglePlaying();
        }
    }
}