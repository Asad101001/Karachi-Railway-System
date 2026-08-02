using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using KarachiRailway.Desktop.Playback;
using KarachiRailway.Simulation.Engine;
using KarachiRailway.Simulation.Models;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace KarachiRailway.Desktop.ViewModels;

/// <summary>Navigation tabs for the sidebar.</summary>
public enum AppTab { Home, Model, Metrics, Compare, Reports, Settings, About }

public enum SimulationState { Idle, Running, Paused, Completed }



/// <summary>
/// Main view model for the Karachi Railway Simulation desktop application.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private SimulationRunner?        _runner;
    private CancellationTokenSource? _cts;
    private SimulationState          _state = SimulationState.Idle;
    private SimulationResult?        _result;
    private Passenger?               _selectedPassenger;
    private bool                     _isCustomerModalOpen;

    private readonly PlaybackController _playback = new();
    private readonly Dictionary<int, string>                   _passengerCurrentNodes = new();
    private readonly Dictionary<int, PassengerTokenViewModel>  _tokenMap              = new();
    private readonly Dictionary<string, FlowNodeViewModel>     _nodeMap               = new();
    private const int MaxVisibleTokens = 15;

    public ObservableCollection<Passenger> ActivePassengers { get; } = new();


    public MainViewModel()
    {
        QueueModels = Enum.GetValues<QueueModelType>().ToList();

        SpeedOptions = new List<SpeedOption>
        {
            new("0.25x", 0.25),
            new("0.5x",  0.5),
            new("1x",    1.0),
            new("1.5x",  1.5),
            new("2x",    2.0),
        };
        _selectedSpeed = SpeedOptions[2];

        StartCommand  = new AsyncRelayCommand(StartSimulationAsync,
                            () => State is SimulationState.Idle or SimulationState.Completed);
        PauseCommand  = new RelayCommand(PausePlayback,  () => State == SimulationState.Running);
        ResumeCommand = new RelayCommand(ResumePlayback, () => State == SimulationState.Paused);
          StepCommand   = new RelayCommand(StepForwardPlayback,
                        () => State is SimulationState.Running or SimulationState.Paused &&
                            _playback.EventsDone < _playback.EventsTotal);
        StopCommand   = new RelayCommand(StopSimulation, () => State != SimulationState.Idle);
        ResetCommand  = new RelayCommand(Reset,          () => State != SimulationState.Idle);

        ToggleLeftPanelCommand  = new RelayCommand(() => ShowLeftPanel = !ShowLeftPanel);
        ToggleRightPanelCommand = new RelayCommand(() => ShowRightPanel = !ShowRightPanel);
        SelectModelCommand      = new RelayCommand(SelectModel);
        NavigateCommand         = new RelayCommand(p => NavigateTo((string)p!));
        
        ViewCustomerCommand = new RelayCommand(p => 
        {
            SelectedPassenger = (Passenger)p!;
            IsCustomerModalOpen = true;
        });
        CloseCustomerModalCommand = new RelayCommand(() => IsCustomerModalOpen = false);

        _playback.EventApplied      += OnEventApplied;
        _playback.PlaybackCompleted += OnPlaybackCompleted;

        BuildFlowDiagram();
    }

    public SimulationState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsCompleted));
                Application.Current.Dispatcher.Invoke(() => System.Windows.Input.CommandManager.InvalidateRequerySuggested());
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(CanEditParams));
            }
        }
    }

    public bool IsIdle        => State == SimulationState.Idle;
    public bool IsRunning     => State == SimulationState.Running;
    public bool IsPaused      => State == SimulationState.Paused;
    public bool IsCompleted   => State == SimulationState.Completed;
    public bool CanEditParams => State is SimulationState.Idle or SimulationState.Completed;

    public string StatusLabel => State switch
    {
        SimulationState.Idle      => "Ready",
        SimulationState.Running   => "Playing...",
        SimulationState.Paused    => "Paused",
        SimulationState.Completed => "Completed",
        _                         => "Unknown",
    };

    public string StatusColor => State switch
    {
        SimulationState.Running   => "#22C55E",
        SimulationState.Paused    => "#F59E0B",
        SimulationState.Completed => "#3B82F6",
        _                         => "#94A3B8",
    };

    public List<SpeedOption> SpeedOptions { get; }
    public List<QueueModelType> QueueModels { get; }

    private QueueModelType _selectedQueueModel = QueueModelType.MM1;
    public QueueModelType SelectedQueueModel
    {
        get => _selectedQueueModel;
        set
        {
            if (SetProperty(ref _selectedQueueModel, value))
            {
                OnPropertyChanged(nameof(IsMM1));
                OnPropertyChanged(nameof(IsMG1));
                OnPropertyChanged(nameof(IsGG1));
                OnPropertyChanged(nameof(QueueModelHeaderText));
                
                // Update table with dummy data to preview model behavior
                if (ModellingRows.Count > 0)
                {
                    BuildModellingTable(new SimulationResult { TotalArrived = 15 });
                }
            }
        }
    }

    public string QueueModelHeaderText =>
        $"{SelectedQueueModel switch
        {
            QueueModelType.MM1 => "M/M/1",
            QueueModelType.MG1 => "M/G/1",
            QueueModelType.GG1 => "G/G/1",
            _                  => "M/M/1",
        }} Queue Simulation  ·  Flow Diagram Mode";

    private bool _showModelSelection = true;
    public bool ShowModelSelection
    {
        get => _showModelSelection;
        set => SetProperty(ref _showModelSelection, value);
    }

    public bool IsMM1 => SelectedQueueModel == QueueModelType.MM1;
    public bool IsMG1 => SelectedQueueModel == QueueModelType.MG1;
    public bool IsGG1 => SelectedQueueModel == QueueModelType.GG1;

    public double DiagramCanvasWidth => 1320;
    public double DiagramCanvasHeight => 980;

    private double _diagramZoom = 0.84;
    public double DiagramZoom
    {
        get => _diagramZoom;
        set
        {
            if (SetProperty(ref _diagramZoom, Math.Clamp(value, 0.6, 1.25)))
                OnPropertyChanged(nameof(EffectiveDiagramZoom));
        }
    }

    private double _blockScale = 1.0;
    public double BlockScale
    {
        get => _blockScale;
        set
        {
            if (SetProperty(ref _blockScale, Math.Clamp(value, 0.7, 1.7)))
                OnPropertyChanged(nameof(EffectiveBlockScale));
        }
    }

    private bool _showLeftPanel = true;
    public bool ShowLeftPanel
    {
        get => _showLeftPanel;
        set
        {
            if (SetProperty(ref _showLeftPanel, value))
            {
                OnPropertyChanged(nameof(LeftPanelWidth));
                OnPropertyChanged(nameof(LeftPanelToggleLabel));
                OnPropertyChanged(nameof(AutoZoomFactor));
                OnPropertyChanged(nameof(EffectiveDiagramZoom));
                OnPropertyChanged(nameof(EffectiveBlockScale));
            }
        }
    }

    private bool _showRightPanel = true;
    public bool ShowRightPanel
    {
        get => _showRightPanel;
        set
        {
            if (SetProperty(ref _showRightPanel, value))
            {
                OnPropertyChanged(nameof(RightPanelWidth));
                OnPropertyChanged(nameof(RightPanelToggleLabel));
                OnPropertyChanged(nameof(AutoZoomFactor));
                OnPropertyChanged(nameof(EffectiveDiagramZoom));
                OnPropertyChanged(nameof(EffectiveBlockScale));
            }
        }
    }

    public GridLength LeftPanelWidth => ShowLeftPanel ? new GridLength(370) : new GridLength(0);
    public GridLength RightPanelWidth => ShowRightPanel ? new GridLength(370) : new GridLength(0);

    public string LeftPanelToggleLabel => ShowLeftPanel ? "Hide Settings" : "Show Settings";
    public string RightPanelToggleLabel => ShowRightPanel ? "Hide Metrics" : "Show Metrics";

    public double AutoZoomFactor =>
        (ShowLeftPanel, ShowRightPanel) switch
        {
            (true, true) => 0.84,
            (false, true) or (true, false) => 0.93,
            _ => 1.0,
        };

    public double EffectiveDiagramZoom => DiagramZoom * AutoZoomFactor;
    public double EffectiveBlockScale => BlockScale * AutoZoomFactor;

    // ── Sidebar navigation ───────────────────────────────────────────────────
    private AppTab _activeTab = AppTab.Home;
    public AppTab ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(IsTabHome));
                OnPropertyChanged(nameof(IsTabModel));
                OnPropertyChanged(nameof(IsTabMetrics));
                OnPropertyChanged(nameof(IsTabCompare));
                OnPropertyChanged(nameof(IsTabReports));
                OnPropertyChanged(nameof(IsTabSettings));
                OnPropertyChanged(nameof(IsTabAbout));
            }
        }
    }
    public bool IsTabHome     => ActiveTab == AppTab.Home;
    public bool IsTabModel    => ActiveTab == AppTab.Model;
    public bool IsTabMetrics  => ActiveTab == AppTab.Metrics;
    public bool IsTabCompare  => ActiveTab == AppTab.Compare;
    public bool IsTabReports  => ActiveTab == AppTab.Reports;
    public bool IsTabSettings => ActiveTab == AppTab.Settings;
    public bool IsTabAbout    => ActiveTab == AppTab.About;

    private void NavigateTo(object? param)
    {
        if (param is string s && Enum.TryParse<AppTab>(s, out var tab))
            ActiveTab = tab;
    }

    // ── Run History (Reports tab) ─────────────────────────────────────────────
    private int _runCounter = 0;
    public ObservableCollection<SimulationRunSummary> RunHistory { get; } = new();

    // ── Table Modeling ───────────────────────────────────────────────────────
    public ObservableCollection<ModellingRowViewModel> ModellingRows { get; } = new();

    // ── Gantt Chart ──────────────────────────────────────────────────────────
    public ObservableCollection<GanttItem> GanttItems { get; } = new();

    // ── Passenger History ────────────────────────────────────────────────────
    public ObservableCollection<Passenger> CompletedPassengers { get; } = new();

    private double _modellingAvgWait;
    public double ModellingAvgWait { get => _modellingAvgWait; private set => SetProperty(ref _modellingAvgWait, value); }
    private double _modellingAvgTurnaround;
    public double ModellingAvgTurnaround { get => _modellingAvgTurnaround; private set => SetProperty(ref _modellingAvgTurnaround, value); }
    private double _modellingAvgResponse;
    public double ModellingAvgResponse { get => _modellingAvgResponse; private set => SetProperty(ref _modellingAvgResponse, value); }
    private double _modellingAvgInterArrival;
    public double ModellingAvgInterArrival { get => _modellingAvgInterArrival; private set => SetProperty(ref _modellingAvgInterArrival, value); }

    public Passenger? SelectedPassenger
    {
        get => _selectedPassenger;
        set => SetProperty(ref _selectedPassenger, value);
    }

    public bool IsCustomerModalOpen
    {
        get => _isCustomerModalOpen;
        set => SetProperty(ref _isCustomerModalOpen, value);
    }

    private string _runNarrative = "Run a simulation to generate a report.";
    public string RunNarrative
    {
        get => _runNarrative;
        private set => SetProperty(ref _runNarrative, value);
    }

    // ── Metrics chart data (Compare tab & Metrics tab) ────────────────────────
    public ISeries[] WaitTimeTrend { get; } = new ISeries[]
    {
        new LineSeries<double>
        {
            Values = new ObservableCollection<double>(),
            Name = "Wait Time (Wq)",
            Stroke = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 3 },
            Fill = null,
            GeometrySize = 6,
            GeometryStroke = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 }
        }
    };

    public ISeries[] QueueLengthTrend { get; } = new ISeries[]
    {
        new LineSeries<double>
        {
            Values = new ObservableCollection<double>(),
            Name = "Queue Length (Lq)",
            Stroke = new SolidColorPaint(SKColors.Gold) { StrokeThickness = 3 },
            Fill = null,
            GeometrySize = 6,
            GeometryStroke = new SolidColorPaint(SKColors.Gold) { StrokeThickness = 2 }
        }
    };

    public ISeries[] CurrentRunWaitTimes { get; } = new ISeries[]
    {
        new LineSeries<double>
        {
            Values = new ObservableCollection<double>(),
            Name = "Wait Time",
            Stroke = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 3 },
            Fill = new SolidColorPaint(SKColors.MediumSeaGreen.WithAlpha(50)),
            GeometrySize = 4,
            GeometryStroke = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 }
        }
    };

    public ISeries[] CurrentRunTurnaroundTimes { get; } = new ISeries[]
    {
        new LineSeries<double>
        {
            Values = new ObservableCollection<double>(),
            Name = "Turnaround Time",
            Stroke = new SolidColorPaint(SKColors.Gold) { StrokeThickness = 3 },
            Fill = new SolidColorPaint(SKColors.Gold.WithAlpha(50)),
            GeometrySize = 4,
            GeometryStroke = new SolidColorPaint(SKColors.Gold) { StrokeThickness = 2 }
        }
    };

    public ISeries[] KpiRhoSeries { get; } = new ISeries[] { new LineSeries<double> { Values = new ObservableCollection<double>(), Stroke = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 }, Fill = null, GeometrySize = 0 } };
    public ISeries[] KpiWqSeries { get; } = new ISeries[] { new LineSeries<double> { Values = new ObservableCollection<double>(), Stroke = new SolidColorPaint(SKColors.Gold) { StrokeThickness = 2 }, Fill = null, GeometrySize = 0 } };
    public ISeries[] KpiWSeries { get; } = new ISeries[] { new LineSeries<double> { Values = new ObservableCollection<double>(), Stroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 2 }, Fill = null, GeometrySize = 0 } };
    public ISeries[] KpiLSeries { get; } = new ISeries[] { new LineSeries<double> { Values = new ObservableCollection<double>(), Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2 }, Fill = null, GeometrySize = 0 } };

    public Axis[] SparklineXAxes { get; } = new Axis[] { new Axis { IsVisible = false } };
    public Axis[] SparklineYAxes { get; } = new Axis[] { new Axis { IsVisible = false } };

    public ISeries[] ModelCompareWqSeries { get; } = new ISeries[]
    {
        new ColumnSeries<double>
        {
            Values = new ObservableCollection<double> { 0, 0, 0 },
            Name = "Wait Time (Wq)",
            Fill = new SolidColorPaint(SKColors.Gold),
            MaxBarWidth = 40
        }
    };

    public ISeries[] ModelCompareWSeries { get; } = new ISeries[]
    {
        new ColumnSeries<double>
        {
            Values = new ObservableCollection<double> { 0, 0, 0 },
            Name = "System Time (W)",
            Fill = new SolidColorPaint(SKColors.MediumSeaGreen),
            MaxBarWidth = 40
        }
    };

    public Axis[] ModelCompareXAxes { get; } = new Axis[] 
    { 
        new Axis 
        { 
            Labels = new ObservableCollection<string> { "M/M/1", "M/G/1", "G/G/1" },
            LabelsRotation = 0
        } 
    };

    public Axis[] XAxes { get; } = new Axis[] { new Axis { Labels = new ObservableCollection<string>() } };
    public Axis[] YAxes { get; } = new Axis[] { new Axis { Labeler = value => value.ToString("0.00") } };

    private SpeedOption _selectedSpeed;
    public SpeedOption SelectedSpeed
    {
        get => _selectedSpeed;
        set
        {
            if (SetProperty(ref _selectedSpeed, value) && value != null)
            {
                _playback.SpeedMultiplier = value.Value;
                PlaybackSpeed = value.Value;
            }
        }
    }

    private double _playbackSpeed = 1.0;
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (SetProperty(ref _playbackSpeed, value))
            {
                _playback.SpeedMultiplier = value;
            }
        }
    }

    private double _playbackProgress;
    public double PlaybackProgress
    {
        get => _playbackProgress;
        private set => SetProperty(ref _playbackProgress, value);
    }

    private double _playbackTotal = 1;
    public double PlaybackTotal
    {
        get => _playbackTotal;
        private set => SetProperty(ref _playbackTotal, value);
    }

    private double _arrivalRate = 8.0;
    public double ArrivalRate
    {
        get => _arrivalRate;
        set
        {
            if (SetProperty(ref _arrivalRate, value))
            {
                OnPropertyChanged(nameof(UtilizationPreview));
                OnPropertyChanged(nameof(IsStablePreview));
                OnPropertyChanged(nameof(StabilityHint));
            }
        }
    }

    private double _serviceRate = 10.0;
    public double ServiceRate
    {
        get => _serviceRate;
        set
        {
            if (SetProperty(ref _serviceRate, value))
            {
                OnPropertyChanged(nameof(UtilizationPreview));
                OnPropertyChanged(nameof(IsStablePreview));
                OnPropertyChanged(nameof(StabilityHint));
            }
        }
    }

    private double _serviceCv = 1.5;
    public double ServiceCv
    {
        get => _serviceCv;
        set => SetProperty(ref _serviceCv, value);
    }

    private double _arrivalCv = 0.8;
    public double ArrivalCv
    {
        get => _arrivalCv;
        set => SetProperty(ref _arrivalCv, value);
    }

    private int _durationMinutes = 120;
    public int DurationMinutes
    {
        get => _durationMinutes;
        set => SetProperty(ref _durationMinutes, value);
    }

    private double _ticketRequiredProb = 0.65;
    public double TicketRequiredProb
    {
        get => _ticketRequiredProb;
        set => SetProperty(ref _ticketRequiredProb, value);
    }

    private double _buyTicketProb = 0.80;
    public double BuyTicketProb
    {
        get => _buyTicketProb;
        set => SetProperty(ref _buyTicketProb, value);
    }

    private double _cardUsageProb = 0.45;
    public double CardUsageProb
    {
        get => _cardUsageProb;
        set => SetProperty(ref _cardUsageProb, value);
    }

    private double _cardValidProb = 0.95;
    public double CardValidProb
    {
        get => _cardValidProb;
        set => SetProperty(ref _cardValidProb, value);
    }

    private double _accountValidProb = 0.97;
    public double AccountValidProb
    {
        get => _accountValidProb;
        set => SetProperty(ref _accountValidProb, value);
    }

    private double _sufficientFundsProb = 0.90;
    public double SufficientFundsProb
    {
        get => _sufficientFundsProb;
        set => SetProperty(ref _sufficientFundsProb, value);
    }

    public double UtilizationPreview =>
        ServiceRate > 0 ? ArrivalRate / ServiceRate : double.NaN;

    public bool IsStablePreview =>
        ServiceRate > 0 && ArrivalRate / ServiceRate < 1.0;

    public string StabilityHint =>
        IsStablePreview
            ? $"System stable (rho = {UtilizationPreview:P0})"
            : $"Unstable! (rho = {UtilizationPreview:F2} >= 1)";

    private double _kpiRho;
    public double KpiRho { get => _kpiRho; private set => SetProperty(ref _kpiRho, value); }
    private double _kpiWq;
    public double KpiWq  { get => _kpiWq;  private set => SetProperty(ref _kpiWq,  value); }
    private double _kpiW;
    public double KpiW   { get => _kpiW;   private set => SetProperty(ref _kpiW,   value); }
    private double _kpiLq;
    public double KpiLq  { get => _kpiLq;  private set => SetProperty(ref _kpiLq,  value); }
    private double _kpiL;
    public double KpiL   { get => _kpiL;   private set => SetProperty(ref _kpiL,   value); }

    private int _totalArrived;
    public int TotalArrived   { get => _totalArrived;   private set => SetProperty(ref _totalArrived,   value); }
    private int _totalCompleted;
    public int TotalCompleted { get => _totalCompleted; private set => SetProperty(ref _totalCompleted, value); }
    private int _totalLeft;
    public int TotalLeft      { get => _totalLeft;      private set => SetProperty(ref _totalLeft,      value); }

    private double _simAvgWait;
    public double SimAvgWait { get => _simAvgWait; private set => SetProperty(ref _simAvgWait, value); }
    private double _simAvgSys;
    public double SimAvgSys  { get => _simAvgSys;  private set => SetProperty(ref _simAvgSys,  value); }
    private double _throughput;
    public double Throughput { get => _throughput; private set => SetProperty(ref _throughput, value); }
    private double _completionRate;
    public double CompletionRate { get => _completionRate; private set => SetProperty(ref _completionRate, value); }
    private int _processedCount;
    public int ProcessedCount { get => _processedCount; private set => SetProperty(ref _processedCount, value); }
    private double _simCurrentTime;
    public double SimCurrentTime { get => _simCurrentTime; private set => SetProperty(ref _simCurrentTime, value); }

    private string _plainSummary = "Configure parameters and click Simulate to run.";
    public string PlainSummary
    {
        get => _plainSummary;
        private set => SetProperty(ref _plainSummary, value);
    }

    public ObservableCollection<string> PassengerLog { get; } = new();

    private string _traceOutput = string.Empty;
    public string TraceOutput
    {
        get => _traceOutput;
        private set => SetProperty(ref _traceOutput, value);
    }

    private bool _traceModeEnabled;
    public bool TraceModeEnabled
    {
        get => _traceModeEnabled;
        set => SetProperty(ref _traceModeEnabled, value);
    }

    private string _validationError = string.Empty;
    public string ValidationError
    {
        get => _validationError;
        private set
        {
            if (SetProperty(ref _validationError, value))
                OnPropertyChanged(nameof(HasValidationError));
        }
    }
    public bool HasValidationError => !string.IsNullOrEmpty(_validationError);

    public ObservableCollection<FlowNodeViewModel>       FlowNodes       { get; } = new();
    public ObservableCollection<FlowNodeViewModel>       BlockFlowNodes  { get; } = new();
    public ObservableCollection<FlowEdgeViewModel>       FlowEdges       { get; } = new();
    public ObservableCollection<PassengerTokenViewModel> PassengerTokens { get; } = new();

    public ICommand StartCommand  { get; }
    public ICommand PauseCommand  { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StepCommand   { get; }
    public ICommand StopCommand   { get; }
    public ICommand ResetCommand  { get; }
    public ICommand ToggleLeftPanelCommand  { get; }
    public ICommand ToggleRightPanelCommand { get; }
    public ICommand SelectModelCommand      { get; }
    public ICommand NavigateCommand         { get; }
    public ICommand ViewCustomerCommand     { get; }
    public ICommand CloseCustomerModalCommand { get; }

    private async Task StartSimulationAsync()
    {
        if (!ValidateParameters()) return;

        _cts    = new CancellationTokenSource();
        _runner = new SimulationRunner(BuildParameters());

        PassengerLog.Clear();
        TraceOutput      = string.Empty;
        PlainSummary     = "Computing simulation...";
        ProcessedCount   = 0;
        TotalArrived     = TotalCompleted = TotalLeft = 0;
        SimCurrentTime   = 0;
        PlaybackProgress = 0;
        ResetFlowDiagram();
        State = SimulationState.Running;

        try
        {
            var (result, events) = await _runner.RunForPlaybackAsync(cancellationToken: _cts.Token);

            _result       = result;
            PlaybackTotal = Math.Max(1, events.Count);
            ApplyResult(result);
            SaveRunToHistory(result);
            BuildModellingTable(result);
            
            CompletedPassengers.Clear();
            foreach (var p in result.Passengers)
            {
                CompletedPassengers.Add(p);
            }

            UpdateChartData();
            BuildGanttChart();

            if (TraceModeEnabled)
            {
                foreach (var p in result.Passengers.Take(50))
                {
                    var trace = string.Join(" > ", p.StepTrace.Select(StepLabel));
                    PassengerLog.Insert(0, $"#{p.Id}: {trace}");
                }
            }

            _playback.Load(events);
            _playback.SpeedMultiplier = SelectedSpeed.Value;
            _playback.Start();
            PlainSummary = "Playback started - watch passengers move through the diagram...";
        }
        catch (OperationCanceledException)
        {
            PlainSummary = "Simulation stopped by user.";
            State = SimulationState.Idle;
        }
        catch (Exception ex)
        {
            PlainSummary = $"Error: {ex.Message}";
            State = SimulationState.Idle;
        }
    }

    private void PausePlayback()
    {
        _playback.Pause();
        State = SimulationState.Paused;
        PlainSummary = "Playback paused. Click Resume to continue.";
    }

    private void ResumePlayback()
    {
        _playback.SpeedMultiplier = SelectedSpeed.Value;
        _playback.Resume();
        State = SimulationState.Running;
        PlainSummary = "Playback running...";
    }

    private void StepForwardPlayback()
    {
        if (State == SimulationState.Running)
            _playback.Pause();

        if (_playback.StepForward())
        {
            if (State != SimulationState.Completed)
                State = SimulationState.Paused;

            PlainSummary = "Advanced one step.";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void StopSimulation()
    {
        _cts?.Cancel();
        _playback.Pause();
        State = SimulationState.Idle;
        PlainSummary = "Stopped.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void SelectModel(object? parameter)
    {
        if (parameter is string text && Enum.TryParse<QueueModelType>(text, out var selected))
            SelectedQueueModel = selected;
        else if (parameter is QueueModelType typed)
            SelectedQueueModel = typed;

        ShowModelSelection = false;
    }

    private void Reset()
    {
        _cts?.Cancel();
        _playback.Reset();
        _runner = null;
        _cts    = null;
        _result = null;
        State   = SimulationState.Idle;

        KpiRho = KpiWq = KpiW = KpiLq = KpiL = 0;
        TotalArrived = TotalCompleted = TotalLeft = 0;
        SimAvgWait = SimAvgSys = Throughput = CompletionRate = 0;
        ProcessedCount   = 0;
        SimCurrentTime   = 0;
        PlaybackProgress = 0;
        PlaybackTotal    = 1;

        PassengerLog.Clear();
        TraceOutput     = string.Empty;
        ValidationError = string.Empty;
        PlainSummary    = "Parameters reset. Configure and click Simulate to run again.";
        ResetFlowDiagram();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnEventApplied(PlaybackEvent evt)
    {
        string newNodeId = StepToNodeId(evt.Step);

        if (_passengerCurrentNodes.TryGetValue(evt.PassengerId, out var oldNodeId) &&
            oldNodeId != newNodeId &&
            _nodeMap.TryGetValue(oldNodeId, out var oldNode))
        {
            oldNode.LeavePassenger(evt.PassengerId);
        }

        if (_nodeMap.TryGetValue(newNodeId, out var newNode))
            newNode.EnterPassenger(evt.PassengerId);

        _passengerCurrentNodes[evt.PassengerId] = newNodeId;

        EnsureToken(evt.PassengerId);
        if (_tokenMap.TryGetValue(evt.PassengerId, out var token) && newNode != null)
        {
            int    slot = GetNodeSlot(newNodeId, evt.PassengerId);
            double dx   = (slot % 4) * 16 - 24;
            double dy   = (slot / 4) * 16;
            token.X = newNode.CenterX + dx - 8;
            token.Y = newNode.CenterY + dy - 8;
        }

        switch (evt.Step)
        {
            case PassengerStep.Arrived:
                TotalArrived++;
                ProcessedCount = TotalArrived;
                break;
            case PassengerStep.Completed:
                TotalCompleted++;
                if (_tokenMap.TryGetValue(evt.PassengerId, out var ct)) ct.IsCompleted = true;
                break;
            case PassengerStep.PassengerLeftSystem:
                TotalLeft++;
                if (_tokenMap.TryGetValue(evt.PassengerId, out var lt)) lt.IsLeft = true;
                break;
        }

        SimCurrentTime   = evt.SimTime;
        PlaybackProgress = _playback.EventsDone;
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnPlaybackCompleted()
    {
        foreach (var n in FlowNodes)
            n.ClearPassengers();
        _passengerCurrentNodes.Clear();

        if (_result != null)
        {
            PlainSummary = BuildPlainSummary(_result);
            RunNarrative = BuildNarrative(_result);
        }

        State = SimulationState.Completed;
        CommandManager.InvalidateRequerySuggested();
    }

    private void SaveRunToHistory(SimulationResult r)
    {
        _runCounter++;
        var model = SelectedQueueModel switch
        {
            QueueModelType.MM1 => "M/M/1",
            QueueModelType.MG1 => "M/G/1",
            QueueModelType.GG1 => "G/G/1",
            _                  => "M/M/1",
        };
        RunHistory.Insert(0, new SimulationRunSummary
        {
            RunNumber      = _runCounter,
            ModelName      = model,
            Lambda         = ArrivalRate,
            Mu             = ServiceRate,
            Rho            = r.Utilization,
            Lq             = r.AvgQueueLength,
            Wq             = r.AvgQueueWaitTime,
            W              = r.AvgSystemTime,
            L              = r.AvgNumberInSystem,
            TotalServed    = r.TotalCompleted,
            TotalLeft      = r.TotalLeft,
            TotalArrived   = r.TotalArrived,
            Throughput     = r.Throughput,
            CompletionPct  = r.CompletionRate,
            SimDuration    = r.SimulationDurationMinutes,
            Timestamp      = DateTime.Now.ToString("HH:mm:ss"),
        });
    }

    /// <summary>
    /// Builds the per-customer modelling table, using the correct service-time
    /// distribution for the selected queue model:
    ///   M/M/1 → Exponential
    ///   M/G/1 → Gamma (shape = 1/Cs², rate = mu/shape)
    ///   G/G/1 → Gamma for service AND Gamma for inter-arrival (using Ca²)
    /// </summary>
    private void BuildModellingTable(SimulationResult result)
    {
        ModellingRows.Clear();

        int rowCount = Math.Min(25, Math.Max(8, result.TotalArrived > 0 ? result.TotalArrived : 15));
        var rng = new Random(42);

        double lambda  = ArrivalRate;
        double mu      = ServiceRate;
        double cs      = ServiceCv;
        double ca      = ArrivalCv;
        double meanIAT = lambda > 0 ? 1.0 / lambda * 60 : 5.0;
        double meanSvc = mu    > 0 ? 1.0 / mu    * 60 : 4.0;

        // For G/G/1: inter-arrival shape from Ca²
        double arrivalShape = ca > 0 ? 1.0 / (ca * ca) : 1.0;
        // For M/G/1 & G/G/1: service shape from Cs²
        double serviceShape = cs > 0 ? 1.0 / (cs * cs) : 1.0;

        double currentTime = 0;
        double serverFree  = 0;
        double cumProb     = 0;

        double totalWait       = 0;
        double totalTurnaround = 0;
        double totalResponse   = 0;
        double totalIAT        = 0;

        for (int i = 1; i <= rowCount; i++)
        {
            double u1 = rng.NextDouble();
            double u2 = rng.NextDouble();

            // Inter-arrival time
            double interArrival;
            if (i == 1)
                interArrival = 0;
            else if (IsGG1 && ca > 0 && Math.Abs(ca - 1.0) > 0.01)
                interArrival = Math.Max(0, SampleGamma(rng, arrivalShape, meanIAT / arrivalShape));
            else
                interArrival = Math.Max(0, -Math.Log(u1) * meanIAT); // Exponential (M/M/1 & M/G/1)

            currentTime += interArrival;

            // Service time
            double svcTime;
            if (IsMM1 || Math.Abs(cs - 1.0) < 0.01)
                svcTime = Math.Max(0.1, -Math.Log(u2) * meanSvc);
            else
                svcTime = Math.Max(0.1, SampleGamma(rng, serviceShape, meanSvc / serviceShape));

            double startTime = Math.Max(currentTime, serverFree);
            double endTime   = startTime + svcTime;
            serverFree = endTime;

            double wait       = startTime - currentTime;
            double turnaround = endTime   - currentTime;
            double response   = svcTime;

            double prevCumProb = cumProb;
            cumProb = 1.0 - Math.Exp(-lambda * (i / (double)rowCount) * mu);
            cumProb = Math.Min(cumProb, 0.9999);

            totalWait        += wait;
            totalTurnaround  += turnaround;
            totalResponse    += response;
            totalIAT         += interArrival;

            ModellingRows.Add(new ModellingRowViewModel
            {
                SNo                  = i,
                CumProb              = cumProb,
                CumProbLookup        = prevCumProb,
                MinsBetweenArrivals  = (int)Math.Round(interArrival),
                RandomInterArrival   = Math.Round(u1, 4),
                PoissonArrival       = (int)Math.Round(interArrival),
                RandomService        = Math.Round(u2, 4),
                ExpService           = Math.Round(svcTime, 2),
                ArrivalTime          = Math.Round(currentTime, 2),
                StartTime            = Math.Round(startTime, 2),
                EndTime              = Math.Round(endTime, 2),
                TurnaroundTime       = Math.Round(turnaround, 2),
                WaitTime             = Math.Round(wait, 2),
                ResponseTime         = Math.Round(response, 2),
            });
        }

        if (rowCount > 0)
        {
            ModellingAvgWait        = Math.Round(totalWait        / rowCount, 3);
            ModellingAvgTurnaround  = Math.Round(totalTurnaround  / rowCount, 3);
            ModellingAvgResponse    = Math.Round(totalResponse    / rowCount, 3);
            ModellingAvgInterArrival = Math.Round(totalIAT        / Math.Max(1, rowCount - 1), 3);
        }
    }

    /// <summary>Samples from a Gamma distribution via the Marsaglia-Tsang method.</summary>
    private static double SampleGamma(Random rng, double shape, double scale)
    {
        if (shape <= 0 || scale <= 0) return 0;
        if (shape < 1) return SampleGamma(rng, shape + 1, scale) * Math.Pow(rng.NextDouble(), 1.0 / shape);
        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x, v;
            do { x = NextGaussian(rng); v = 1.0 + c * x; } while (v <= 0);
            v = v * v * v;
            double u = rng.NextDouble();
            double x2 = x * x;
            if (u < 1.0 - 0.0331 * (x2 * x2)) return d * v * scale;
            if (Math.Log(u) < 0.5 * x2 + d * (1.0 - v + Math.Log(v))) return d * v * scale;
        }
    }

    private static double NextGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    /// <summary>Builds the Gantt chart from completed passengers — first 5 + last 5.</summary>
    private void BuildGanttChart()
    {
        GanttItems.Clear();
        if (CompletedPassengers.Count == 0) return;

        var passengers = CompletedPassengers.ToList();
        int total = passengers.Count;
        const int ShowEach = 5;

        var shown = new List<(Passenger p, bool isEllipsis)>();
        int firstCount = Math.Min(ShowEach, total);
        for (int i = 0; i < firstCount; i++) shown.Add((passengers[i], false));

        if (total > ShowEach * 2)
            shown.Add((passengers[0], true)); // ellipsis marker

        int lastStart = Math.Max(firstCount, total - ShowEach);
        for (int i = lastStart; i < total; i++) shown.Add((passengers[i], false));

        int row = 0;
        foreach (var (p, isEllipsis) in shown)
        {
            if (isEllipsis)
            {
                GanttItems.Add(new GanttItem { IsEllipsis = true, Row = row++ });
                continue;
            }

            double arrivalTime  = p.ArrivalTime;
            double serviceStart = p.ServiceStartTime > 0 ? p.ServiceStartTime : arrivalTime;
            double serviceEnd   = p.ExitTime   > 0 ? p.ExitTime   : serviceStart;

            GanttItems.Add(new GanttItem
            {
                Row          = row++,
                PassengerId  = p.Id,
                ArrivalTime  = arrivalTime,
                WaitStart    = arrivalTime,
                ServiceStart = serviceStart,
                ServiceEnd   = serviceEnd,
                WaitDuration = Math.Max(0, serviceStart - arrivalTime),
                SvcDuration  = Math.Max(0, serviceEnd - serviceStart),
                IsEllipsis   = false,
            });
        }
    }

    private void UpdateChartData()
    {
        if (WaitTimeTrend[0].Values is ObservableCollection<double> waitValues &&
            QueueLengthTrend[0].Values is ObservableCollection<double> queueValues &&
            XAxes[0].Labels is ObservableCollection<string> labels)
        {
            waitValues.Clear();
            queueValues.Clear();
            labels.Clear();

            // Show last 10 runs for trend
            var latest = RunHistory.Take(10).Reverse().ToList();
            foreach (var run in latest)
            {
                waitValues.Add(run.Wq);
                queueValues.Add(run.Lq);
                labels.Add($"Run {run.RunNumber}");
            }
        }

        // Update KPI Sparklines with last 20 runs trend
        if (KpiRhoSeries[0].Values is ObservableCollection<double> rhoVals &&
            KpiWqSeries[0].Values is ObservableCollection<double> wqVals &&
            KpiWSeries[0].Values is ObservableCollection<double> wVals &&
            KpiLSeries[0].Values is ObservableCollection<double> lVals)
        {
            rhoVals.Clear();
            wqVals.Clear();
            wVals.Clear();
            lVals.Clear();
            
            var sparklineHistory = RunHistory.Take(20).Reverse().ToList();
            foreach (var run in sparklineHistory)
            {
                rhoVals.Add(run.Rho);
                wqVals.Add(run.Wq);
                wVals.Add(run.W);
                lVals.Add(run.L);
            }
        }

        if (_result != null &&
            CurrentRunWaitTimes[0].Values is ObservableCollection<double> currentWait &&
            CurrentRunTurnaroundTimes[0].Values is ObservableCollection<double> currentTurnaround)
        {
            currentWait.Clear();
            currentTurnaround.Clear();
            foreach (var p in _result.Passengers.Where(x => x.Completed))
            {
                currentWait.Add(p.WaitTime);
                currentTurnaround.Add(p.SystemTime);
            }
        }

        // Update Model Compare Charts
        if (ModelCompareWqSeries[0].Values is ObservableCollection<double> wqCompareVals &&
            ModelCompareWSeries[0].Values is ObservableCollection<double> wCompareVals)
        {
            var mm1Run = RunHistory.FirstOrDefault(r => r.ModelName == "M/M/1");
            var mg1Run = RunHistory.FirstOrDefault(r => r.ModelName == "M/G/1");
            var gg1Run = RunHistory.FirstOrDefault(r => r.ModelName == "G/G/1");

            wqCompareVals[0] = mm1Run?.Wq ?? 0;
            wqCompareVals[1] = mg1Run?.Wq ?? 0;
            wqCompareVals[2] = gg1Run?.Wq ?? 0;

            wCompareVals[0] = mm1Run?.W ?? 0;
            wCompareVals[1] = mg1Run?.W ?? 0;
            wCompareVals[2] = gg1Run?.W ?? 0;
        }
    }

    private void EnsureToken(int passengerId)
    {
        if (_tokenMap.ContainsKey(passengerId)) return;

        if (PassengerTokens.Count >= MaxVisibleTokens)
        {
            var evict = PassengerTokens.FirstOrDefault(t => t.IsCompleted || t.IsLeft)
                     ?? PassengerTokens.FirstOrDefault();
            if (evict != null)
            {
                PassengerTokens.Remove(evict);
                _tokenMap.Remove(evict.PassengerId);
            }
        }

        var token = new PassengerTokenViewModel { PassengerId = passengerId };
        _tokenMap[passengerId] = token;
        PassengerTokens.Add(token);
    }

    private int GetNodeSlot(string nodeId, int passengerId)
    {
        int slot = 0;
        foreach (var (pid, nid) in _passengerCurrentNodes)
        {
            if (nid == nodeId && pid < passengerId)
                slot++;
        }
        return slot;
    }

    private void BuildFlowDiagram()
    {
        var nodes = new FlowNodeViewModel[]
        {
            new() { Id="start",            Title="Start",                     Type=FlowNodeType.Start,    Left=80,   Top=20,  Width=120, Height=42 },
            new() { Id="arrival",          Title="Passenger Arrival",         Type=FlowNodeType.Process,  Left=22,   Top=100, Width=200, Height=44 },
            new() { Id="ticketReq",        Title="Ticket Required?",          Type=FlowNodeType.Decision, Left=20,   Top=188, Width=200, Height=62 },
            new() { Id="ticketCounter",    Title="Ticket Counter",            Type=FlowNodeType.Process,  Left=20,   Top=290, Width=210, Height=44 },
            new() { Id="security",         Title="Security Check",            Type=FlowNodeType.Process,  Left=20,   Top=390, Width=210, Height=44 },
            new() { Id="waiting",          Title="Waiting Area",              Type=FlowNodeType.Process,  Left=20,   Top=490, Width=210, Height=44 },
            new() { Id="trainArrival",     Title="Train Arrival",             Type=FlowNodeType.Process,  Left=20,   Top=590, Width=210, Height=44 },
            new() { Id="boarding",         Title="Boarding",                  Type=FlowNodeType.Process,  Left=20,   Top=690, Width=210, Height=44 },
            new() { Id="departs",          Title="Passenger Departs",         Type=FlowNodeType.Process,  Left=20,   Top=790, Width=210, Height=44 },
            new() { Id="end",              Title="End",                       Type=FlowNodeType.Success,  Left=80,   Top=900, Width=120, Height=42 },
            new() { Id="inquiry",          Title="Inquiry Desk",              Type=FlowNodeType.Process,  Left=340,  Top=205, Width=220, Height=44 },
            new() { Id="buyTicket",        Title="Buy Ticket?",               Type=FlowNodeType.Decision, Left=340,  Top=390, Width=220, Height=62 },
            new() { Id="hasCash",          Title="Has Cash?",                 Type=FlowNodeType.Decision, Left=340,  Top=518, Width=220, Height=62 },
            new() { Id="sufficientFunds",  Title="Sufficient Funds?",         Type=FlowNodeType.Decision, Left=340,  Top=646, Width=220, Height=62 },
            new() { Id="ticketReceipt",    Title="Ticket / Receipt",          Type=FlowNodeType.Success,  Left=340,  Top=860, Width=220, Height=50 },
            new() { Id="hasCard",          Title="Has Card?",                 Type=FlowNodeType.Decision, Left=740,  Top=20,  Width=220, Height=62 },
            new() { Id="cardValid",        Title="Card Valid?",               Type=FlowNodeType.Decision, Left=740,  Top=212, Width=220, Height=62 },
            new() { Id="fundsAvailable",   Title="Funds Available?",          Type=FlowNodeType.Decision, Left=740,  Top=340, Width=220, Height=62 },
            new() { Id="paymentBank",      Title="Payment Verified by Bank",  Type=FlowNodeType.Process,  Left=740,  Top=470, Width=240, Height=50 },
            new() { Id="accountValid",     Title="Account Valid?",            Type=FlowNodeType.Decision, Left=740,  Top=600, Width=220, Height=62 },
            new() { Id="txnComplete",      Title="Transaction Complete",      Type=FlowNodeType.Success,  Left=740,  Top=860, Width=240, Height=50 },
            new() { Id="leave",            Title="Passenger Leaves System",   Type=FlowNodeType.Failure,  Left=1010, Top=900, Width=240, Height=50 },
        };

        foreach (var n in nodes) { FlowNodes.Add(n); _nodeMap[n.Id] = n; }

        FlowEdgeViewModel Edge(string from, string to, string? lbl, params (double x, double y)[] pts)
        {
            var e = new FlowEdgeViewModel { FromId = from, ToId = to, Label = lbl };
            foreach (var (x, y) in pts) e.Points.Add(new Point(x, y));
            e.Build();
            return e;
        }

        var edges = new[]
        {
            Edge("start",           "arrival",         null,  (140, 62), (122, 100)),
            Edge("arrival",         "ticketReq",       null,  (122, 144), (120, 188)),
            Edge("ticketReq",       "ticketCounter",   "Yes", (120, 250), (125, 290)),
            Edge("ticketReq",       "inquiry",         "No",  (220, 219), (340, 227)),
            Edge("ticketCounter",   "security",        null,  (125, 334), (125, 390)),
            Edge("security",        "waiting",         null,  (125, 434), (125, 490)),
            Edge("waiting",         "trainArrival",    null,  (125, 534), (125, 590)),
            Edge("trainArrival",    "boarding",        null,  (125, 634), (125, 690)),
            Edge("boarding",        "departs",         null,  (125, 734), (125, 790)),
            Edge("departs",         "end",             null,  (125, 834), (140, 900)),
            Edge("inquiry",         "buyTicket",       null,  (450, 249), (450, 390)),
            Edge("buyTicket",       "hasCash",         "Yes", (450, 452), (450, 518)),
            Edge("buyTicket",       "leave",           "No",  (560, 420), (930, 420), (930, 925), (1010, 925)),
            Edge("hasCash",         "sufficientFunds", "Yes", (450, 580), (450, 646)),
            Edge("hasCash",         "hasCard",         "No",  (560, 548), (700, 548), (700, 51), (740, 51)),
            Edge("sufficientFunds", "ticketReceipt",   "Yes", (450, 708), (450, 860)),
            Edge("sufficientFunds", "leave",           "No",  (560, 676), (930, 676), (930, 933), (1010, 933)),
            Edge("hasCard",         "cardValid",       "Yes", (850, 82), (850, 212)),
            Edge("hasCard",         "leave",           "No",  (960, 51), (960, 925), (1010, 925)),
            Edge("cardValid",       "fundsAvailable",  "Yes", (850, 274), (850, 340)),
            Edge("cardValid",       "leave",           "No",  (960, 243), (960, 933), (1010, 933)),
            Edge("fundsAvailable",  "paymentBank",     "Yes", (850, 402), (860, 470)),
            Edge("fundsAvailable",  "leave",           "No",  (960, 371), (960, 941), (1010, 941)),
            Edge("paymentBank",     "accountValid",    null,  (860, 520), (850, 600)),
            Edge("accountValid",    "txnComplete",     "Yes", (850, 662), (860, 860)),
            Edge("accountValid",    "leave",           "No",  (960, 631), (960, 949), (1010, 949)),
            Edge("txnComplete",     "ticketReceipt",   null,  (740, 885), (560, 885)),
            Edge("ticketReceipt",   "security",        null,  (340, 885), (260, 885), (260, 412), (230, 412)),
        };
        foreach (var e in edges) FlowEdges.Add(e);

        RebuildBlockFlowNodes();
    }

    private void RebuildBlockFlowNodes()
    {
        BlockFlowNodes.Clear();

        foreach (var node in FlowNodes.Where(n => n.Id is not "leave" and not "end"))
            BlockFlowNodes.Add(node);

        if (_nodeMap.TryGetValue("leave", out var leaveNode))
            BlockFlowNodes.Add(leaveNode);

        if (_nodeMap.TryGetValue("end", out var endNode))
            BlockFlowNodes.Add(endNode);
    }

    private void ResetFlowDiagram()
    {
        foreach (var n in FlowNodes) n.ClearPassengers();
        PassengerTokens.Clear();
        _tokenMap.Clear();
        _passengerCurrentNodes.Clear();
    }

    private static string StepToNodeId(PassengerStep step) => step switch
    {
        PassengerStep.Arrived                => "arrival",
        PassengerStep.TicketRequired_Yes      => "ticketReq",
        PassengerStep.TicketRequired_No       => "ticketReq",
        PassengerStep.TicketCounter           => "ticketCounter",
        PassengerStep.InquiryDesk             => "inquiry",
        PassengerStep.BuyTicket_Yes           => "buyTicket",
        PassengerStep.BuyTicket_No            => "leave",
        PassengerStep.HasCash_Yes             => "hasCash",
        PassengerStep.HasCash_No              => "hasCash",
        PassengerStep.CashSufficientFunds_Yes => "sufficientFunds",
        PassengerStep.CashSufficientFunds_No  => "sufficientFunds",
        PassengerStep.HasCard_Yes             => "hasCard",
        PassengerStep.HasCard_No              => "hasCard",
        PassengerStep.CardValid_Yes           => "cardValid",
        PassengerStep.CardValid_No            => "cardValid",
        PassengerStep.CardFundsAvailable_Yes  => "fundsAvailable",
        PassengerStep.CardFundsAvailable_No   => "leave",
        PassengerStep.AccountValid_Yes        => "accountValid",
        PassengerStep.AccountValid_No         => "leave",
        PassengerStep.PaymentVerifiedByBank   => "paymentBank",
        PassengerStep.TransactionComplete     => "txnComplete",
        PassengerStep.TicketReceipt           => "ticketReceipt",
        PassengerStep.SecurityCheck           => "security",
        PassengerStep.WaitingArea             => "waiting",
        PassengerStep.TrainArrival            => "trainArrival",
        PassengerStep.Boarding                => "boarding",
        PassengerStep.PassengerDeparts        => "departs",
        PassengerStep.Completed               => "end",
        PassengerStep.PassengerLeftSystem     => "leave",
        _                                     => "arrival",
    };

    private SimulationParameters BuildParameters() => new()
    {
        ModelType                = SelectedQueueModel,
        ArrivalRate               = ArrivalRate,
        ServiceRate               = ServiceRate,
        ServiceCv                 = ServiceCv,
        ArrivalCv                 = ArrivalCv,
        SimulationDurationMinutes = DurationMinutes,
        TicketRequiredProbability = TicketRequiredProb,
        BuyTicketProbability      = BuyTicketProb,
        CardUsageProbability      = CardUsageProb,
        CardValidProbability      = CardValidProb,
        AccountValidProbability   = AccountValidProb,
        SufficientFundsProbability = SufficientFundsProb,
    };

    private bool ValidateParameters()
    {
        ValidationError = string.Empty;
        if (ArrivalRate <= 0)  { ValidationError = "Arrival rate must be > 0."; return false; }
        if (ServiceRate <= 0)  { ValidationError = "Service rate must be > 0."; return false; }
        if (DurationMinutes < 1) { ValidationError = "Duration must be >= 1 min."; return false; }
        if (ServiceCv <= 0) { ValidationError = "Service CV must be > 0."; return false; }
        if (SelectedQueueModel == QueueModelType.GG1 && ArrivalCv <= 0)
        {
            ValidationError = "Arrival CV must be > 0 for G/G/1.";
            return false;
        }
        double[] probs = { TicketRequiredProb, BuyTicketProb, CardUsageProb,
                           CardValidProb, AccountValidProb, SufficientFundsProb };
        if (probs.Any(p => p < 0 || p > 1))
        { ValidationError = "All probabilities must be between 0 and 1."; return false; }
        return true;
    }

    private void ApplyResult(SimulationResult result)
    {
        KpiRho = result.Utilization; KpiWq = result.AvgQueueWaitTime;
        KpiW   = result.AvgSystemTime; KpiLq = result.AvgQueueLength;
        KpiL   = result.AvgNumberInSystem;
        SimAvgWait = result.SimAvgWaitTime; SimAvgSys = result.SimAvgSystemTime;
        Throughput = result.Throughput; CompletionRate = result.CompletionRate;
    }

    private static string BuildPlainSummary(SimulationResult r)
    {
        bool stable = !double.IsNaN(r.AvgQueueWaitTime);
        string u = $"Server busy {r.Utilization:P0} of the time (rho={r.Utilization:F2}).";
        if (!stable) return $"{u}\nUNSTABLE - arrivals exceeded capacity.";
        return $"{u}\nAvg queue wait: {r.AvgQueueWaitTime:F2} min  System time: {r.AvgSystemTime:F2} min\n" +
               $"{r.TotalArrived} arrived, {r.TotalCompleted} boarded ({r.CompletionRate:F1}%), " +
               $"{r.TotalLeft} left.  Throughput: {r.Throughput:F2} pax/min";
    }

    private static string BuildNarrative(SimulationResult r)
    {
        bool stable = r.Utilization < 1.0;
        string status = stable ? "busy but stable" : "unstable and overloaded";
        return $"In this simulation run, {r.TotalArrived} passengers arrived into the system. The server was utilized {r.Utilization:P0} of the time, indicating a {status} environment. On average, passengers waited {r.AvgQueueWaitTime:F2} minutes in the queue and spent {r.AvgSystemTime:F2} minutes in the system entirely. A total of {r.TotalCompleted} passengers successfully completed their journey ({r.CompletionRate:F1}% completion rate), while {r.TotalLeft} left due to balking or reneging. The system achieved a throughput of {r.Throughput:F2} passengers per minute.";
    }

    private static string StepLabel(PassengerStep step) => step switch
    {
        PassengerStep.Arrived               => "Arrived",
        PassengerStep.TicketRequired_Yes     => "Ticket+",
        PassengerStep.TicketRequired_No      => "No Ticket",
        PassengerStep.TicketCounter          => "Ticket Counter",
        PassengerStep.SecurityCheck          => "Security",
        PassengerStep.WaitingArea            => "Waiting",
        PassengerStep.TrainArrival           => "Train",
        PassengerStep.Boarding               => "Boarding",
        PassengerStep.PassengerDeparts       => "Departs",
        PassengerStep.InquiryDesk            => "Inquiry",
        PassengerStep.BuyTicket_Yes          => "Buy+",
        PassengerStep.BuyTicket_No           => "Won't Buy",
        PassengerStep.PaymentVerifiedByBank  => "Bank+",
        PassengerStep.TransactionComplete    => "Txn+",
        PassengerStep.TicketReceipt          => "Receipt",
        PassengerStep.PassengerLeftSystem    => "LEFT",
        PassengerStep.Completed              => "DONE",
        _                                    => step.ToString(),
    };
}

/// <summary>Playback speed option for the speed ComboBox.</summary>
public record SpeedOption(string Label, double Value)
{
    public override string ToString() => Label;
}

/// <summary>Represents an item in the Gantt chart.</summary>
public class GanttItem
{
    public int Row { get; set; }
    public int PassengerId { get; set; }
    public double ArrivalTime { get; set; }
    public double WaitStart { get; set; }
    public double ServiceStart { get; set; }
    public double ServiceEnd { get; set; }
    public double WaitDuration { get; set; }
    public double SvcDuration { get; set; }
    public bool IsEllipsis { get; set; }
}
