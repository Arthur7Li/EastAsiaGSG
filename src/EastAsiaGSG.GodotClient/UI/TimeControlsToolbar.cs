using EastAsiaGSG.Core.ECS;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Top HUD Toolbar managing simulation execution timing, calendar progression,
/// play/pause toggles, simulation speed multipliers, and manual 6-hour step triggers.
/// </summary>
public partial class TimeControlsToolbar : PanelContainer
{
    private Label? _dateLabel;
    private Label? _statusBadge;
    private Button? _playPauseButton;
    private Button? _stepButton;
    private Button? _speed1Button;
    private Button? _speed2Button;
    private Button? _speed5Button;

    private SimulationWorld? _world;
    private bool _isPaused = false;
    private int _speedMultiplier = 1;
    private double _tickAccumulator;

    public bool IsPaused => _isPaused;
    public int SpeedMultiplier => _speedMultiplier;

    public event Action? Ticked;
    public event Action<bool>? PauseStateChanged;
    public event Action<int>? SpeedMultiplierChanged;

    public void Initialize(SimulationWorld world)
    {
        _world = world;
        UpdateCalendarLabel();
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(580, 44);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.16f, 0.94f),
            BorderColor = new Color(0.35f, 0.42f, 0.52f, 0.95f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6
        };
        AddThemeStyleboxOverride("panel", style);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        AddChild(margin);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(hbox);

        // Calendar Date Display
        _dateLabel = new Label
        {
            Text = "1985-01-01 00:00",
            VerticalAlignment = VerticalAlignment.Center
        };
        _dateLabel.AddThemeFontSizeOverride("font_size", 14);
        _dateLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.88f, 0.45f));
        hbox.AddChild(_dateLabel);

        // Status Badge (PAUSED vs RUNNING)
        _statusBadge = new Label
        {
            Text = _isPaused ? "⏸ PAUSED" : "▶ RUNNING",
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusBadge.AddThemeFontSizeOverride("font_size", 12);
        _statusBadge.AddThemeColorOverride("font_color", _isPaused ? new Color(1.0f, 0.45f, 0.45f) : new Color(0.35f, 1.0f, 0.55f));
        hbox.AddChild(_statusBadge);

        // Separator
        hbox.AddChild(new VSeparator());

        // Play/Pause Button
        _playPauseButton = new Button
        {
            Text = _isPaused ? "▶ Play (Space)" : "⏸ Pause (Space)",
            CustomMinimumSize = new Vector2(110, 30)
        };
        _playPauseButton.Pressed += TogglePlayPause;
        hbox.AddChild(_playPauseButton);

        // Step Button
        _stepButton = new Button
        {
            Text = "Step 6h (>|)",
            CustomMinimumSize = new Vector2(85, 30)
        };
        _stepButton.Pressed += ExecuteManualStep;
        hbox.AddChild(_stepButton);

        // Separator
        hbox.AddChild(new VSeparator());

        // Speed Buttons
        _speed1Button = new Button { Text = "1x (1)", ToggleMode = true, ButtonPressed = true, CustomMinimumSize = new Vector2(50, 30) };
        _speed2Button = new Button { Text = "2x (2)", ToggleMode = true, ButtonPressed = false, CustomMinimumSize = new Vector2(50, 30) };
        _speed5Button = new Button { Text = "5x (3)", ToggleMode = true, ButtonPressed = false, CustomMinimumSize = new Vector2(50, 30) };

        _speed1Button.Pressed += () => SetSpeed(1);
        _speed2Button.Pressed += () => SetSpeed(2);
        _speed5Button.Pressed += () => SetSpeed(5);

        hbox.AddChild(_speed1Button);
        hbox.AddChild(_speed2Button);
        hbox.AddChild(_speed5Button);

        UpdateCalendarLabel();
    }

    public override void _Process(double delta)
    {
        if (_world == null || _isPaused)
        {
            return;
        }

        // Compute tick interval in seconds:
        // Speed 1x: 1 tick/second
        // Speed 2x: 0.35 seconds/tick (~2.8 ticks/second)
        // Speed 5x: 0.12 seconds/tick (~8.3 ticks/second)
        double interval = _speedMultiplier switch
        {
            1 => 1.0,
            2 => 0.35,
            5 => 0.12,
            _ => 1.0
        };

        _tickAccumulator += delta;
        if (_tickAccumulator >= interval)
        {
            _tickAccumulator -= interval;
            ExecuteSimulationTick();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } keyEvent)
        {
            if (keyEvent.Keycode == Key.Space)
            {
                TogglePlayPause();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Keycode == Key.Key1)
            {
                SetSpeed(1);
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Keycode == Key.Key2)
            {
                SetSpeed(2);
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Keycode == Key.Key3)
            {
                SetSpeed(5);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void TogglePlayPause()
    {
        _isPaused = !_isPaused;
        _tickAccumulator = 0.0;

        if (_playPauseButton != null)
        {
            _playPauseButton.Text = _isPaused ? "▶ Play (Space)" : "⏸ Pause (Space)";
        }

        if (_statusBadge != null)
        {
            _statusBadge.Text = _isPaused ? "⏸ PAUSED" : "▶ RUNNING";
            _statusBadge.AddThemeColorOverride("font_color", _isPaused ? new Color(1.0f, 0.45f, 0.45f) : new Color(0.35f, 1.0f, 0.55f));
        }

        PauseStateChanged?.Invoke(_isPaused);
    }

    public void SetSpeed(int speedMultiplier)
    {
        _speedMultiplier = speedMultiplier;
        _tickAccumulator = 0.0;

        if (_speed1Button != null) _speed1Button.ButtonPressed = speedMultiplier == 1;
        if (_speed2Button != null) _speed2Button.ButtonPressed = speedMultiplier == 2;
        if (_speed5Button != null) _speed5Button.ButtonPressed = speedMultiplier == 5;

        SpeedMultiplierChanged?.Invoke(speedMultiplier);
    }

    public void ExecuteManualStep()
    {
        ExecuteSimulationTick();
    }

    private void ExecuteSimulationTick()
    {
        if (_world == null)
        {
            return;
        }

        _world.StepSixHours();
        UpdateCalendarLabel();
        Ticked?.Invoke();
    }

    private void UpdateCalendarLabel()
    {
        if (_dateLabel == null || _world == null)
        {
            return;
        }

        string hourStr = _world.CurrentHour < 10 ? $"0{_world.CurrentHour}:00" : $"{_world.CurrentHour}:00";
        string monthStr = _world.CurrentMonth < 10 ? $"0{_world.CurrentMonth}" : $"{_world.CurrentMonth}";
        string dayStr = _world.CurrentDay < 10 ? $"0{_world.CurrentDay}" : $"{_world.CurrentDay}";

        _dateLabel.Text = $"{_world.CurrentYear}-{monthStr}-{dayStr} {hourStr}";
    }
}
