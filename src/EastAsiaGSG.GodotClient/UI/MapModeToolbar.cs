using EastAsiaGSG.Data.Map;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Map Mode HUD Toolbar providing reactive buttons and F1–F5 hotkey toggles
/// for switching between Political, Terrain, Supply, Industrial, and Air Superiority map modes.
/// </summary>
public partial class MapModeToolbar : PanelContainer
{
    [Signal]
    public delegate void MapModeChangedEventHandler(int mapMode);

    public event Action<MapMode>? ModeSelected;

    public MapMode CurrentMode { get; private set; } = MapMode.Political;

    private readonly Dictionary<MapMode, Button> _buttons = new();
    private HBoxContainer? _container;

    private static readonly (MapMode Mode, string Label, Key Shortcut)[] Modes =
    [
        (MapMode.Political, "Political (F1)", Key.F1),
        (MapMode.Terrain, "Terrain (F2)", Key.F2),
        (MapMode.Supply, "Supply (F3)", Key.F3),
        (MapMode.Industrial, "Industry (F4)", Key.F4),
        (MapMode.AirSuperiority, "Air Region (F5)", Key.F5)
    ];

    public override void _Ready()
    {
        BuildUiIfNeeded();
        UpdateButtonStates();
    }

    private void BuildUiIfNeeded()
    {
        _container = GetNodeOrNull<HBoxContainer>("HBoxContainer");
        if (_container == null)
        {
            _container = new HBoxContainer
            {
                Name = "HBoxContainer",
                Alignment = BoxContainer.AlignmentMode.Center
            };
            _container.AddThemeConstantOverride("separation", 6);
            AddChild(_container);
        }

        // Apply dark translucent panel styling
        var styleBox = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.12f, 0.15f, 0.85f),
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };
        AddThemeStyleboxOverride("panel", styleBox);

        foreach (var (mode, label, _) in Modes)
        {
            if (_buttons.ContainsKey(mode))
            {
                continue;
            }

            var btn = new Button
            {
                Text = label,
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(110, 32)
            };

            MapMode capturedMode = mode;
            btn.Pressed += () => SelectMode(capturedMode);

            _buttons[mode] = btn;
            _container.AddChild(btn);
        }
    }

    /// <summary>
    /// Switches the active MapMode, refreshes button toggle states, and fires events.
    /// </summary>
    public void SelectMode(MapMode mode)
    {
        if (CurrentMode == mode)
        {
            UpdateButtonStates();
            return;
        }

        CurrentMode = mode;
        UpdateButtonStates();

        EmitSignal(SignalName.MapModeChanged, (int)mode);
        ModeSelected?.Invoke(mode);
    }

    private void UpdateButtonStates()
    {
        foreach (var (mode, btn) in _buttons)
        {
            btn.SetPressedNoSignal(mode == CurrentMode);
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            foreach (var (mode, _, shortcutKey) in Modes)
            {
                if (keyEvent.Keycode == shortcutKey)
                {
                    SelectMode(mode);
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
        }
    }
}
