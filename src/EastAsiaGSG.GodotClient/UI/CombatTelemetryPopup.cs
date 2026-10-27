using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Floating HUD panel presenting detailed combat telemetry for an active HOI4 battle bubble,
/// including 0..100 battle progress, combat width saturation, damage rates, and terrain modifiers.
/// </summary>
public partial class CombatTelemetryPopup : PanelContainer
{
    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();

    private Label? _titleLabel;
    private Label? _terrainLabel;
    private ProgressBar? _progressBar;
    private Label? _progressPercentLabel;
    private Label? _attackerHeaderLabel;
    private Label? _defenderHeaderLabel;
    private Label? _combatWidthLabel;
    private Label? _damageStatsLabel;
    private Label? _pgmStatusLabel;
    private Button? _closeButton;

    private int _currentCombatId = -1;
    private SimulationWorld? _world;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(340, 260);
        Visible = false;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.11f, 0.14f, 0.95f),
            BorderColor = new Color(0.24f, 0.28f, 0.35f, 1.0f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8
        };
        AddThemeStyleboxOverride("panel", style);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        // Header with Title & Close Button
        var header = new HBoxContainer();
        _titleLabel = new Label
        {
            Text = "Combat Telemetry",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 15);
        _titleLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.88f, 0.40f));
        header.AddChild(_titleLabel);

        _closeButton = new Button
        {
            Text = " ✕ ",
            Flat = true
        };
        _closeButton.Pressed += () => Visible = false;
        header.AddChild(_closeButton);
        vbox.AddChild(header);

        // Terrain & Location info
        _terrainLabel = new Label
        {
            Text = "Location: Kaesong City | Terrain: Urban",
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _terrainLabel.AddThemeFontSizeOverride("font_size", 12);
        _terrainLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.82f));
        vbox.AddChild(_terrainLabel);

        // Battle Progress Bar (0..100)
        var progressContainer = new VBoxContainer();
        var progressHeader = new HBoxContainer();

        _attackerHeaderLabel = new Label
        {
            Text = "ATTACKER: ROK",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _attackerHeaderLabel.AddThemeFontSizeOverride("font_size", 12);
        _attackerHeaderLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.75f, 1.0f));
        progressHeader.AddChild(_attackerHeaderLabel);

        _defenderHeaderLabel = new Label
        {
            Text = "PRK :DEFENDER",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _defenderHeaderLabel.AddThemeFontSizeOverride("font_size", 12);
        _defenderHeaderLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.40f, 0.40f));
        progressHeader.AddChild(_defenderHeaderLabel);
        progressContainer.AddChild(progressHeader);

        _progressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 50,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 16)
        };
        progressContainer.AddChild(_progressBar);

        _progressPercentLabel = new Label
        {
            Text = "Battle Progress: 50% (Contested)",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _progressPercentLabel.AddThemeFontSizeOverride("font_size", 11);
        progressContainer.AddChild(_progressPercentLabel);
        vbox.AddChild(progressContainer);

        // Combat Width details
        _combatWidthLabel = new Label
        {
            Text = "Combat Width: 80 / 80 (Attacker: 4 div, Defender: 1 div)"
        };
        _combatWidthLabel.AddThemeFontSizeOverride("font_size", 12);
        _combatWidthLabel.AddThemeColorOverride("font_color", new Color(0.90f, 0.90f, 0.90f));
        vbox.AddChild(_combatWidthLabel);

        // Damage Telemetry
        _damageStatsLabel = new Label
        {
            Text = "Org Loss: Attacker -0.3 / tick | Defender -4.9 / tick\nStrength Loss: Attacker -0.05% | Defender -0.78%",
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _damageStatsLabel.AddThemeFontSizeOverride("font_size", 11);
        _damageStatsLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.85f, 0.90f));
        vbox.AddChild(_damageStatsLabel);

        // PGM & Air Status
        _pgmStatusLabel = new Label
        {
            Text = "PGM Strike: Active (+35% Attack) | Air Superiority: Contested"
        };
        _pgmStatusLabel.AddThemeFontSizeOverride("font_size", 11);
        _pgmStatusLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.80f, 0.20f));
        vbox.AddChild(_pgmStatusLabel);
    }

    public void DisplayCombat(SimulationWorld world, int combatId)
    {
        _world = world;
        _currentCombatId = combatId;
        UpdateTelemetry();
        Visible = true;
    }

    public void RefreshIfActive()
    {
        if (Visible && _currentCombatId != -1)
        {
            UpdateTelemetry();
        }
    }

    private void UpdateTelemetry()
    {
        if (_world == null || _currentCombatId == -1)
        {
            return;
        }

        ActiveCombatComponent foundCombat = default;
        bool found = false;

        Query query = _world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                if (combats[i].CombatId == _currentCombatId)
                {
                    foundCombat = combats[i];
                    found = true;
                    break;
                }
            }
            if (found) break;
        }

        if (!found || foundCombat.Outcome != CombatOutcome.Ongoing)
        {
            if (found)
            {
                string outcomeText = foundCombat.Outcome == CombatOutcome.AttackerVictory
                    ? "Attacker Victory!"
                    : "Defender Victory (Repulsed)!";
                if (_titleLabel != null) _titleLabel.Text = $"Battle Concluded ({outcomeText})";
                if (_progressBar != null) _progressBar.Value = foundCombat.Outcome == CombatOutcome.AttackerVictory ? 100 : 0;
            }
            return;
        }

        ushort targetId = foundCombat.TargetProvinceId;
        ref readonly ProvinceData prov = ref _world.Provinces[targetId];

        string attTag = CountryTag.GetCode(foundCombat.AttackerTag);
        string defTag = CountryTag.GetCode(foundCombat.DefenderTag);

        if (_titleLabel != null)
        {
            _titleLabel.Text = $"Battle for Province #{targetId} (Tick {foundCombat.ElapsedSixHourTicks})";
        }

        if (_terrainLabel != null)
        {
            _terrainLabel.Text = $"Terrain: {prov.Terrain} | Infra: {prov.InfrastructureLevel} | Supply: {prov.SupplyRatio:P0}";
        }

        if (_attackerHeaderLabel != null)
        {
            _attackerHeaderLabel.Text = $"ATTACKER: {attTag}";
        }

        if (_defenderHeaderLabel != null)
        {
            _defenderHeaderLabel.Text = $"{defTag} :DEFENDER";
        }

        float progress = Mathf.Clamp(foundCombat.BattleProgress, 0f, 100f);
        if (_progressBar != null)
        {
            _progressBar.Value = progress;
        }

        if (_progressPercentLabel != null)
        {
            string status = progress >= 55f ? "Attacker Winning" : (progress >= 45f ? "Contested" : "Defender Holding");
            _progressPercentLabel.Text = $"Progress: {Mathf.RoundToInt(progress)}% ({status})";
        }

        if (_combatWidthLabel != null)
        {
            _combatWidthLabel.Text = $"Combat Width: {foundCombat.TotalCombatWidth} (Used: Att {foundCombat.UsedAttackerWidth} / Def {foundCombat.UsedDefenderWidth})\n" +
                                     $"Forces Engaged: {foundCombat.EngagedAttackerCount} Att vs {foundCombat.EngagedDefenderCount} Def ({foundCombat.AttackDirectionCount} axes)";
        }

        if (_damageStatsLabel != null)
        {
            _damageStatsLabel.Text = $"Recent Org Loss: Def -{foundCombat.LastTickAttackerOrgDamage:F1} | Att -{foundCombat.LastTickDefenderOrgDamage:F1}\n" +
                                     $"Recent Str Loss: Def -{foundCombat.LastTickAttackerStrengthDamage * 100f:F2}% | Att -{foundCombat.LastTickDefenderStrengthDamage * 100f:F2}%";
        }

        if (_pgmStatusLabel != null)
        {
            ref readonly CountryState attCountry = ref _world.Countries[foundCombat.AttackerTag];
            int pgmStockpile = attCountry.GetTotalArchetypeStockpile(EquipmentType.PGM);
            _pgmStatusLabel.Text = pgmStockpile > 0
                ? $"PGM Stockpile: {pgmStockpile} units (Authorized +35% strike)"
                : "PGM Stockpile: Depleted (Standard Fire)";
        }
    }
}
