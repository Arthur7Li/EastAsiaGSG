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
    [Export] public float SmoothDamping { get; set; } = 16.0f;
    [Export] public Rect2 MapBounds { get; set; } = new(0, 0, 1024, 1024);
    [Export] public float BoundsMargin { get; set; } = 150f;
    [Export] public float GestureZoomSensitivity { get; set; } = 1.0f;
    [Export] public bool TrackpadScrollZooms { get; set; } = false;

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
        // 1. Mac trackpad pinch-to-zoom gesture
        if (@event is InputEventMagnifyGesture magnify)
        {
            float factor = 1.0f + (magnify.Factor - 1.0f) * GestureZoomSensitivity;
            if (factor > 0.001f)
            {
                ApplyCursorCenteredZoom(factor, magnify.Position);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // 2. Mac trackpad two-finger pan & scroll gesture
        if (@event is InputEventPanGesture pan)
        {
            // Check if user is holding Command (Meta on macOS), Control, or Option (Alt), or if TrackpadScrollZooms is active
            bool isZoomModifier = pan.MetaPressed || pan.CtrlPressed || pan.AltPressed
                || Input.IsKeyPressed(Key.Meta) || Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Alt)
                || TrackpadScrollZooms;

            if (isZoomModifier && Mathf.Abs(pan.Delta.Y) > 0.0001f)
            {
                // Smooth trackpad scroll zoom: scroll up (negative delta) zooms in, scroll down zooms out
                float clampedDeltaY = Mathf.Clamp(pan.Delta.Y, -10f, 10f);
                float zoomFactor = 1.0f - clampedDeltaY * (ZoomSpeed * 0.2f);
                zoomFactor = Mathf.Clamp(zoomFactor, 0.5f, 2.0f);
                ApplyCursorCenteredZoom(zoomFactor, pan.Position);
                GetViewport().SetInputAsHandled();
                return;
            }

            // Default two-finger trackpad drag smoothly pans the camera
            Vector2 panOffset = (pan.Delta * PanSpeed * 0.15f) / _targetZoom;
            _targetPosition += panOffset;
            ClampTargetPosition();
            GetViewport().SetInputAsHandled();
            return;
        }

        // 3. Traditional mouse input (Wheel zoom & Middle click drag-pan)
        if (@event is InputEventMouseButton mouseBtn)
        {
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
                    ApplyCursorCenteredZoom(zoomFactor, mouseBtn.Position);
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            // Middle mouse button drag-panning (Right mouse reserved for tactical unit orders)
            if (mouseBtn.ButtonIndex == MouseButton.Middle)
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

        // 4. Keyboard zoom fallback (+ / - / = / _)
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            float keyZoomFactor = 0f;
            if (keyEvent.Keycode == Key.Equal || keyEvent.Keycode == Key.Plus || keyEvent.Keycode == Key.KpAdd)
            {
                keyZoomFactor = 1.0f + ZoomSpeed;
            }
            else if (keyEvent.Keycode == Key.Minus || keyEvent.Keycode == Key.KpSubtract)
            {
                keyZoomFactor = 1.0f / (1.0f + ZoomSpeed);
            }

            if (keyZoomFactor != 0f)
            {
                ApplyCursorCenteredZoom(keyZoomFactor);
                GetViewport().SetInputAsHandled();
                return;
            }
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

    private void ApplyCursorCenteredZoom(float zoomFactor, Vector2? screenPoint = null)
    {
        float oldZoom = _targetZoom;
        float newZoom = Mathf.Clamp(oldZoom * zoomFactor, MinZoom, MaxZoom);

        if (Mathf.Abs(newZoom - oldZoom) <= 0.0001f)
        {
            return;
        }

        Viewport? viewport = GetViewport();
        if (viewport == null)
        {
            return;
        }

        Vector2 screenPos = screenPoint ?? viewport.GetMousePosition();
        Vector2 viewportCenter = viewport.GetVisibleRect().Size * 0.5f;
        Vector2 screenOffset = screenPos - viewportCenter;

        _targetZoom = newZoom;

        // Shift camera target position so the world coordinate under screenPos stays constant
        _targetPosition += screenOffset * (1.0f / oldZoom - 1.0f / newZoom);
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
