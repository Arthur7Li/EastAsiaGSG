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
    public byte ActivePlayerCountryTag { get; set; } = CountryTag.ROK;
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
    private readonly List<(Rect2 rect, List<int> divIndices)> _counterClickTargets = [];
    private readonly List<(Vector2 center, float radius, ActiveCombatComponent combat)> _bubbleClickTargets = [];

    // Pre-allocated buffers to prevent GC allocations during draw
    private readonly Vector2[] _arrowTriBuffer = new Vector2[3];
    private readonly Vector2[] _arrowPolyBuffer = new Vector2[4];

    private Vector2 _lastCameraPos;
    private Vector2 _lastCameraZoom;
    private int _lastTick = -1;
    private bool _entitiesDirty = true;

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
        _entitiesDirty = true;
    }

    public void OnTick()
    {
        _entitiesDirty = true;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_world == null)
        {
            return;
        }

        Vector2 camPos = _camera?.Position ?? Vector2.Zero;
        Vector2 camZoom = _camera?.Zoom ?? Vector2.One;
        int currentTick = _world.TotalSixHourTicks;

        if (currentTick != _lastTick)
        {
            _lastTick = currentTick;
            _entitiesDirty = true;
        }

        if (camPos != _lastCameraPos || camZoom != _lastCameraZoom || _entitiesDirty)
        {
            _lastCameraPos = camPos;
            _lastCameraZoom = camZoom;
            QueueRedraw();
        }
    }

    private static readonly (ushort ProvinceId, string Label, bool IsCapital)[] StrategicLandmarks =
    [
        (0, "★ SEOUL", true),
        (60, "★ PYONGYANG", true),
        (56, "Busan", false),
        (102, "Wonsan", false),
        (119, "Chongjin", false),
        (200, "★ BEIJING", true),
        (210, "Shenyang", false),
        (212, "Dalian", false),
        (221, "Qingdao", false),
        (230, "Shanghai", false),
        (240, "Fuzhou", false),
        (250, "Guangzhou", false),
        (171, "★ VLADIVOSTOK", true),
        (163, "Khabarovsk", false),
        (150, "Irkutsk Gate", false),
        (180, "Sakhalin", false),
        (300, "★ TOKYO", true),
        (310, "Sapporo", false),
        (320, "Osaka", false),
        (330, "Fukuoka", false),
        (400, "★ TAIPEI", true),
        (410, "Kaohsiung", false),
        (500, "★ OKINAWA", true),
        (510, "Guam", false),
        (600, "★ HANOI", true),
        (603, "Cam Ranh", false),
        (610, "★ MANILA", true),
        (620, "★ ULAANBAATAR", true),
        (630, "★ BANGKOK", true),
        (640, "★ VIENTIANE", true),
        (650, "★ PHNOM PENH", true),
        (660, "★ YANGON", true),
        (670, "★ KUALA LUMPUR", true)
    ];

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }

        if (_entitiesDirty)
        {
            CollectDynamicEntities();
            _entitiesDirty = false;
        }

        Vector2 camPos = _camera?.Position ?? Vector2.Zero;
        Vector2 camZoom = _camera?.Zoom ?? Vector2.One;
        Vector2 viewSize = GetViewportRect().Size;
        Vector2 halfViewWorld = (viewSize * 0.5f) / camZoom;
        Rect2 visibleWorldRect = new(camPos - halfViewWorld, halfViewWorld * 2.0f);
        Rect2 cullRect = visibleWorldRect.Grow(100.0f);

        float zoom = camZoom.X;
        float counterScale = Mathf.Clamp(0.85f / zoom, 0.55f, 1.4f);
        float lineScale = Mathf.Clamp(1.0f / zoom, 0.7f, 1.3f);
        Font font = _defaultFont ?? ThemeDB.FallbackFont;

        _counterClickTargets.Clear();
        _bubbleClickTargets.Clear();

        // 0. Draw Strategic Landmarks & Capital Labels
        DrawStrategicLandmarks(zoom, font, cullRect);

        // 1. Draw Movement & Attack Arrows
        DrawMovementAndAttackArrows(lineScale, cullRect);

        // 2. Draw Division Counters
        DrawDivisionCounters(counterScale, font, cullRect);

        // 3. Draw HOI4-style 0..100 Battle Bubbles
        DrawBattleBubbles(counterScale, font, cullRect);
    }

    private void DrawStrategicLandmarks(float zoom, Font font, Rect2 cullRect)
    {
        if (_world == null || font == null)
        {
            return;
        }

        int fontSize = zoom >= 1.2f ? 12 : 11;
        for (int i = 0; i < StrategicLandmarks.Length; i++)
        {
            var (provId, label, isCapital) = StrategicLandmarks[i];
            if (!isCapital && zoom < 0.55f)
            {
                continue;
            }
            if (provId >= SimulationWorld.MaxProvinces)
            {
                continue;
            }

            ref readonly ProvinceData prov = ref _world.Provinces[provId];
            if (prov.CenterX == 0 && prov.CenterY == 0)
            {
                continue;
            }

            Vector2 pos = new(prov.CenterX, prov.CenterY);
            if (!cullRect.HasPoint(pos))
            {
                continue;
            }

            Color dotColor = isCapital ? new Color(1.0f, 0.85f, 0.20f, 0.95f) : new Color(0.92f, 0.95f, 1.0f, 0.85f);
            float dotRadius = isCapital ? 4.0f : 2.8f;

            DrawCircle(pos, dotRadius + 1.2f, new Color(0f, 0f, 0f, 0.85f));
            DrawCircle(pos, dotRadius, dotColor);

            Vector2 textPos = pos + new Vector2(6f, -6f);
            DrawStringOutline(font, textPos, label, HorizontalAlignment.Left, -1, fontSize, 3, new Color(0.02f, 0.04f, 0.08f, 0.92f));
            DrawString(font, textPos, label, HorizontalAlignment.Left, -1, fontSize, isCapital ? new Color(1.0f, 0.94f, 0.68f, 0.98f) : new Color(0.90f, 0.94f, 0.98f, 0.90f));
        }
    }

    private void CollectDynamicEntities()
    {
        _activeDivisions.Clear();
        _activeCombats.Clear();
        foreach (var list in _provincesToDivisions.Values)
        {
            list.Clear();
        }

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

    private void DrawMovementAndAttackArrows(float lineScale, Rect2 cullRect)
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

                    if (!cullRect.HasPoint(visualPos) && !cullRect.HasPoint(pendingCenter))
                    {
                        continue;
                    }

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

            if (!cullRect.HasPoint(visualPos) && !cullRect.HasPoint(targetCenter))
            {
                continue;
            }

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

                if (cullRect.HasPoint(prev) || cullRect.HasPoint(nextCenter))
                {
                    Color futureColor = new(arrowColor.R, arrowColor.G, arrowColor.B, 0.50f);
                    DrawLine(prev, nextCenter, new Color(0, 0, 0, 0.3f), lineWidth);
                    DrawLine(prev, nextCenter, futureColor, lineWidth - 1.0f);
                }
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

                        if (!cullRect.HasPoint(originCenter) && !cullRect.HasPoint(targetCenter))
                        {
                            continue;
                        }

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

        _arrowTriBuffer[0] = p1;
        _arrowTriBuffer[1] = p2;
        _arrowTriBuffer[2] = p3;
        DrawColoredPolygon(_arrowTriBuffer, color);

        _arrowPolyBuffer[0] = p1;
        _arrowPolyBuffer[1] = p2;
        _arrowPolyBuffer[2] = p3;
        _arrowPolyBuffer[3] = p1;
        DrawPolyline(_arrowPolyBuffer, new Color(0, 0, 0, 0.7f), 1.0f);
    }

    #endregion

    #region Drawing: Division Counters

    private void DrawDivisionCounters(float counterScale, Font font, Rect2 cullRect)
    {
        float baseWidth = 40.0f * counterScale;
        float baseHeight = 22.0f * counterScale;

        Span<int> stationaryIndices = stackalloc int[128];
        Span<int> countryCount = stackalloc int[SimulationWorld.MaxCountries];

        foreach (var (provId, indices) in _provincesToDivisions)
        {
            if (indices.Count == 0 || provId >= SimulationWorld.MaxProvinces)
            {
                continue;
            }

            int stationaryCount = 0;
            int maxStationary = stationaryIndices.Length;

            for (int k = 0; k < indices.Count; k++)
            {
                int divIdx = indices[k];
                var (visualPos, div, _) = _activeDivisions[divIdx];
                if (div.State == DivisionState.Moving || div.State == DivisionState.Retreating)
                {
                    // Draw moving unit individually
                    Vector2 counterCenter = visualPos;
                    Rect2 counterRect = new(
                        counterCenter - new Vector2(baseWidth * 0.5f, baseHeight * 0.5f),
                        new Vector2(baseWidth, baseHeight));

                    if (cullRect.Intersects(counterRect))
                    {
                        var singleList = new List<int>(1) { divIdx };
                        _counterClickTargets.Add((counterRect, singleList));
                        DrawSingleCounter(counterRect, in div, 1, counterScale, font);
                    }
                }
                else if (stationaryCount < maxStationary)
                {
                    stationaryIndices[stationaryCount++] = divIdx;
                }
            }

            if (stationaryCount == 0)
            {
                continue;
            }

            // Group stationary units by CountryTag
            countryCount.Clear();

            for (int s = 0; s < stationaryCount; s++)
            {
                int divIdx = stationaryIndices[s];
                byte tag = _activeDivisions[divIdx].div.CountryTag;
                if (tag < SimulationWorld.MaxCountries)
                {
                    countryCount[tag]++;
                }
            }

            int presentCountryIndex = 0;
            int totalPresentCountries = 0;
            for (int c = 0; c < SimulationWorld.MaxCountries; c++)
            {
                if (countryCount[c] > 0) totalPresentCountries++;
            }

            for (int c = 0; c < SimulationWorld.MaxCountries; c++)
            {
                int stackSize = countryCount[c];
                if (stackSize == 0)
                {
                    continue;
                }

                var stackDivIndices = new List<int>(stackSize);
                for (int s = 0; s < stationaryCount; s++)
                {
                    int divIdx = stationaryIndices[s];
                    if (_activeDivisions[divIdx].div.CountryTag == c)
                    {
                        stackDivIndices.Add(divIdx);
                    }
                }

                ref readonly ProvinceData prov = ref _world!.Provinces[provId];
                Vector2 basePos = new(prov.CenterX, prov.CenterY);

                float multiCountryOffset = totalPresentCountries > 1
                    ? (presentCountryIndex - (totalPresentCountries - 1) * 0.5f) * (baseWidth + 4.0f)
                    : 0.0f;
                presentCountryIndex++;

                Vector2 counterCenter = basePos + new Vector2(multiCountryOffset, 0);
                Rect2 counterRect = new(
                    counterCenter - new Vector2(baseWidth * 0.5f, baseHeight * 0.5f),
                    new Vector2(baseWidth, baseHeight));

                if (!cullRect.Intersects(counterRect))
                {
                    continue;
                }

                _counterClickTargets.Add((counterRect, stackDivIndices));

                int dominantDivIdx = stackDivIndices[0];
                bool anySelected = false;
                for (int s = 0; s < stackDivIndices.Count; s++)
                {
                    int dIdx = stackDivIndices[s];
                    if (_activeDivisions[dIdx].div.DivisionId == SelectedDivisionId)
                    {
                        dominantDivIdx = dIdx;
                        anySelected = true;
                        break;
                    }
                }

                if (!anySelected)
                {
                    float bestScore = -1f;
                    for (int s = 0; s < stackDivIndices.Count; s++)
                    {
                        int dIdx = stackDivIndices[s];
                        DivisionComponent d = _activeDivisions[dIdx].div;
                        float score = d.Hardness * 100f + (d.Breakthrough > 0 ? 10f : 0f) + d.MaxOrganization;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            dominantDivIdx = dIdx;
                        }
                    }
                }

                var dominantItem = _activeDivisions[dominantDivIdx];
                DrawSingleCounter(counterRect, in dominantItem.div, stackSize, counterScale, font);
            }
        }
    }

    private void DrawSingleCounter(
        Rect2 counterRect,
        in DivisionComponent div,
        int stackCount,
        float counterScale,
        Font font)
    {
        bool isSelected = div.DivisionId == SelectedDivisionId;
        bool isHovered = div.DivisionId == HoveredDivisionId;

        // 1. Background Fill with Country Color Tone
        Color countryColor = GetCountryColor(div.CountryTag);
        Color bgColor = new(countryColor.R * 0.45f, countryColor.G * 0.45f, countryColor.B * 0.45f, 0.95f);
        DrawRect(counterRect, bgColor, filled: true);

        // 2. Selection / Hover Border
        Vector2 counterCenter = counterRect.GetCenter();
        if (isSelected)
        {
            DrawRect(counterRect.Grow(1.5f * counterScale), new Color(1.0f, 0.88f, 0.2f, 0.6f), filled: false, width: 2.0f * counterScale);
            DrawRect(counterRect, new Color(1.0f, 0.84f, 0.0f), filled: false, width: 2.8f * counterScale);

            Vector2 tip = new(counterCenter.X, counterRect.Position.Y - 2.0f * counterScale);
            Vector2 b1 = new(counterCenter.X - 5.0f * counterScale, counterRect.Position.Y - 9.0f * counterScale);
            Vector2 b2 = new(counterCenter.X + 5.0f * counterScale, counterRect.Position.Y - 9.0f * counterScale);
            _arrowTriBuffer[0] = tip;
            _arrowTriBuffer[1] = b1;
            _arrowTriBuffer[2] = b2;
            DrawColoredPolygon(_arrowTriBuffer, new Color(1.0f, 0.84f, 0.0f));
        }
        else if (isHovered)
        {
            DrawRect(counterRect, new Color(0.3f, 0.9f, 1.0f), filled: false, width: 1.8f * counterScale);
        }
        else
        {
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

        // 7. Stack Count Badge (if stackCount > 1)
        if (stackCount > 1)
        {
            float badgeW = 16.0f * counterScale;
            float badgeH = 11.0f * counterScale;
            Rect2 badgeRect = new(
                counterRect.Position.X + counterRect.Size.X - badgeW * 0.65f,
                counterRect.Position.Y - badgeH * 0.45f,
                badgeW,
                badgeH);

            DrawRect(badgeRect, new Color(0.08f, 0.10f, 0.14f, 0.95f), filled: true);
            DrawRect(badgeRect, isSelected ? new Color(1.0f, 0.84f, 0.0f) : new Color(0.7f, 0.75f, 0.85f, 0.85f), filled: false, width: 1.0f * counterScale);

            int badgeFontSize = Mathf.Clamp(Mathf.RoundToInt(8.5f * counterScale), 7, 11);
            Vector2 badgeTextPos = new(badgeRect.Position.X, badgeRect.Position.Y + badgeRect.Size.Y - 1.5f);
            DrawString(font, badgeTextPos, $"x{stackCount}", HorizontalAlignment.Center, badgeW, badgeFontSize, new Color(1.0f, 0.95f, 0.85f));
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

    private void DrawBattleBubbles(float counterScale, Font font, Rect2 cullRect)
    {
        float radius = 14.0f * counterScale;

        for (int i = 0; i < _activeCombats.Count; i++)
        {
            var (center, combat) = _activeCombats[i];
            if (!cullRect.HasPoint(center))
            {
                continue;
            }

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

                // Priority 2: Check Division Counter / Stack click (with generous padding and cycle selection)
                for (int i = 0; i < _counterClickTargets.Count; i++)
                {
                    var (rect, divIndices) = _counterClickTargets[i];
                    Rect2 hitRect = rect.Grow(14.0f);
                    if (hitRect.HasPoint(mouseGlobal) && divIndices.Count > 0)
                    {
                        int currentLocalIdx = -1;
                        for (int k = 0; k < divIndices.Count; k++)
                        {
                            if (_activeDivisions[divIndices[k]].div.DivisionId == SelectedDivisionId)
                            {
                                currentLocalIdx = k;
                                break;
                            }
                        }

                        int nextLocalIdx = (currentLocalIdx + 1) % divIndices.Count;
                        int nextDivIdx = divIndices[nextLocalIdx];
                        var (_, div, entity) = _activeDivisions[nextDivIdx];

                        SelectedDivisionId = div.DivisionId;
                        SelectedDivisionEntity = entity;
                        DivisionSelected?.Invoke(div);
                        GetViewport().SetInputAsHandled();
                        QueueRedraw();
                        return;
                    }
                }

                // Priority 3: Check if clicked province has stationed divisions & cycle
                ushort clickedProvince = _mapRenderer.PickProvinceAtGlobalPosition(mouseGlobal);
                if (clickedProvince < SimulationWorld.MaxProvinces)
                {
                    if (_provincesToDivisions.TryGetValue(clickedProvince, out List<int>? divIndices) && divIndices.Count > 0)
                    {
                        int currentLocalIdx = -1;
                        for (int k = 0; k < divIndices.Count; k++)
                        {
                            if (_activeDivisions[divIndices[k]].div.DivisionId == SelectedDivisionId)
                            {
                                currentLocalIdx = k;
                                break;
                            }
                        }

                        int nextLocalIdx = (currentLocalIdx + 1) % divIndices.Count;
                        int nextDivIdx = divIndices[nextLocalIdx];
                        var (_, div, entity) = _activeDivisions[nextDivIdx];

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
                    ref DivisionComponent div = ref _world.EcsWorld.Get<DivisionComponent>(SelectedDivisionEntity.Value);
                    if (div.CountryTag != ActivePlayerCountryTag)
                    {
                        return;
                    }

                    ushort targetProvinceId = _mapRenderer.PickProvinceAtGlobalPosition(mouseGlobal);
                    if (targetProvinceId < SimulationWorld.MaxProvinces)
                    {
                        if (targetProvinceId != div.CurrentProvinceId)
                        {
                            _pendingOrders[div.DivisionId] = targetProvinceId;
                            _world.EnqueueCommand(SimulationCommand.CreateMoveDivision(ActivePlayerCountryTag, div.DivisionId, targetProvinceId));
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
            var (rect, divIndices) = _counterClickTargets[i];
            Rect2 hitRect = rect.Grow(8.0f);
            if (hitRect.HasPoint(mouseGlobal) && divIndices.Count > 0)
            {
                int firstIdx = divIndices[0];
                HoveredDivisionId = _activeDivisions[firstIdx].div.DivisionId;
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
