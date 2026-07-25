using System.Text;
using UnityEngine;

namespace IronKingdoms.Combat
{
    public partial class TestLevelUnitController
    {
        private void AddCombatLogEntry(string entry)
        {
            combatLog.Add(entry);
            if (combatLog.Count > CombatLogMaxEntries)
            {
                combatLog.RemoveAt(0);
            }

            combatLogScrollPosition = new Vector2(0f, float.MaxValue);
        }

        private void TickFloatingDamage(float deltaTime)
        {
            for (var i = floatingDamageEntries.Count - 1; i >= 0; i--)
            {
                var entry = floatingDamageEntries[i];
                entry.Age += deltaTime;
                if (entry.Age >= FloatingDamageLifetime)
                {
                    floatingDamageEntries.RemoveAt(i);
                }
                else
                {
                    floatingDamageEntries[i] = entry;
                }
            }
        }

        private void SpawnFloatingText(Vector3 worldPosition, string text, Color color)
        {
            worldPosition.y += 0.5f;
            floatingDamageEntries.Add(new FloatingDamageEntry
            {
                WorldPosition = worldPosition,
                Text = text,
                Age = 0f,
                Color = color
            });
        }

        private void DrawFloatingDamageNumbers()
        {
            if (floatingDamageEntries.Count == 0)
            {
                return;
            }

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            if (floatingDamageStyle == null)
            {
                floatingDamageStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                floatingDamageShadowStyle = new GUIStyle(floatingDamageStyle);
            }

            for (var i = 0; i < floatingDamageEntries.Count; i++)
            {
                var entry = floatingDamageEntries[i];
                var t = entry.Age / FloatingDamageLifetime;
                var fadeAlpha = 1f - (t * t);
                var screenPos = activeCamera.WorldToScreenPoint(entry.WorldPosition);
                if (screenPos.z <= 0f)
                {
                    continue;
                }

                var riseOffset = entry.Age * FloatingDamageRiseSpeed;
                var guiX = screenPos.x - 40f;
                var guiY = Screen.height - screenPos.y - riseOffset - 20f;
                var labelRect = new Rect(guiX, guiY, 80f, 30f);

                var textColor = entry.Color;
                textColor.a = fadeAlpha;
                floatingDamageStyle.normal.textColor = textColor;
                floatingDamageShadowStyle.normal.textColor = new Color(0f, 0f, 0f, fadeAlpha * 0.65f);

                GUI.Label(new Rect(guiX + 1f, guiY + 1f, 80f, 30f), entry.Text, floatingDamageShadowStyle);
                GUI.Label(labelRect, entry.Text, floatingDamageStyle);
            }
        }

        private void OnGUI()
        {
            cameraManager?.DrawGui();
            DrawFogDebugPanel();
            DrawFloatingDamageNumbers();
            DrawTargetCoverPopup();
            DrawCombatLog();

            GUILayout.BeginArea(new Rect(RosterAreaX, RosterAreaY, RosterAreaWidth, RosterAreaHeight), "Player-Controlled Units", GUI.skin.window);
            GUILayout.Label($"Active Turn: {activeTurnSide}");
            if (activeTurnSide == TurnSide.Player && GUILayout.Button("End Turn"))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    EndPlayerTurn();
                }
            }

            GUILayout.Space(6f);
            if (playerRuntimeUnits.Count == 0)
            {
                GUILayout.Label("No units assigned.");
            }
            else
            {
                for (var i = 0; i < playerRuntimeUnits.Count; i++)
                {
                    var unit = playerRuntimeUnits[i];
                    var label = $"{i + 1}. {unit.Definition.DisplayName} - HP {unit.Health}";
                    if (!unit.IsAlive)
                    {
                        label += " (defeated)";
                    }

                    if (GUILayout.Button(label))
                    {
                        if (!WasUiCancelTriggeredThisFrame())
                        {
                            SelectUnit(unit);
                        }
                    }
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Enemies");
            for (var i = 0; i < enemyRuntimeUnits.Count; i++)
            {
                var enemy = enemyRuntimeUnits[i];
                string enemyLabel;
                if (!enemy.IsAlive)
                {
                    enemyLabel = $"{enemy.Definition.DisplayName} - defeated";
                }
                else if (!enemy.IsVisibleToPlayer)
                {
                    enemyLabel = "Hidden by fog of war";
                }
                else
                {
                    enemyLabel = $"{enemy.Definition.DisplayName} - HP {enemy.Health}/{enemy.Definition.Stats.health}";
                }

                GUILayout.Label(enemyLabel);
            }
            GUILayout.EndArea();

            if (selectedUnit == null)
            {
                DrawTeamVisionHint();
                DrawHoveredEnemyHealth();
                return;
            }

            GUILayout.BeginArea(GetSelectedUnitPanelRect(), "Selected Unit", GUI.skin.window);
            var selectedUnitScrollHeight = SelectedUnitPanelHeight - SelectedUnitPanelChromeHeight;
            selectedUnitPanelScrollPosition = GUILayout.BeginScrollView(
                selectedUnitPanelScrollPosition,
                false,
                true,
                GUILayout.Width(SelectedUnitPanelWidth - 8f),
                GUILayout.Height(selectedUnitScrollHeight));
            GUILayout.Label(selectedUnit.Definition.DisplayName);
            GUILayout.Label($"Role: {selectedUnit.Definition.Role}");
            GUILayout.Label($"HP: {selectedUnit.Health}/{selectedUnit.Definition.Stats.health}");
            GUILayout.Label(BuildHealthBoxes(selectedUnit.Health, selectedUnit.Definition.Stats.health));
            GUILayout.Label($"Speed: {selectedUnit.Definition.Stats.speed:0.0}  |  Move left: {selectedUnit.RemainingMovementThisTurn:0.0}\"");
            Unit.DrawTerrainStateDebug(selectedUnit);
            Unit.DrawDefenseModifierDebug(selectedUnit);
            Unit.DrawAbilityDebug(selectedUnit);
            Unit.DrawAdvantageDebug(selectedUnit);
            GUILayout.Label($"Model Size: {selectedUnit.Definition.Stats.modelSize.DisplayName()}");
            var selectedWeapon = GetSelectedAttackWeapon(selectedUnit);
            GUILayout.Label($"Weapon: {selectedWeapon.DisplayName}");
            GUILayout.Label($"Type: {selectedWeapon.AttackType}  |  Range: {selectedWeapon.Range:0.0}\"");
            GUILayout.Label($"Weapon Power: {selectedWeapon.Power}");
            if (selectedUnit.IsAimingThisTurn)
            {
                GUILayout.Label($"Aiming: +{AimToHitBonus} to hit (next attack)");
            }
            GUILayout.Label($"MAT Mod: {selectedWeapon.MatModifier:+#;-#;0}  |  RAT Mod: {selectedWeapon.RatModifier:+#;-#;0}");
            var effectiveMat = selectedUnit.Definition.Stats.meleeAttack + selectedWeapon.MatModifier;
            var effectiveRat = selectedUnit.Definition.Stats.rangedAttack + selectedWeapon.RatModifier;
            GUILayout.Label($"Effective MAT: {effectiveMat}  |  Effective RAT: {effectiveRat}");
            GUILayout.Space(6f);
            if (GUILayout.Button("Team Vision (show all friendly fog)"))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    SelectUnit(null);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            DrawActionBar();
            DrawHoveredEnemyHealth();
        }

        private static string BuildHealthBoxes(int health, int maxHealth)
        {
            var clampedCurrent = Mathf.Clamp(health, 0, maxHealth);
            var sb = new StringBuilder(maxHealth + (maxHealth / 10) + 1);
            for (var i = 0; i < maxHealth; i++)
            {
                if (i > 0 && i % 10 == 0)
                {
                    sb.Append(' ');
                }

                sb.Append(i < clampedCurrent ? '■' : '□');
            }

            return sb.ToString();
        }

        private void DrawActionBar()
        {
            if (selectedUnit == null || activeTurnSide != TurnSide.Player)
            {
                return;
            }

            GUILayout.BeginArea(GetActionBarRect(), string.Empty, GUI.skin.window);
            GUILayout.BeginHorizontal();

            var canMove = selectedUnit.RemainingMovementThisTurn > MovementBudgetEpsilon
                && !selectedUnit.MoveTarget.HasValue
                && (!selectedUnit.HasActedThisTurn || selectedUnit.HasRunActionThisTurn)
                && !selectedUnit.HasChargedThisTurn;
            GUI.enabled = canMove;
            var moveLabel = currentPlayerMode == UnitActionMode.Move ? "[ Move ]" : "Move";
            if (GUILayout.Button(moveLabel, GUILayout.Height(30f)))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Move ? UnitActionMode.None : UnitActionMode.Move);
                }
            }

            var canAttack = !selectedUnit.HasActedThisTurn && !selectedUnit.HasRunActionThisTurn;
            GUI.enabled = canAttack;
            var attackLabel = currentPlayerMode == UnitActionMode.Attack ? "[ Attack ]" : "Attack";
            if (GUILayout.Button(attackLabel, GUILayout.Height(30f)))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Attack ? UnitActionMode.None : UnitActionMode.Attack);
                }
            }

            GUI.enabled = true;
            var hideLabel = currentPlayerMode == UnitActionMode.Hide || selectedUnit.IsHiding
                ? "[ Hide ]"
                : "Hide";
            if (GUILayout.Button(hideLabel, GUILayout.Height(30f)))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    SetCurrentMode(currentPlayerMode == UnitActionMode.Hide ? UnitActionMode.None : UnitActionMode.Hide);
                }
            }

            GUILayout.EndHorizontal();

            if (currentPlayerMode == UnitActionMode.Attack)
            {
                DrawAttackActionControls();
            }
            else if (currentPlayerMode == UnitActionMode.Move)
            {
                DrawMoveActionControls(canMove);
            }
            else if (currentPlayerMode == UnitActionMode.Hide || selectedUnit.IsHiding)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Enemy line of sight (red grid). Hold Left Shift to preview without Hide.");
            }

            GUILayout.EndArea();
        }

        private void DrawAttackActionControls()
        {
            GUILayout.Space(6f);
            if (selectedUnit.Weapons == null || selectedUnit.Weapons.Length == 0)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            for (var i = 0; i < selectedUnit.Weapons.Length; i++)
            {
                var weapon = selectedUnit.Weapons[i];
                var label = $"{weapon.DisplayName} ({weapon.Range:0.0}\")";
                if (i == selectedAttackWeaponIndex)
                {
                    label = $"[ {label} ]";
                }

                if (GUILayout.Button(label))
                {
                    if (!WasUiCancelTriggeredThisFrame())
                    {
                        selectedAttackWeaponIndex = i;
                        RefreshAttackRangeRing();
                    }
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawMoveActionControls(bool canMove)
        {
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            var runLocked = selectedUnit.HasRunActionThisTurn;
            var chargeLocked = selectedUnit.HasChargedThisTurn
                || runLocked
                || selectedUnit.HasAdvancedThisTurn;
            if (runLocked)
            {
                selectedMovementOption = MovementStepOption.Run;
            }
            else if (selectedUnit.HasChargedThisTurn)
            {
                selectedMovementOption = MovementStepOption.Charge;
            }

            var advanceBudget = selectedUnit.RemainingMovementThisTurn;
            var advanceCore = $"Advance ({advanceBudget:0.0}\")";
            var advanceLabel = selectedMovementOption == MovementStepOption.Advance ? $"[ {advanceCore} ]" : advanceCore;
            GUI.enabled = !runLocked && !selectedUnit.HasChargedThisTurn;
            if (GUILayout.Button(advanceLabel))
            {
                selectedMovementOption = MovementStepOption.Advance;
            }

            var runBudget = selectedUnit.HasRunActionThisTurn
                ? selectedUnit.RemainingMovementThisTurn
                : selectedUnit.RemainingMovementThisTurn * RunMovementMultiplier;
            var runCore = $"Run ({runBudget:0.0}\")";
            var runLabel = selectedMovementOption == MovementStepOption.Run ? $"[ {runCore} ]" : runCore;
            GUI.enabled = canMove && !selectedUnit.HasAdvancedThisTurn && !selectedUnit.HasChargedThisTurn;
            if (GUILayout.Button(runLabel))
            {
                selectedMovementOption = MovementStepOption.Run;
            }

            var chargeBudget = selectedUnit.RemainingMovementThisTurn
                + (GetSelectedAttackWeapon(selectedUnit).attackType == WeaponAttackType.Melee ? ChargeMovementBonus : 0f);
            var chargeCore = $"Charge ({chargeBudget:0.0}\")";
            var chargeLabel = selectedMovementOption == MovementStepOption.Charge ? $"[ {chargeCore} ]" : chargeCore;
            GUI.enabled = canMove && !chargeLocked;
            if (GUILayout.Button(chargeLabel))
            {
                selectedMovementOption = MovementStepOption.Charge;
            }

            GUI.enabled = canMove && !selectedUnit.HasRunActionThisTurn;
            if (GUILayout.Button($"Aim (+{AimToHitBonus} to hit)"))
            {
                if (!WasUiCancelTriggeredThisFrame())
                {
                    ApplyAim(selectedUnit);
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawStagedMoveSummary();
        }

        private void DrawStagedMoveSummary()
        {
            if (!hasStagedMoveAmount)
            {
                return;
            }

            if (selectedUnit.IgnoresRoughTerrainMovementCost(selectedUnit, selectedMovementOption))
            {
                GUILayout.Label($"Total Move: {stagedMoveAmountInches:0.0}\" (Pathfinder: rough terrain treated as open)");
            }
            else if (stagedRoughTerrainInches > MovementBudgetEpsilon)
            {
                GUILayout.Label($"Total Move: {stagedMoveAmountInches:0.0}\" ({stagedRoughTerrainInches:0.0}\" rough terrain)");
            }
            else
            {
                GUILayout.Label($"Total Move: {stagedMoveAmountInches:0.0}\"");
            }
        }

        private static Rect GetActionBarRect()
        {
            var areaX = (Screen.width - ActionBarWidth) * 0.5f;
            var areaY = Screen.height - ActionBarHeight - ActionBarBottomMargin;
            return new Rect(areaX, areaY, ActionBarWidth, ActionBarHeight);
        }

        private static Rect GetSelectedUnitPanelRect()
        {
            var areaY = Screen.height - SelectedUnitPanelHeight - SelectedUnitPanelOffsetY;
            return new Rect(SelectedUnitPanelOffsetX, areaY, SelectedUnitPanelWidth, SelectedUnitPanelHeight);
        }

        private float GetHoverPanelHeight()
        {
            var showHitChance = currentPlayerMode == UnitActionMode.Attack
                && selectedUnit != null && selectedUnit.IsAlive
                && activeTurnSide == TurnSide.Player;
            return showHitChance ? HoverPanelHeight + HoverPanelAttackExtraHeight : HoverPanelHeight;
        }

        private void DrawTargetCoverPopup()
        {
            if (currentPlayerMode != UnitActionMode.Attack || hoveredEnemyUnit == null || !hoveredEnemyUnit.IsAlive)
            {
                return;
            }

            var activeCamera = cameraManager != null ? cameraManager.ActiveCamera : Camera.main;
            if (activeCamera == null)
            {
                return;
            }

            var modifiers = CombatDefenseEvaluator.CollectActiveDefenseModifiers(hoveredEnemyUnit.Definition, hoveredEnemyUnit.Pawn, selectedUnit?.Pawn);
            if (modifiers.Count == 0)
            {
                return;
            }

            var modelSize = hoveredEnemyUnit.Definition.Stats.modelSize;
            var topHeight = modelSize.VolumeHeightWorldUnits();
            var worldPos = hoveredEnemyUnit.Pawn.transform.position + Vector3.up * (topHeight + 0.15f);
            var screenPos = activeCamera.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0f)
            {
                return;
            }

            EnsureCoverPopupStyles();

            const float popupWidth = 180f;
            const float lineHeight = 20f;
            var popupHeight = modifiers.Count * lineHeight;
            var guiX = screenPos.x - popupWidth * 0.5f;
            var guiY = Screen.height - screenPos.y - popupHeight;

            for (var i = 0; i < modifiers.Count; i++)
            {
                DrawCoverModifierLine(modifiers[i], guiX, guiY + i * lineHeight, popupWidth, lineHeight);
            }
        }

        private void EnsureCoverPopupStyles()
        {
            if (coverPopupStyle != null)
            {
                return;
            }

            coverPopupStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            coverPopupShadowStyle = new GUIStyle(coverPopupStyle)
            {
                normal = { textColor = new Color(0f, 0f, 0f, 0.65f) }
            };
        }

        private void DrawCoverModifierLine(CombatDefenseModifierInstance modifier, float x, float y, float width, float height)
        {
            var category = modifier.Definition.Category;
            var attackerIgnoresConcealment = selectedUnit?.Definition?.Stats != null
                && selectedUnit.IgnoresConcealmentAndStealth();
            var ignored = category == CombatDefenseModifierCategory.Concealment && attackerIgnoresConcealment;
            var color = ignored
                ? new Color(0.6f, 0.6f, 0.6f)
                : category == CombatDefenseModifierCategory.Cover
                    ? new Color(0.3f, 0.7f, 1f)
                    : new Color(0.5f, 1f, 0.4f);
            coverPopupStyle.normal.textColor = color;
            var categoryLabel = category == CombatDefenseModifierCategory.Cover ? "Cover" : "Concealment";
            var ignoredSuffix = ignored ? " (ignored)" : string.Empty;
            var label = $"[{categoryLabel}] {modifier.SourceLabel} +{modifier.Definition.DefenseBonus}{ignoredSuffix}";
            var rect = new Rect(x, y, width, height);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, coverPopupShadowStyle);
            GUI.Label(rect, label, coverPopupStyle);
        }

        private void DrawHoveredEnemyHealth()
        {
            if (hoveredEnemyUnit == null || !hoveredEnemyUnit.IsAlive)
            {
                return;
            }

            var mousePosition = Input.mousePosition;
            var panelHeight = GetHoverPanelHeight();
            var x = Mathf.Clamp(mousePosition.x + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.width - HoverPanelWidth - HoverPanelScreenPadding);
            var y = Mathf.Clamp(Screen.height - mousePosition.y + HoverPanelMouseOffset, HoverPanelScreenPadding, Screen.height - panelHeight - HoverPanelScreenPadding);

            GUILayout.BeginArea(new Rect(x, y, HoverPanelWidth, panelHeight), "Target", GUI.skin.window);
            GUILayout.Label(hoveredEnemyUnit.Definition.DisplayName);
            GUILayout.Label($"HP: {hoveredEnemyUnit.Health}/{hoveredEnemyUnit.Definition.Stats.health}");
            GUILayout.Label(BuildHealthBoxes(hoveredEnemyUnit.Health, hoveredEnemyUnit.Definition.Stats.health));
            DrawHoveredEnemyDefense();
            DrawHoveredEnemyAttackContext();
            GUILayout.EndArea();
        }

        private void DrawHoveredEnemyDefense()
        {
            var baseDefense = hoveredEnemyUnit.Definition.Stats.defense;
            var effectiveDefense = selectedUnit != null && currentPlayerMode == UnitActionMode.Attack
                ? hoveredEnemyUnit.GetEffectiveDefense(selectedUnit, GetSelectedAttackWeapon(selectedUnit))
                : baseDefense;
            var defenseLabel = effectiveDefense != baseDefense
                ? $"DEF: {baseDefense} -> {effectiveDefense} (mod)  |  ARM: {hoveredEnemyUnit.Definition.Stats.armor}"
                : $"DEF: {baseDefense}  |  ARM: {hoveredEnemyUnit.Definition.Stats.armor}";
            GUILayout.Label(defenseLabel);
        }

        private void DrawHoveredEnemyAttackContext()
        {
            if (currentPlayerMode != UnitActionMode.Attack || selectedUnit == null || !selectedUnit.IsAlive || activeTurnSide != TurnSide.Player)
            {
                return;
            }

            var weapon = GetSelectedAttackWeapon(selectedUnit);
            if (!IsInLiveFogVision(hoveredEnemyUnit.GetLineOfSightVolume().SightPoint))
            {
                if (IsSpottedByAnyPlayerUnit(hoveredEnemyUnit))
                {
                    GUILayout.Label("Spotted by team (outside this unit's vision)");
                }
                else
                {
                    GUILayout.Label("Not in vision (fog of war)");
                }
            }
            else if (!selectedUnit.IsWithinVisibilityRangeOf(hoveredEnemyUnit))
            {
                GUILayout.Label("Outside visibility range");
            }
            else if (!HasLineOfSight(selectedUnit, hoveredEnemyUnit))
            {
                GUILayout.Label("No line of sight");
            }
            else if (selectedUnit.IsTargetInRange(hoveredEnemyUnit, weapon))
            {
                var hitChance = Unit.CalculateHitChancePercent(selectedUnit, hoveredEnemyUnit, weapon);
                GUILayout.Label($"Hit Chance: {hitChance:0}%");
            }
            else
            {
                GUILayout.Label("Out of range");
            }
        }

        private void DrawTeamVisionHint()
        {
            if (activeTurnSide != TurnSide.Player)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(SelectedUnitPanelOffsetX, Screen.height - 72f - SelectedUnitPanelOffsetY, SelectedUnitPanelWidth, 72f), "Vision", GUI.skin.window);
            GUILayout.Label("Team vision — all friendly units reveal fog.");
            GUILayout.Label("Select a unit to focus fog on that model only.");
            GUILayout.EndArea();
        }

        private void DrawFogDebugPanel()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            const float panelWidth = 360f;
            const float panelHeight = 352f;
            var areaY = RosterAreaY + RosterAreaHeight + 8f;
            GUILayout.BeginArea(
                new Rect(RosterAreaX, areaY, panelWidth, panelHeight),
                "Fog Drawing",
                GUI.skin.window);

            var useForestPass = CombatForestFogPassSettings.UseForestPass;
            var toggled = GUILayout.Toggle(useForestPass, "Use combat forest pass");
            if (toggled != useForestPass)
            {
                CombatForestFogPassSettings.UseForestPass = toggled;
                debugUseForestFogPass = toggled;
                MarkFogRevealerSettingsDirty();
                RefreshAllFogRevealersAfterForestPassToggle();
            }

            var adaptiveFidelity = CombatForestFogPassSettings.UseAdaptiveFidelityWhileMoving;
            var adaptiveToggled = GUILayout.Toggle(adaptiveFidelity, "Lower fog cost while moving");
            if (adaptiveToggled != adaptiveFidelity)
            {
                CombatForestFogPassSettings.UseAdaptiveFidelityWhileMoving = adaptiveToggled;
                fogAdaptiveFidelityWhileMoving = adaptiveToggled;
            }

            var showProof = debugShowWallBaselineProof;
            var proofToggled = GUILayout.Toggle(showProof, "Show wall baseline proof (selected unit)");
            if (proofToggled != showProof)
            {
                debugShowWallBaselineProof = proofToggled;
                SyncWallBaselineProofOnRevealers();
                MarkFogRevealerSettingsDirty();
            }

            var showShaderUpload = debugShowShaderUploadPolygons;
            var shaderUploadToggled = GUILayout.Toggle(
                showShaderUpload,
                "Show shader upload polygons (selected unit)");
            if (shaderUploadToggled != showShaderUpload)
            {
                debugShowShaderUploadPolygons = shaderUploadToggled;
                SyncWallBaselineProofOnRevealers();
                MarkFogRevealerSettingsDirty();
            }

            GUILayout.Label(CombatForestFogPassSettings.UseForestPass
                ? "Mode: baseline polygon + added forest verts"
                : "Mode: baseline stock FOW");

            if (shaderUploadToggled)
            {
                GUILayout.Label("Blue loop = raw baseline upload verts (not the fog edge).");
                GUILayout.Label("Magenta = wall chord segments the shader draws.");
                GUILayout.Label("Yellow = per-direction baseline boundary (shader pass 1).");
                GUILayout.Label("Green = forest/cloud clip samples only (not open rays).");
            }

            var showFogTexture = debugShowFogTextureBoundary;
            var fogTextureToggled = GUILayout.Toggle(showFogTexture, "Show fog texture boundary (selected unit)");
            if (fogTextureToggled != showFogTexture)
            {
                debugShowFogTextureBoundary = fogTextureToggled;
                if (!fogTextureToggled)
                {
                    fogTextureBoundaryDrawer.ClearGameViewLines();
                }
            }

            var revealer = GetFogRevealer(selectedUnit);

            if (fogTextureToggled)
            {
                GUILayout.Label("Yellow loop = effective fog boundary (baseline + terrain upload).");
                GUILayout.Label("Cyan outline = forest zone footprint (reference only).");
                GUILayout.Label("Yellow is the target fog boundary (baseline + forest/cloud clip).");
                GUILayout.Label("Rendered fog should match the yellow line on open ground.");
                var movingProfile = CombatForestFogPassSettings.UseAdaptiveFidelityWhileMoving;
                var revealerMoving = revealer != null && revealer.IsPawnMoving;
                var wallStep = CombatForestFogPassSettings.WallRaycastResolutionDegrees;
                var lutBins = revealerMoving && movingProfile
                    ? CombatForestFogPassSettings.MovingLutSamples
                    : CombatForestFogPassSettings.MaxShaderLutSamples;
                var updateInterval = CombatForestFogPassSettings.MovingLineOfSightUpdateInterval;
                GUILayout.Label(
                    movingProfile
                        ? $"Wall step: {wallStep:0.##}° (stock FOW) | LUT: {lutBins} bins | update every {updateInterval} frame(s){(revealerMoving ? " (moving)" : " (stationary)")}."
                        : $"Wall raycast step: {wallStep:0.##}° (terrain LUT: {lutBins} bins).");
                GUILayout.Label("Cyan only aligns when the clip lands on the forest edge (thin patch).");
            }

            if (proofToggled)
            {
                GUILayout.Label("Magenta loop = pass-1 FindEdges (forest-off upload).");
                GUILayout.Label("Yellow ticks = uploaded wall hits (must match magenta).");
            }

            if (proofToggled && selectedUnit == null)
            {
                GUILayout.Label("Select a unit to verify wall baseline.");
            }
            else if (proofToggled && revealer == null)
            {
                GUILayout.Label("Selected unit has no fog revealer.");
            }
            else if (proofToggled && revealer != null && !toggled)
            {
                var report = revealer.WallBaselineReport;
                if (report.HasData)
                {
                    GUILayout.Label(report.SummaryLine);
                    GUILayout.Label(report.DetailLine);
                }
                else
                {
                    GUILayout.Label("Forest off — magenta loop shows stock baseline upload.");
                }
            }
            else if (proofToggled && revealer != null && toggled)
            {
                var report = revealer.WallBaselineReport;
                var summaryStyle = report.HasData && report.AllWallBlockedRaysPreserved
                    ? GUI.skin.label
                    : GUI.skin.box;
                GUILayout.Label(report.SummaryLine, summaryStyle);
                if (report.HasData)
                {
                    GUILayout.Label(report.DetailLine);
                }
            }

            GUILayout.EndArea();
        }

        private void DrawCombatLog()
        {
            if (combatLog.Count == 0)
            {
                return;
            }

            var x = Screen.width - CombatLogPanelWidth - CombatLogPanelRightMargin;
            GUILayout.BeginArea(new Rect(x, CombatLogPanelTopMargin, CombatLogPanelWidth, CombatLogPanelHeight), "Combat Log", GUI.skin.window);
            combatLogScrollPosition = GUILayout.BeginScrollView(combatLogScrollPosition);
            foreach (var entry in combatLog)
            {
                GUILayout.Label(entry);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
