using Godot;

namespace EastAsiaGSG.GodotClient.Map;

/// <summary>
/// 2D RTS Camera Controller providing smooth WASD/arrow panning, viewport edge panning,
/// mouse drag-panning, exact cursor-centered zoom, and map boundary clamping.
/// </summary>
public partial class CameraController : Camera2D
{
    [Export] public float PanSpeed { get; set; } = 800f;
    [Export] public float FastPanMultiplier { get; set; } = 2.0f;
    [Export] public bool EnableEdgePan { get; set; } = true;
    [Export] public float EdgeMargin { get; set; } = 20f;
    [Export] public float ZoomSpeed { get; set; } = 0.15f;
    [Export] public float MinZoom { get; set; } = 0.25f;
    [Export] public float MaxZoom { get; set; } = 4.0f;
    [Export] public float SmoothDamping { get; set; } = 12.0f;
    [Export] public Rect2 MapBounds { get; set; } = new(0, 0, 1024, 1024);
    [Export] public float BoundsMargin { get; set; } = 150f;

    private Vector2 _targetPosition;
    private float _targetZoom = 1.0f;
    private bool _isDragging;

    public float TargetZoom => _targetZoom;
    public Vector2 TargetPosition => _targetPosition;

    public override void _Ready()
    {
        _targetPosition = Position;
        _targetZoom = Mathf.Clamp(Zoom.X, MinZoom, MaxZoom);
        Zoom = new Vector2(_targetZoom, _targetZoom);
    }

    /// <summary>
    /// Configures the map boundary rect for pan clamping.
    /// </summary>
    public void SetMapBounds(Rect2 bounds, float margin = 150f)
    {
        MapBounds = bounds;
        BoundsMargin = margin;
        ClampTargetPosition();
    }

    /// <summary>
    /// Smoothly focuses the camera on a given world coordinate, optionally adjusting the zoom level.
    /// </summary>
    public void FocusOnPosition(Vector2 worldPosition, float? zoomLevel = null)
    {
        _targetPosition = worldPosition;
        if (zoomLevel.HasValue)
        {
            _targetZoom = Mathf.Clamp(zoomLevel.Value, MinZoom, MaxZoom);
        }
        ClampTargetPosition();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn)
        {
            // Cursor-centered zoom via mouse wheel
            if (mouseBtn.Pressed)
            {
                float zoomFactor = 0f;
                if (mouseBtn.ButtonIndex == MouseButton.WheelUp)
                {
                    zoomFactor = 1.0f + ZoomSpeed;
                }
                else if (mouseBtn.ButtonIndex == MouseButton.WheelDown)
                {
                    zoomFactor = 1.0f / (1.0f + ZoomSpeed);
                }

                if (zoomFactor != 0f)
                {
                    ApplyCursorCenteredZoom(zoomFactor);
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            // Middle mouse or Right mouse button drag-panning
            if (mouseBtn.ButtonIndex is MouseButton.Middle or MouseButton.Right)
            {
                _isDragging = mouseBtn.Pressed;
                if (_isDragging)
                {
                    GetViewport().SetInputAsHandled();
                }
            }
        }
        else if (_isDragging && @event is InputEventMouseMotion mouseMotion)
        {
            // Drag moves camera opposite to mouse motion vector scaled by current zoom
            Vector2 delta = mouseMotion.Relative / _targetZoom;
            _targetPosition -= delta;
            Position -= delta;
            ClampTargetPosition();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Vector2 inputDir = Vector2.Zero;

        // Keyboard panning (WASD & Arrow Keys)
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
        {
            inputDir.Y -= 1f;
        }
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
        {
            inputDir.Y += 1f;
        }
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
        {
            inputDir.X -= 1f;
        }
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
        {
            inputDir.X += 1f;
        }

        // Viewport edge panning
        if (EnableEdgePan && !_isDragging)
        {
            Viewport viewport = GetViewport();
            Vector2 mousePos = viewport.GetMousePosition();
            Rect2 visibleRect = viewport.GetVisibleRect();

            // Only edge-pan if the mouse is currently within the game window
            if (visibleRect.HasPoint(mousePos))
            {
                if (mousePos.X <= EdgeMargin)
                {
                    inputDir.X -= 1f;
                }
                else if (mousePos.X >= visibleRect.Size.X - EdgeMargin)
                {
                    inputDir.X += 1f;
                }

                if (mousePos.Y <= EdgeMargin)
                {
                    inputDir.Y -= 1f;
                }
                else if (mousePos.Y >= visibleRect.Size.Y - EdgeMargin)
                {
                    inputDir.Y += 1f;
                }
            }
        }

        // Apply keyboard/edge movement
        if (inputDir != Vector2.Zero)
        {
            inputDir = inputDir.Normalized();
            bool isFast = Input.IsKeyPressed(Key.Shift);
            float currentSpeed = PanSpeed * (isFast ? FastPanMultiplier : 1.0f);
            _targetPosition += (inputDir * currentSpeed * dt) / _targetZoom;
            ClampTargetPosition();
        }

        // Smooth framerate-independent exponential interpolation
        float smoothFactor = 1.0f - Mathf.Exp(-SmoothDamping * dt);
        Position = Position.Lerp(_targetPosition, smoothFactor);

        float newZoom = Mathf.Lerp(Zoom.X, _targetZoom, smoothFactor);
        Zoom = new Vector2(newZoom, newZoom);
    }

    private void ApplyCursorCenteredZoom(float zoomFactor)
    {
        float oldZoom = _targetZoom;
        float newZoom = Mathf.Clamp(oldZoom * zoomFactor, MinZoom, MaxZoom);

        if (Mathf.Abs(newZoom - oldZoom) <= 0.0001f)
        {
            return;
        }

        // World position currently under the mouse cursor before zooming
        Vector2 mouseWorld = GetGlobalMousePosition();

        _targetZoom = newZoom;

        // Shift camera target position so mouseWorld remains under the cursor after zoom
        _targetPosition = mouseWorld - (mouseWorld - _targetPosition) * (oldZoom / newZoom);
        ClampTargetPosition();
    }

    private void ClampTargetPosition()
    {
        float minX = MapBounds.Position.X - BoundsMargin;
        float maxX = MapBounds.End.X + BoundsMargin;
        float minY = MapBounds.Position.Y - BoundsMargin;
        float maxY = MapBounds.End.Y + BoundsMargin;

        _targetPosition.X = Mathf.Clamp(_targetPosition.X, minX, maxX);
        _targetPosition.Y = Mathf.Clamp(_targetPosition.Y, minY, maxY);
    }
}
