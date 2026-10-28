using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Map;
using EastAsiaGSG.GodotClient.Map;
using Godot;

namespace EastAsiaGSG.GodotClient.Tactics;

/// <summary>
/// Reactive 2D tactical presentation overlay rendering NATO division counters,
/// movement/attack arrows, and HOI4-style green/red 0–100 Battle Bubbles directly on the map.
/// </summary>
public partial class TacticalMapOverlay : Node2D
{
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();
    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();

    private SimulationWorld? _world;
    private MapRenderer? _mapRenderer;
    private CameraController? _camera;

    // Selection & Hover State
    public int SelectedDivisionId { get; private set; } = -1;
    public Entity? SelectedDivisionEntity { get; private set; }
    public int HoveredDivisionId { get; private set; } = -1;
    public int SelectedCombatId { get; private set; } = -1;

    // Events
    public event Action<DivisionComponent>? DivisionSelected;
    public event Action? DivisionDeselected;
    public event Action<ActiveCombatComponent>? CombatSelected;
    public event Action<ushort>? MoveOrderIssued;

    private readonly List<(Vector2 visualPos, DivisionComponent div, Entity entity)> _activeDivisions = [];
    private readonly List<(Vector2 center, ActiveCombatComponent combat)> _activeCombats = [];
    private readonly Dictionary<ushort, List<int>> _provincesToDivisions = [];
    private readonly Dictionary<int, ushort> _pendingOrders = [];

    // Pre-allocated click targets for hit-testing
    private readonly List<(Rect2 rect, DivisionComponent div, Entity entity)> _counterClickTargets = [];
    private readonly List<(Vector2 center, float radius, ActiveCombatComponent combat)> _bubbleClickTargets = [];

    private Font? _defaultFont;

    public void Initialize(SimulationWorld world, MapRenderer mapRenderer, CameraController camera)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(mapRenderer);
        ArgumentNullException.ThrowIfNull(camera);

        _world = world;
        _mapRenderer = mapRenderer;
        _camera = camera;
        _defaultFont = ThemeDB.FallbackFont;
    }

    public override void _Process(double delta)
    {
        if (_world == null)
        {
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }

        float zoom = _camera?.Zoom.X ?? 1.0f;
        float counterScale = Mathf.Clamp(0.85f / zoom, 0.55f, 1.4f);
        float lineScale = Mathf.Clamp(1.0f / zoom, 0.7f, 1.3f);
        Font font = _defaultFont ?? ThemeDB.FallbackFont;

        CollectDynamicEntities();

        // 1. Draw Movement & Attack Arrows
        DrawMovementAndAttackArrows(lineScale);

        // 2. Draw Division Counters
        DrawDivisionCounters(counterScale, font);

        // 3. Draw HOI4-style 0..100 Battle Bubbles
        DrawBattleBubbles(counterScale, font);
    }

    private void CollectDynamicEntities()
    {
        _activeDivisions.Clear();
        _activeCombats.Clear();
        _provincesToDivisions.Clear();
        _counterClickTargets.Clear();
        _bubbleClickTargets.Clear();

        if (_world == null)
        {
            return;
        }

        // Collect all active divisions
        Query divQuery = _world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divisions = chunk.GetSpan<DivisionComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int count = chunk.Count;

            for (int i = 0; i < count; i++)
            {
                ref DivisionComponent div = ref divisions[i];
                if (div.State == DivisionState.Shattered || div.CurrentProvinceId >= SimulationWorld.MaxProvinces)
                {
                    continue;
                }

                Vector2 visualPos = CalculateVisualPosition(in div);
                int index = _activeDivisions.Count;
                _activeDivisions.Add((visualPos, div, entities[i]));

                ushort provId = div.CurrentProvinceId;
                if (!_provincesToDivisions.TryGetValue(provId, out List<int>? list))
                {
                    list = [];
                    _provincesToDivisions[provId] = list;
                }
                list.Add(index);
            }
        }

        // Collect all active ongoing combats
        Query combatQuery = _world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in combatQuery)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            int count = chunk.Count;

            for (int i = 0; i < count; i++)
            {
                ref ActiveCombatComponent combat = ref combats[i];
                if (combat.Outcome == CombatOutcome.Ongoing && combat.TargetProvinceId < SimulationWorld.MaxProvinces)
                {
                    ref readonly ProvinceData prov = ref _world.Provinces[combat.TargetProvinceId];
                    Vector2 center = new(prov.CenterX, prov.CenterY);
                    _activeCombats.Add((center, combat));
                }
            }
        }
    }

    private Vector2 CalculateVisualPosition(in DivisionComponent div)
    {
        if (_world == null)
        {
            return Vector2.Zero;
        }

        ref readonly ProvinceData currentProv = ref _world.Provinces[div.CurrentProvinceId];
        Vector2 currentCenter = new(currentProv.CenterX, currentProv.CenterY);

        if ((div.State == DivisionState.Moving || div.State == DivisionState.Retreating)
            && div.TargetProvinceId < SimulationWorld.MaxProvinces
            && div.TargetProvinceId != div.CurrentProvinceId)
        {
            ref readonly ProvinceData targetProv = ref _world.Provinces[div.TargetProvinceId];
            Vector2 targetCenter = new(targetProv.CenterX, targetProv.CenterY);
            float progress = Mathf.Clamp(div.MovementProgress, 0.0f, 1.0f);
            return currentCenter.Lerp(targetCenter, progress);
        }

        return currentCenter;
    }

    #region Drawing: Arrows

    private void DrawMovementAndAttackArrows(float lineScale)
    {
        if (_world == null)
        {
            return;
        }

        for (int i = 0; i < _activeDivisions.Count; i++)
        {
            var (visualPos, div, _) = _activeDivisions[i];

            bool isMoving = div.State == DivisionState.Moving || div.State == DivisionState.Retreating;
            bool isAttacking = div.State == DivisionState.Attacking;

            if (!isMoving && !isAttacking && div.WaypointCount == 0)
            {
                // Immediate preview arrow for queued move/attack orders before tick executes
                if (_pendingOrders.TryGetValue(div.DivisionId, out ushort pendingTargetId)
                    && pendingTargetId < SimulationWorld.MaxProvinces
                    && pendingTargetId != div.CurrentProvinceId)
                {
                    ref readonly ProvinceData pendingProv = ref _world.Provinces[pendingTargetId];
                    Vector2 pendingCenter = new(pendingProv.CenterX, pendingProv.CenterY);
                    bool isHostile = pendingProv.ControllerTag != div.CountryTag
                        && pendingProv.ControllerTag != CountryTag.None;

                    Color previewColor = isHostile
                        ? new Color(0.95f, 0.28f, 0.28f, 0.90f) // Bright red attack preview
                        : new Color(0.20f, 0.92f, 0.50f, 0.90f); // Bright green move preview

                    float pLineWidth = 3.5f * lineScale;
                    DrawLine(visualPos, pendingCenter, new Color(0, 0, 0, 0.5f), pLineWidth + 2.0f);
                    DrawLine(visualPos, pendingCenter, previewColor, pLineWidth);
                    DrawArrowHead(visualPos, pendingCenter, previewColor, 10.0f * lineScale);
                }
                continue;
            }
            else
            {
                _pendingOrders.Remove(div.DivisionId);
            }

            if (div.TargetProvinceId >= SimulationWorld.MaxProvinces)
            {
                continue;
            }

            ref readonly ProvinceData targetProv = ref _world.Provinces[div.TargetProvinceId];
            Vector2 targetCenter = new(targetProv.CenterX, targetProv.CenterY);

            // Determine line style
            Color arrowColor = isAttacking
                ? new Color(0.92f, 0.22f, 0.22f, 0.85f) // Red attack vector
                : (div.State == DivisionState.Retreating
                    ? new Color(1.0f, 0.65f, 0.0f, 0.80f) // Amber retreat vector
                    : new Color(0.12f, 0.88f, 0.45f, 0.85f)); // Green movement vector

            float lineWidth = 3.5f * lineScale;

            // Draw line from visual position to target centroid
            DrawLine(visualPos, targetCenter, new Color(0, 0, 0, 0.5f), lineWidth + 2.0f);
            DrawLine(visualPos, targetCenter, arrowColor, lineWidth);

            // Draw arrowhead at target center
            DrawArrowHead(visualPos, targetCenter, arrowColor, 10.0f * lineScale);

            // Draw subsequent planned waypoints
            Vector2 prev = targetCenter;
            for (int w = div.CurrentWaypointIndex + 1; w < div.WaypointCount; w++)
            {
                ushort nextProvId = div.GetWaypoint(w);
                if (nextProvId >= SimulationWorld.MaxProvinces)
                {
                    break;
                }

                ref readonly ProvinceData nextProv = ref _world.Provinces[nextProvId];
                Vector2 nextCenter = new(nextProv.CenterX, nextProv.CenterY);

                Color futureColor = new(arrowColor.R, arrowColor.G, arrowColor.B, 0.50f);
                DrawLine(prev, nextCenter, new Color(0, 0, 0, 0.3f), lineWidth);
                DrawLine(prev, nextCenter, futureColor, lineWidth - 1.0f);
                prev = nextCenter;
            }
        }

        // Draw converging attack vectors from combat origin provinces
        for (int c = 0; c < _activeCombats.Count; c++)
        {
            var (targetCenter, combat) = _activeCombats[c];
            unsafe
            {
                for (int o = 0; o < combat.AttackDirectionCount; o++)
                {
                    ushort originId = combat.AttackOriginProvinces[o];
                    if (originId < SimulationWorld.MaxProvinces)
                    {
                        ref readonly ProvinceData originProv = ref _world.Provinces[originId];
                        Vector2 originCenter = new(originProv.CenterX, originProv.CenterY);
                        Color attackLineColor = new(0.95f, 0.15f, 0.15f, 0.65f);
                        DrawLine(originCenter, targetCenter, attackLineColor, 2.5f * lineScale);
                    }
                }
            }
        }
    }

    private void DrawArrowHead(Vector2 from, Vector2 to, Color color, float size)
    {
        Vector2 dir = (to - from).Normalized();
        if (dir.LengthSquared() < 0.001f)
        {
            return;
        }

        Vector2 normal = new(-dir.Y, dir.X);
        Vector2 p1 = to;
        Vector2 p2 = to - dir * size + normal * (size * 0.55f);
        Vector2 p3 = to - dir * size - normal * (size * 0.55f);

        Vector2[] triangle = [p1, p2, p3];
        DrawColoredPolygon(triangle, color);
        DrawPolyline([p1, p2, p3, p1], new Color(0, 0, 0, 0.7f), 1.0f);
    }

    #endregion

    #region Drawing: Division Counters

    private void DrawDivisionCounters(float counterScale, Font font)
    {
        float baseWidth = 40.0f * counterScale;
        float baseHeight = 22.0f * counterScale;
        float spacing = 18.0f * counterScale;

        foreach (var (provId, indices) in _provincesToDivisions)
        {
            int groupCount = indices.Count;

            for (int k = 0; k < groupCount; k++)
            {
                int divIndex = indices[k];
                var (basePos, div, entity) = _activeDivisions[divIndex];

                // Stacking offset across X-axis
                float offsetX = (k - (groupCount - 1) * 0.5f) * spacing;
                Vector2 counterCenter = basePos + new Vector2(offsetX, 0);

                Rect2 counterRect = new(
                    counterCenter - new Vector2(baseWidth * 0.5f, baseHeight * 0.5f),
                    new Vector2(baseWidth, baseHeight));

                _counterClickTargets.Add((counterRect, div, entity));

                bool isSelected = div.DivisionId == SelectedDivisionId;
                bool isHovered = div.DivisionId == HoveredDivisionId;

                // 1. Background Fill with Country Color Tone
                Color countryColor = GetCountryColor(div.CountryTag);
                Color bgColor = new(countryColor.R * 0.45f, countryColor.G * 0.45f, countryColor.B * 0.45f, 0.95f);
                DrawRect(counterRect, bgColor, filled: true);

                // 2. Selection / Hover Border
                if (isSelected)
                {
                    // Gold selected border + glowing outline
                    DrawRect(counterRect.Grow(1.5f * counterScale), new Color(1.0f, 0.88f, 0.2f, 0.6f), filled: false, width: 2.0f * counterScale);
                    DrawRect(counterRect, new Color(1.0f, 0.84f, 0.0f), filled: false, width: 2.8f * counterScale);

                    // Downward gold selection indicator triangle above counter
                    Vector2 tip = new(counterCenter.X, counterRect.Position.Y - 2.0f * counterScale);
                    Vector2 b1 = new(counterCenter.X - 5.0f * counterScale, counterRect.Position.Y - 9.0f * counterScale);
                    Vector2 b2 = new(counterCenter.X + 5.0f * counterScale, counterRect.Position.Y - 9.0f * counterScale);
                    DrawColoredPolygon([tip, b1, b2], new Color(1.0f, 0.84f, 0.0f));
                }
                else if (isHovered)
                {
                    // Cyan hover border
                    DrawRect(counterRect, new Color(0.3f, 0.9f, 1.0f), filled: false, width: 1.8f * counterScale);
                }
                else
                {
                    // Subtle dark outline
                    DrawRect(counterRect, new Color(0.1f, 0.1f, 0.1f, 0.8f), filled: false, width: 1.2f * counterScale);
                }

                // 3. NATO Military Unit Symbol in Center
                DrawNatoSymbol(counterRect, in div, countryColor, counterScale);

                // 4. Organization Bar (Left Vertical Edge)
                float barWidth = 3.0f * counterScale;
                float orgRatio = Mathf.Clamp(div.Organization / Mathf.Max(1.0f, div.MaxOrganization), 0.0f, 1.0f);
                Color orgColor = orgRatio > 0.50f
                    ? new Color(0.20f, 0.85f, 0.30f)
                    : (orgRatio > 0.25f ? new Color(1.0f, 0.75f, 0.10f) : new Color(0.90f, 0.20f, 0.20f));

                float orgHeight = (counterRect.Size.Y - 2.0f) * orgRatio;
                Rect2 orgRect = new(
                    counterRect.Position.X + 1.0f,
                    counterRect.Position.Y + counterRect.Size.Y - 1.0f - orgHeight,
                    barWidth,
                    orgHeight);
                DrawRect(orgRect, orgColor, filled: true);

                // 5. Strength Bar (Right Vertical Edge)
                float strRatio = Mathf.Clamp(div.Strength, 0.0f, 1.0f);
                Color strColor = new(1.0f, 0.60f, 0.10f); // Amber
                float strHeight = (counterRect.Size.Y - 2.0f) * strRatio;
                Rect2 strRect = new(
                    counterRect.Position.X + counterRect.Size.X - 1.0f - barWidth,
                    counterRect.Position.Y + counterRect.Size.Y - 1.0f - strHeight,
                    barWidth,
                    strHeight);
                DrawRect(strRect, strColor, filled: true);

                // 6. Country Tag Label Badge
                string tagText = CountryTag.GetCode(div.CountryTag);
                int fontSize = Mathf.Clamp(Mathf.RoundToInt(9.0f * counterScale), 7, 13);
                Vector2 textPos = new(counterRect.Position.X, counterRect.Position.Y + counterRect.Size.Y - 2.0f);
                DrawString(font, textPos, tagText, HorizontalAlignment.Center, counterRect.Size.X, fontSize, new Color(1, 1, 1, 0.9f));
            }
        }
    }

    private void DrawNatoSymbol(Rect2 rect, in DivisionComponent div, Color countryColor, float scale)
    {
        // Inner NATO bounding box
        float insetX = 6.0f * scale;
        float insetY = 3.0f * scale;
        Rect2 inner = new(
            rect.Position.X + insetX,
            rect.Position.Y + insetY,
            rect.Size.X - insetX * 2.0f,
            rect.Size.Y - insetY * 2.0f - 4.0f * scale);

        Color symbolColor = new(1.0f, 1.0f, 1.0f, 0.90f);

        // Standard NATO Frame
        DrawRect(inner, countryColor, filled: true);
        DrawRect(inner, symbolColor, filled: false, width: 1.0f * scale);

        if (div.Hardness >= 0.55f)
        {
            // Armor NATO Symbol: Track Oval
            Rect2 oval = new(inner.Position.X + 2.0f * scale, inner.Position.Y + 2.0f * scale, inner.Size.X - 4.0f * scale, inner.Size.Y - 4.0f * scale);
            DrawRect(oval, symbolColor, filled: false, width: 1.2f * scale);
        }
        else if (div.Hardness >= 0.30f)
        {
            // Mechanized NATO Symbol: Diagonal Cross 'X' + Center Oval
            DrawLine(inner.Position, inner.Position + inner.Size, symbolColor, 1.0f * scale);
            DrawLine(new Vector2(inner.Position.X, inner.Position.Y + inner.Size.Y),
                     new Vector2(inner.Position.X + inner.Size.X, inner.Position.Y), symbolColor, 1.0f * scale);
            Vector2 center = inner.Position + inner.Size * 0.5f;
            DrawCircle(center, 2.5f * scale, symbolColor, filled: true);
        }
        else
        {
            // Infantry NATO Symbol: Diagonal Cross 'X'
            DrawLine(inner.Position, inner.Position + inner.Size, symbolColor, 1.2f * scale);
            DrawLine(new Vector2(inner.Position.X, inner.Position.Y + inner.Size.Y),
                     new Vector2(inner.Position.X + inner.Size.X, inner.Position.Y), symbolColor, 1.2f * scale);
        }
    }

    #endregion

    #region Drawing: HOI4-Style 0..100 Battle Bubbles

    private void DrawBattleBubbles(float counterScale, Font font)
    {
        float radius = 14.0f * counterScale;

        for (int i = 0; i < _activeCombats.Count; i++)
        {
            var (center, combat) = _activeCombats[i];

            _bubbleClickTargets.Add((center, radius * 1.2f, combat));

            bool isSelected = combat.CombatId == SelectedCombatId;
            float progress = Mathf.Clamp(combat.BattleProgress, 0.0f, 100.0f);
            int progressVal = Mathf.RoundToInt(progress);

            // HOI4 Battle Bubble 3-tier color palette:
            // >= 55: Green (attacker winning)
            // 45..54: Amber (contested)
            // <= 44: Red (defender winning)
            Color bubbleColor = progress >= 55.0f
                ? new Color(0.18f, 0.72f, 0.22f, 0.95f)
                : (progress >= 45.0f
                    ? new Color(0.85f, 0.65f, 0.12f, 0.95f)
                    : new Color(0.82f, 0.18f, 0.18f, 0.95f));

            // Outer dark drop shadow / outline
            DrawCircle(center, radius + 2.5f * counterScale, new Color(0.08f, 0.08f, 0.08f, 0.85f), filled: true);

            // Main bubble body
            DrawCircle(center, radius, bubbleColor, filled: true);

            // Selection or intensity border
            if (isSelected)
            {
                DrawCircle(center, radius + 3.0f * counterScale, new Color(1.0f, 0.84f, 0.0f), filled: false, width: 2.0f * counterScale);
            }
            else
            {
                DrawCircle(center, radius, new Color(1, 1, 1, 0.85f), filled: false, width: 1.2f * counterScale);
            }

            // Progress number (0..100) centered
            string progressText = progressVal.ToString();
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(11.0f * counterScale), 8, 16);
            Vector2 textPos = new(center.X - radius, center.Y + (fontSize * 0.35f));
            DrawString(font, textPos, progressText, HorizontalAlignment.Center, radius * 2.0f, fontSize, new Color(1, 1, 1));
        }
    }

    #endregion

    #region Input Handling & Hit Testing

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_world == null || _mapRenderer == null)
        {
            return;
        }

        Vector2 mouseGlobal = GetGlobalMousePosition();

        if (@event is InputEventMouseMotion)
        {
            UpdateHoverState(mouseGlobal);
        }
        else if (@event is InputEventMouseButton { Pressed: true } mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                // Priority 1: Check Battle Bubble click
                for (int i = 0; i < _bubbleClickTargets.Count; i++)
                {
                    var (center, radius, combat) = _bubbleClickTargets[i];
                    if (mouseGlobal.DistanceTo(center) <= radius)
                    {
                        SelectedCombatId = combat.CombatId;
                        CombatSelected?.Invoke(combat);
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                }

                // Priority 2: Check Division Counter click (with generous padding)
                for (int i = 0; i < _counterClickTargets.Count; i++)
                {
                    var (rect, div, entity) = _counterClickTargets[i];
                    Rect2 hitRect = rect.Grow(14.0f);
                    if (hitRect.HasPoint(mouseGlobal))
                    {
                        SelectedDivisionId = div.DivisionId;
                        SelectedDivisionEntity = entity;
                        DivisionSelected?.Invoke(div);
                        GetViewport().SetInputAsHandled();
                        QueueRedraw();
                        return;
                    }
                }

                // Priority 3: Check if clicked province has stationed divisions
                ushort clickedProvince = _mapRenderer.PickProvinceAtGlobalPosition(mouseGlobal);
                if (clickedProvince < SimulationWorld.MaxProvinces)
                {
                    if (_provincesToDivisions.TryGetValue(clickedProvince, out List<int>? divIndices) && divIndices.Count > 0)
                    {
                        int divIndex = divIndices[0];
                        var (_, div, entity) = _activeDivisions[divIndex];
                        SelectedDivisionId = div.DivisionId;
                        SelectedDivisionEntity = entity;
                        DivisionSelected?.Invoke(div);
                        GetViewport().SetInputAsHandled();
                        QueueRedraw();
                        return;
                    }
                }

                // If clicked outside counters, bubbles, and division provinces, deselect division
                if (SelectedDivisionId != -1)
                {
                    SelectedDivisionId = -1;
                    SelectedDivisionEntity = null;
                    DivisionDeselected?.Invoke();
                    QueueRedraw();
                }
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Right)
            {
                // Right Click with selected division: Issue Move / Attack Order
                if (SelectedDivisionId != -1 && SelectedDivisionEntity.HasValue && _world.EcsWorld.IsAlive(SelectedDivisionEntity.Value))
                {
                    ushort targetProvinceId = _mapRenderer.PickProvinceAtGlobalPosition(mouseGlobal);
                    if (targetProvinceId < SimulationWorld.MaxProvinces)
                    {
                        ref DivisionComponent div = ref _world.EcsWorld.Get<DivisionComponent>(SelectedDivisionEntity.Value);
                        if (targetProvinceId != div.CurrentProvinceId)
                        {
                            _pendingOrders[div.DivisionId] = targetProvinceId;
                            _world.EnqueueCommand(SimulationCommand.CreateMoveDivision(div.CountryTag, div.DivisionId, targetProvinceId));
                            MoveOrderIssued?.Invoke(targetProvinceId);
                            GetViewport().SetInputAsHandled();
                            QueueRedraw();
                            return;
                        }
                    }
                }
            }
        }
    }

    private void UpdateHoverState(Vector2 mouseGlobal)
    {
        int prevHover = HoveredDivisionId;
        HoveredDivisionId = -1;

        for (int i = 0; i < _counterClickTargets.Count; i++)
        {
            var (rect, div, _) = _counterClickTargets[i];
            Rect2 hitRect = rect.Grow(8.0f);
            if (hitRect.HasPoint(mouseGlobal))
            {
                HoveredDivisionId = div.DivisionId;
                break;
            }
        }

        if (prevHover != HoveredDivisionId)
        {
            QueueRedraw();
        }
    }

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color GetCountryColor(byte tag)
    {
        Color32 c = MapLutBuffer.GetPoliticalColor(tag);
        return new Color(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f);
    }
}
