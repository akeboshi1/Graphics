using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering.HighDefinition
{
    using CED = CoreEditorDrawer<SerializedHDLight>;

    static partial class HDLightUI
    {
        // The UI has a hierarchy for area and spot lights, e.g. the tube light is shown in the Shape dropdown when the
        // light type is set to Area. This hierarchy doesn't exist in the API, e.g. LightType.Tube and
        // LightType.Directional are siblings. To enable the UI hierarchy, we define a few enums that are only used in
        // UI and are never serialized.
        internal enum LightArchetype
        {
            Spot,
            Directional,
            Point,
            Area
        }
        internal static LightArchetype GetArchetype(LightType type)
        {
            if (type == LightType.Directional)
            {
                return LightArchetype.Directional;
            }
            else if (type == LightType.Point)
            {
                return LightArchetype.Point;
            }
            else if (type.IsSpot())
            {
                return LightArchetype.Spot;
            }
            else if (type.IsArea())
            {
                return LightArchetype.Area;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        internal enum AreaSubtype
        {
            Rectangle,
            Tube,
            Disc
        }
        internal enum SpotSubtype
        {
            Cone,
            Pyramid,
            Box
        }

        public static class ScalableSettings
        {
            public static IntScalableSetting ShadowResolution(LightType lightType, HDRenderPipelineAsset hdrp)
            {
                switch (lightType)
                {
                    case LightType.Directional:
                        return HDAdditionalLightData.ScalableSettings.ShadowResolutionDirectional(hdrp);
                    case LightType.Point:
                        return HDAdditionalLightData.ScalableSettings.ShadowResolutionPunctual(hdrp);
                    case LightType.Spot:
                    case LightType.Pyramid:
                    case LightType.Box:
                        return HDAdditionalLightData.ScalableSettings.ShadowResolutionPunctual(hdrp);
                    case LightType.Rectangle:
                    case LightType.Tube:
                    case LightType.Disc:
                        return HDAdditionalLightData.ScalableSettings.ShadowResolutionArea(hdrp);
                    default: throw new ArgumentOutOfRangeException(nameof(lightType));
                }
            }
        }

        enum ShadowmaskMode
        {
            Shadowmask,
            DistanceShadowmask
        }

        [HDRPHelpURL("Light-Component")]
        enum Expandable
        {
            General = 1 << 0,
            Shape = 1 << 1,
            Emission = 1 << 2,
            Volumetric = 1 << 3,
            Shadows = 1 << 4,
            ShadowMap = 1 << 5,
            ContactShadow = 1 << 6,
            BakedShadow = 1 << 7,
            ShadowQuality = 1 << 8,
            CelestialBody = 1 << 9,
        }

        enum AdditionalProperties
        {
            General = 1 << 0,
            Shape = 1 << 1,
            Emission = 1 << 2,
            Shadow = 1 << 3,
        }

        readonly static ExpandedState<Expandable, Light> k_ExpandedState = new ExpandedState<Expandable, Light>(0, "HDRP");
        readonly static AdditionalPropertiesState<AdditionalProperties, Light> k_AdditionalPropertiesState = new AdditionalPropertiesState<AdditionalProperties, Light>(0, "HDRP");

        readonly static HDLightUnitSliderUIDrawer k_LightUnitSliderUIDrawer = new HDLightUnitSliderUIDrawer();

        public static readonly CED.IDrawer Inspector;

        internal static void RegisterEditor(HDLightEditor editor)
        {
            k_AdditionalPropertiesState.RegisterEditor(editor);
        }

        internal static void UnregisterEditor(HDLightEditor editor)
        {
            k_AdditionalPropertiesState.UnregisterEditor(editor);
        }

        [SetAdditionalPropertiesVisibility]
        internal static void SetAdditionalPropertiesVisibility(bool value)
        {
            if (value)
                k_AdditionalPropertiesState.ShowAll();
            else
                k_AdditionalPropertiesState.HideAll();
        }

        static Func<LightingSettings> GetLightingSettingsOrDefaultsFallback;

        static HDLightUI()
        {
            Func<SerializedHDLight, bool> isArea = (serialized) => serialized.settings.lightType.GetEnumValue<LightType>().IsArea();

            Inspector = CED.Group(
                CED.AdditionalPropertiesFoldoutGroup(LightUI.Styles.generalHeader, Expandable.General, k_ExpandedState, AdditionalProperties.General, k_AdditionalPropertiesState,
                CED.Group((serialized, owner) => DrawGeneralContent(serialized, owner)), DrawGeneralAdditionalContent),
                CED.FoldoutGroup(LightUI.Styles.shapeHeader, Expandable.Shape, k_ExpandedState, DrawShapeContent),
                CED.Conditional((serialized, owner) => serialized.settings.lightType.GetEnumValue<LightType>() == LightType.Directional && !serialized.settings.isCompletelyBaked,
                    CED.FoldoutGroup(s_Styles.celestialBodyHeader, Expandable.CelestialBody, k_ExpandedState, DrawCelestialBodyContent)),
                CED.AdditionalPropertiesFoldoutGroup(LightUI.Styles.emissionHeader, Expandable.Emission, k_ExpandedState, AdditionalProperties.Emission, k_AdditionalPropertiesState,
                    CED.Group(
                        LightUI.DrawColor,
                        DrawLightIntensityGUILayout,
                        DrawEmissionContent),
                    DrawEmissionAdditionalContent),
                CED.Conditional((serialized, owner) => !serialized.settings.isCompletelyBaked,
                    CED.FoldoutGroup(s_Styles.volumetricHeader, Expandable.Volumetric, k_ExpandedState, DrawVolumetric)),
                CED.Conditional((serialized, owner) =>
                {
                    LightType type = serialized.settings.lightType.GetEnumValue<LightType>();
                    return !type.IsArea() || type.IsArea() && type != LightType.Tube;
                },
                    CED.TernaryConditional((serialized, owner) => !serialized.settings.isCompletelyBaked,
                        CED.AdditionalPropertiesFoldoutGroup(LightUI.Styles.shadowHeader, Expandable.Shadows, k_ExpandedState, AdditionalProperties.Shadow, k_AdditionalPropertiesState,
                            CED.Group(
                                CED.Group(
                                    CED.AdditionalPropertiesFoldoutGroup(s_Styles.shadowMapSubHeader, Expandable.ShadowMap, k_ExpandedState, AdditionalProperties.Shadow, k_AdditionalPropertiesState,
                                        DrawShadowMapContent, DrawShadowMapAdditionalContent, FoldoutOption.SubFoldout | FoldoutOption.Indent | FoldoutOption.NoSpaceAtEnd)),
                                CED.space,
                                CED.Conditional((serialized, owner) => !isArea(serialized) && k_AdditionalPropertiesState[AdditionalProperties.Shadow] && HasPunctualShadowQualitySettingsUI(HDShadowFilteringQuality.High, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.highShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawHighShadowSettingsContent)),
                                CED.Conditional((serialized, owner) => !isArea(serialized) && HasPunctualShadowQualitySettingsUI(HDShadowFilteringQuality.Medium, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.mediumShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawMediumShadowSettingsContent)),
                                CED.Conditional((serialized, owner) => !isArea(serialized) && HasPunctualShadowQualitySettingsUI(HDShadowFilteringQuality.Low, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.lowShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawLowShadowSettingsContent)),

                                CED.Conditional((serialized, owner) => !isArea(serialized) && k_AdditionalPropertiesState[AdditionalProperties.Shadow] && HasDirectionalShadowQualitySettingsUI(HDShadowFilteringQuality.High, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.highShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawHighShadowSettingsContent)),
                                CED.Conditional((serialized, owner) => !isArea(serialized) && HasDirectionalShadowQualitySettingsUI(HDShadowFilteringQuality.Medium, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.mediumShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawMediumShadowSettingsContent)),
                                CED.Conditional((serialized, owner) => !isArea(serialized) && HasDirectionalShadowQualitySettingsUI(HDShadowFilteringQuality.Low, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.lowShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawLowShadowSettingsContent)),

                                CED.Conditional((serialized, owner) => isArea(serialized) && k_AdditionalPropertiesState[AdditionalProperties.Shadow] && HasAreaShadowQualitySettingsUI(HDAreaShadowFilteringQuality.High, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.highShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawHighShadowSettingsContent)),
                                CED.Conditional((serialized, owner) => isArea(serialized) && HasAreaShadowQualitySettingsUI(HDAreaShadowFilteringQuality.Medium, serialized, owner),
                                    CED.FoldoutGroup(s_Styles.mediumShadowQualitySubHeader, Expandable.ShadowQuality, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent, DrawMediumShadowSettingsContent)),
                                CED.Conditional((serialized, owner) => !isArea(serialized),
                                    CED.FoldoutGroup(s_Styles.contactShadowsSubHeader, Expandable.ContactShadow, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent | FoldoutOption.NoSpaceAtEnd, DrawContactShadowsContent)
                                )
                                ),
                            CED.noop //will only add parameter in first sub header
                            ),
                        CED.FoldoutGroup(LightUI.Styles.shadowHeader, Expandable.Shadows, k_ExpandedState,
                            CED.FoldoutGroup(s_Styles.bakedShadowsSubHeader, Expandable.BakedShadow, k_ExpandedState, FoldoutOption.SubFoldout | FoldoutOption.Indent | FoldoutOption.NoSpaceAtEnd, DrawBakedShadowsContent))
                    )
                )
            );

            PresetInspector = CED.Group(
                CED.Group((serialized, owner) =>
                    EditorGUILayout.HelpBox(LightUI.Styles.unsupportedPresetPropertiesMessage, MessageType.Info)),
                CED.Group((serialized, owner) => EditorGUILayout.Space()),
                CED.FoldoutGroup(LightUI.Styles.generalHeader, Expandable.General, k_ExpandedStatePreset, CED.Group((serialized, owner) => DrawGeneralContent(serialized, owner, true))),
                CED.FoldoutGroup(LightUI.Styles.emissionHeader, Expandable.Emission, k_ExpandedStatePreset, CED.Group(
                    LightUI.DrawColor,
                    DrawEmissionContent)),
                CED.FoldoutGroup(LightUI.Styles.shadowHeader, Expandable.Shadows, k_ExpandedStatePreset, DrawEnableShadowMapInternal)
            );

            Type lightMappingType = typeof(Lightmapping);
            var getLightingSettingsOrDefaultsFallbackInfo = lightMappingType.GetMethod("GetLightingSettingsOrDefaultsFallback", BindingFlags.Static | BindingFlags.NonPublic);
            var getLightingSettingsOrDefaultsFallbackLambda = Expression.Lambda<Func<LightingSettings>>(Expression.Call(null, getLightingSettingsOrDefaultsFallbackInfo));
            GetLightingSettingsOrDefaultsFallback = getLightingSettingsOrDefaultsFallbackLambda.Compile();
        }

        // This scope is here mainly to keep pointLightHDType isolated
        public struct LightTypeEditionScope : IDisposable
        {
            EditorGUI.PropertyScope lightTypeScope;

            public LightTypeEditionScope(Rect rect, GUIContent label, SerializedHDLight serialized, bool isPreset)
            {
                // When editing a Light Preset, the HDAdditionalData, is not editable as is not shown on the inspector, therefore, all the properties
                // That come from the HDAdditionalData are not editable, if we use the PropertyScope for those, as they are not editable this will block
                // the edition of any property that came afterwards. So make sure that we do not use the PropertyScope if the editor is for a preset
                lightTypeScope = new EditorGUI.PropertyScope(rect, label, serialized.settings.lightType);
            }

            void IDisposable.Dispose()
            {
                lightTypeScope.Dispose();
            }
        }

        // !!! This is very weird, but for some reason the change of this field is not registered when changed.
        //     Because it is important to trigger the rebuild of the light entity (for the burst light loop) and it
        //     happens only when a change in editor happens !!!
        //     The issue is likely on the C# trunk side with the GUI Dropdown.
        static int s_OldLightBakeType = (int)LightmapBakeType.Realtime;

        static void DrawGeneralContent(SerializedHDLight serialized, Editor owner, bool isPreset = false)
        {
            Rect lineRect = EditorGUILayout.GetControlRect();

            // Break down the current light type into the archetype and the subtype. We do this because the UI has a
            // hierarchy of two dropdowns, e.g. Spot -> Pyramid, while the backing data type is flat, e.g. LightType.Pyramid.
            LightArchetype archetype = GetArchetype(serialized.settings.lightType.GetEnumValue<LightType>());
            EditorGUI.BeginChangeCheck();

            // Partial support for prefab. There is no way to fully support it at the moment.
            // Missing support on the Apply and Revert contextual menu on Label for Prefab overrides. They need to be done two times.
            // (This will continue unless we remove AdditionalDatas)
            using (new LightTypeEditionScope(lineRect, s_Styles.shape, serialized, isPreset))
            {
                EditorGUI.showMixedValue = serialized.HasMultipleLightTypes(owner);
                archetype = (LightArchetype)EditorGUI.EnumPopup(
                    lineRect,
                    s_Styles.shape,
                    archetype,
                    e => !isPreset || (LightArchetype)e != LightArchetype.Area,
                    false);
            }

            if (EditorGUI.EndChangeCheck())
            {
                switch (archetype)
                {
                    case LightArchetype.Spot:
                        serialized.settings.lightType.SetEnumValue(LightType.Spot);
                        break;
                    case LightArchetype.Directional:
                        serialized.settings.lightType.SetEnumValue(LightType.Directional);
                        break;
                    case LightArchetype.Point:
                        serialized.settings.lightType.SetEnumValue(LightType.Point);
                        break;
                    case LightArchetype.Area:
                        serialized.settings.lightType.SetEnumValue(LightType.Rectangle);
                        serialized.shapeWidth.floatValue = Mathf.Max(serialized.shapeWidth.floatValue, HDAdditionalLightData.k_MinLightSize);
                        serialized.shapeHeight.floatValue = Mathf.Max(serialized.shapeHeight.floatValue, HDAdditionalLightData.k_MinLightSize);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                UpdateLightIntensityUnit(serialized, owner);

                // For GI we need to detect any change on additional data and call SetLightDirty + For intensity we need to detect light shape change
                serialized.needUpdateAreaLightEmissiveMeshComponents = true;
                serialized.FetchAreaLightEmissiveMeshComponents();
                SetLightsDirty(owner); // Should be apply only to parameter that's affect GI, but make the code cleaner
            }
            EditorGUI.showMixedValue = false;

            // Draw the mode, for Tube and Disc lights, there is only one choice, so we can disable the enum.
            {
                LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();
                using (new EditorGUI.DisabledScope(lightType == LightType.Tube ||
                                                   lightType == LightType.Disc))
                    serialized.settings.DrawLightmapping();


                if (s_OldLightBakeType != serialized.settings.lightmapping.intValue)
                {
                    s_OldLightBakeType = serialized.settings.lightmapping.intValue;
                    GUI.changed = true;
                }

                switch (lightType)
                {
                    case LightType.Tube:
                        if (serialized.settings.isBakedOrMixed)
                            EditorGUILayout.HelpBox("Tube Area Lights are realtime only.", MessageType.Error);
                        break;
                    case LightType.Disc:
                        if (!serialized.settings.isCompletelyBaked)
                            EditorGUILayout.HelpBox("Disc Area Lights are baked only.", MessageType.Error);
                        // Disc lights are not supported in Enlighten
                        if (!Lightmapping.bakedGI && Lightmapping.realtimeGI)
                            EditorGUILayout.HelpBox("Disc Area Lights are not supported with realtime GI.",
                                MessageType.Error);
                        break;
                }
            }
        }

        static void DrawGeneralAdditionalContent(SerializedHDLight serialized, Editor owner)
        {
            if (HDUtils.hdrpSettings.supportLightLayers)
            {
                using (var change = new EditorGUI.ChangeCheckScope())
                {
                    EditorGUILayout.PropertyField(serialized.lightlayersMask, LightUI.Styles.lightLayer);
                    if (change.changed && serialized.linkShadowLayers.boolValue)
                    {
                        Undo.RecordObjects(owner.targets, "Undo Light Layers Changed");
                        SyncLightAndShadowLayers(serialized, owner);
                    }
                }
            }
        }

        static void DrawShapeContent(SerializedHDLight serialized, Editor owner)
        {
            EditorGUI.BeginChangeCheck(); // For GI we need to detect any change on additional data and call SetLightDirty + For intensity we need to detect light shape change

            LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();

            if (serialized.HasMultipleLightTypes(owner))
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.LabelField("Multiple different Types in selection");
            }
            else if (lightType == LightType.Directional)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.angularDiameter, s_Styles.angularDiameter);
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.angularDiameter.floatValue = Mathf.Clamp(serialized.angularDiameter.floatValue, 0, 90);
                    serialized.settings.bakedShadowAngleProp.floatValue = serialized.angularDiameter.floatValue;
                }
            }
            else if (lightType == LightType.Point)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.shapeRadius, s_Styles.lightRadius);
                if (EditorGUI.EndChangeCheck())
                {
                    //Also affect baked shadows
                    serialized.settings.bakedShadowRadiusProp.floatValue = serialized.shapeRadius.floatValue;
                }
            }
            else if (lightType.IsSpot())
            {
                // Spot light shape
                {
                    SpotSubtype subtype;
                    switch (lightType)
                    {
                        case LightType.Spot:
                            subtype = SpotSubtype.Cone;
                            break;
                        case LightType.Pyramid:
                            subtype = SpotSubtype.Pyramid;
                            break;
                        case LightType.Box:
                            subtype = SpotSubtype.Box;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    EditorGUI.BeginChangeCheck();
                    subtype = (SpotSubtype)EditorGUILayout.EnumPopup(s_Styles.spotLightShape, subtype);
                    if (EditorGUI.EndChangeCheck())
                    {
                        switch (subtype)
                        {
                            case SpotSubtype.Cone:
                                serialized.settings.lightType.SetEnumValue(LightType.Spot);
                                break;
                            case SpotSubtype.Pyramid:
                                serialized.settings.lightType.SetEnumValue(LightType.Pyramid);
                                break;
                            case SpotSubtype.Box:
                                serialized.settings.lightType.SetEnumValue(LightType.Box);
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                        UpdateLightIntensityUnit(serialized, owner);
                    }
                }
                using (new EditorGUI.IndentLevelScope())
                {
                    // If realtime GI is enabled and the shape is unsupported or not implemented, show a warning.
                    if (serialized.settings.isRealtime && SupportedRenderingFeatures.active.enlighten && GetLightingSettingsOrDefaultsFallback.Invoke().realtimeGI)
                    {
                        if (lightType == LightType.Box || lightType == LightType.Pyramid)
                            EditorGUILayout.HelpBox(s_Styles.unsupportedLightShapeWarning, MessageType.Warning);
                    }

                    if (serialized.HasMultipleLightTypes(owner))
                    {
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.LabelField("Multiple different spot Shapes in selection");
                    }
                    else if (lightType == LightType.Box)
                    {
                        // Box directional light.
                        EditorGUILayout.PropertyField(serialized.shapeWidth, s_Styles.shapeWidthBox);
                        EditorGUILayout.PropertyField(serialized.shapeHeight, s_Styles.shapeHeightBox);
                    }
                    else if (lightType == LightType.Spot)
                    {
                        // Cone spot projector
                        int indent = EditorGUI.indentLevel;

                        float textFieldWidth = EditorGUIUtility.pixelsPerPoint * 25f;
                        float spacing = EditorGUIUtility.pixelsPerPoint * 2f;

                        float max = serialized.settings.spotAngle.floatValue ;
                        float min = (serialized.spotInnerPercent.floatValue / 100f) * max;

                        Rect position = EditorGUILayout.GetControlRect();

                        EditorGUI.indentLevel--;
                        Rect rect = EditorGUI.PrefixLabel(position, s_Styles.innerOuterSpotAngle);
                        EditorGUI.indentLevel = 0;

                        Rect sliderRect = rect;
                        sliderRect.x += textFieldWidth + spacing;
                        sliderRect.width -= (textFieldWidth + spacing) * 2f;

                        Rect minRect = rect;
                        minRect.width = textFieldWidth;

                        Rect maxRect = position;
                        maxRect.x += maxRect.width - textFieldWidth;
                        maxRect.width = textFieldWidth;

                        EditorGUI.BeginChangeCheck();
                        min = EditorGUI.DelayedFloatField(minRect, min);
                        if (EditorGUI.EndChangeCheck())
                        {
                            min = Mathf.Clamp(min, HDAdditionalLightData.k_MinSpotAngle, max);
                            serialized.spotInnerPercent.floatValue = min / max * 100f;
                        }

                        EditorGUI.BeginChangeCheck();
                        EditorGUI.MinMaxSlider(sliderRect, ref min, ref max, HDAdditionalLightData.k_MinSpotAngle,HDAdditionalLightData.k_MaxSpotAngle );

                        if (EditorGUI.EndChangeCheck())
                        {
                            min = Mathf.Clamp(min, HDAdditionalLightData.k_MinSpotAngle, max);
                            serialized.spotInnerPercent.floatValue = min / max * 100f;
                            serialized.settings.spotAngle.floatValue = max;
                            serialized.settings.bakedShadowRadiusProp.floatValue = serialized.shapeRadius.floatValue;
                        }

                        EditorGUI.DelayedFloatField(maxRect, serialized.settings.spotAngle,GUIContent.none);

                        EditorGUI.indentLevel = indent - 1;
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(serialized.shapeRadius, s_Styles.lightRadius);
                        if (EditorGUI.EndChangeCheck())
                        {
                            //Also affect baked shadows
                            serialized.settings.bakedShadowRadiusProp.floatValue = serialized.shapeRadius.floatValue;
                        }

                        EditorGUI.indentLevel = indent;
                    }
                    else if (lightType == LightType.Pyramid)
                    {
                        // pyramid spot projector
                        EditorGUI.BeginChangeCheck();
                        serialized.settings.DrawSpotAngle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            serialized.customSpotLightShadowCone.floatValue = Math.Min(serialized.customSpotLightShadowCone.floatValue, serialized.settings.spotAngle.floatValue);
                        }
                        EditorGUILayout.Slider(serialized.aspectRatio, HDAdditionalLightData.k_MinAspectRatio, HDAdditionalLightData.k_MaxAspectRatio, s_Styles.aspectRatioPyramid);
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(serialized.shapeRadius, s_Styles.lightRadius);
                        if (EditorGUI.EndChangeCheck())
                        {
                            //Also affect baked shadows
                            serialized.settings.bakedShadowRadiusProp.floatValue = serialized.shapeRadius.floatValue;
                        }
                    }
                    else
                    {
                        Debug.Assert(false, "Not implemented spot light shape");
                    }
                }
            }
            else if (lightType.IsArea())
            {
                // Area light shape
                {
                    AreaSubtype subtype;
                    switch (lightType)
                    {
                        case LightType.Rectangle:
                            subtype = AreaSubtype.Rectangle;
                            break;
                        case LightType.Disc:
                            subtype = AreaSubtype.Disc;
                            break;
                        case LightType.Tube:
                            subtype = AreaSubtype.Tube;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    EditorGUI.BeginChangeCheck();
                    subtype = (AreaSubtype)EditorGUILayout.EnumPopup(s_Styles.areaLightShape, subtype);
                    if (EditorGUI.EndChangeCheck())
                    {
                        switch (subtype)
                        {
                            case AreaSubtype.Rectangle:
                                serialized.settings.lightType.SetEnumValue(LightType.Rectangle);
                                break;
                            case AreaSubtype.Disc:
                                serialized.settings.lightType.SetEnumValue(LightType.Disc);
                                break;
                            case AreaSubtype.Tube:
                                serialized.settings.lightType.SetEnumValue(LightType.Tube);
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                        UpdateLightIntensityUnit(serialized, owner);
                    }
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    if (lightType == LightType.Rectangle)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(serialized.shapeWidth, s_Styles.shapeWidthRect);
                        EditorGUILayout.PropertyField(serialized.shapeHeight, s_Styles.shapeHeightRect);
                        if (ShaderConfig.s_BarnDoor == 1)
                        {
                            EditorGUILayout.PropertyField(serialized.barnDoorAngle, s_Styles.barnDoorAngle);
                            EditorGUILayout.PropertyField(serialized.barnDoorLength, s_Styles.barnDoorLength);
                        }
                        if (EditorGUI.EndChangeCheck())
                        {
                            serialized.settings.areaSizeX.floatValue = serialized.shapeWidth.floatValue;
                            serialized.settings.areaSizeY.floatValue = serialized.shapeHeight.floatValue;
                            if (ShaderConfig.s_BarnDoor == 1)
                            {
                                serialized.barnDoorAngle.floatValue = Mathf.Clamp(serialized.barnDoorAngle.floatValue, 0.0f, 90.0f);
                                serialized.barnDoorLength.floatValue = Mathf.Clamp(serialized.barnDoorLength.floatValue, 0.0f, float.MaxValue);
                            }
                        }
                    }
                    else if (lightType == LightType.Disc)
                    {
                        //draw the built-in area light control at the moment as everything is handled by built-in
                        serialized.settings.DrawArea();
                        serialized.displayAreaLightEmissiveMesh.boolValue = false; //force deactivate emissive mesh for Disc (not supported)
                    }
                    else if (lightType == LightType.Tube)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(serialized.shapeWidth, s_Styles.shapeWidthTube);
                        if (EditorGUI.EndChangeCheck())
                        {
                            // Fake line with a small rectangle in vanilla unity for GI
                            serialized.settings.areaSizeX.floatValue = serialized.shapeWidth.floatValue;
                            serialized.settings.areaSizeY.floatValue = HDAdditionalLightData.k_MinLightSize;
                        }
                        // If realtime GI is enabled and the shape is unsupported or not implemented, show a warning.
                        if (serialized.settings.isRealtime && SupportedRenderingFeatures.active.enlighten && GetLightingSettingsOrDefaultsFallback.Invoke().realtimeGI)
                        {
                            EditorGUILayout.HelpBox(s_Styles.unsupportedLightShapeWarning, MessageType.Warning);
                        }
                    }
                    else
                    {
                        Debug.Assert(false, "Not implemented area light shape");
                    }
                }
            }
            else
            {
                Debug.Assert(false, "Not implemented light type");
            }

            if (EditorGUI.EndChangeCheck())
            {
                serialized.needUpdateAreaLightEmissiveMeshComponents = true;
                SetLightsDirty(owner); // Should be apply only to parameter that's affect GI, but make the code cleaner
            }
        }

        static readonly int k_DiameterPopupWidth = 70;
        static readonly string[] k_DiameterModeNames = new string[] { "Multiply", "Override" };
        static void AngularDiameterOverrideField(SerializedHDLight serialized)
        {
            var rect = EditorGUILayout.GetControlRect();
            rect.xMax -= k_DiameterPopupWidth + 2;

            var popupRect = rect;
            popupRect.x = rect.xMax + 2 - EditorGUI.indentLevel * 15;
            popupRect.width = k_DiameterPopupWidth + EditorGUI.indentLevel * 15;

            int mode = serialized.diameterMultiplerMode.boolValue ? 0 : 1;
            mode = EditorGUI.Popup(popupRect, mode, k_DiameterModeNames);
            serialized.diameterMultiplerMode.boolValue = mode == 0 ? true : false;

            EditorGUI.BeginProperty(rect, GUIContent.none, serialized.diameterMultiplerMode);
            if (mode == 0)
            {
                EditorGUI.PropertyField(rect, serialized.diameterMultiplier, s_Styles.diameterMultiplier);
            }
            else if (mode == 1)
            {
                EditorGUI.PropertyField(rect, serialized.diameterOverride, s_Styles.diameterOverride);
            }
            EditorGUI.EndProperty();
        }

        static readonly GUIContent[] k_BodyTypeNames = new GUIContent[] { new GUIContent("Star"), new GUIContent("Moon") };
        static void BodyTypeField(SerializedHDLight serialized)
        {
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, GUIContent.none, serialized.diameterMultiplerMode);
            int mode = serialized.emissiveLightSource.boolValue ? 0 : 1;
            mode = EditorGUI.Popup(rect, s_Styles.bodyType, mode, k_BodyTypeNames);
            serialized.emissiveLightSource.boolValue = mode == 0 ? true : false;
            EditorGUI.EndProperty();

            EditorGUI.indentLevel++;
            if (!serialized.emissiveLightSource.boolValue)
            {
                EditorGUILayout.PropertyField(serialized.automaticMoonPhase);
                if (!serialized.automaticMoonPhase.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serialized.moonPhase);
                    EditorGUILayout.PropertyField(serialized.moonPhaseRotation);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.PropertyField(serialized.earthshine);
            }
            EditorGUI.indentLevel--;
        }

        static void DrawCelestialBodyContent(SerializedHDLight serialized, Editor owner)
        {
            EditorGUI.BeginChangeCheck();
            {
                EditorGUILayout.PropertyField(serialized.interactsWithSky, s_Styles.interactsWithSky);

                using (new EditorGUI.DisabledScope(!serialized.interactsWithSky.boolValue))
                {
                    EditorGUI.indentLevel++;
                    AngularDiameterOverrideField(serialized);
                    BodyTypeField(serialized);
                    EditorGUILayout.PropertyField(serialized.flareSize, s_Styles.flareSize);
                    EditorGUILayout.PropertyField(serialized.flareFalloff, s_Styles.flareFalloff);
                    EditorGUILayout.PropertyField(serialized.flareTint, s_Styles.flareTint);
                    EditorGUILayout.PropertyField(serialized.surfaceTexture, s_Styles.surfaceTexture);
                    EditorGUILayout.PropertyField(serialized.surfaceTint, s_Styles.surfaceTint);
                    EditorGUILayout.PropertyField(serialized.distance, s_Styles.distance);
                    EditorGUI.indentLevel--;
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                // Clamp the value and also affect baked shadows.
                serialized.flareSize.floatValue = Mathf.Clamp(serialized.flareSize.floatValue, 0, 90);
                serialized.flareFalloff.floatValue = Mathf.Max(serialized.flareFalloff.floatValue, 0);
                serialized.distance.floatValue = Mathf.Max(serialized.distance.floatValue, 0);
                serialized.diameterMultiplier.floatValue = Mathf.Max(serialized.diameterMultiplier.floatValue, 0);
                serialized.diameterOverride.floatValue = Mathf.Clamp(serialized.diameterOverride.floatValue, 0, 90);

                if (serialized.surfaceTexture.objectReferenceValue is Texture surfaceTexture && surfaceTexture != null)
                {
                    if (surfaceTexture.dimension != TextureDimension.Tex2D)
                    {
                        Debug.LogError($"The texture '{surfaceTexture.name}' isn't compatible with the Celestial Body Surface Texture property. Only 2D textures are supported.");
                        serialized.surfaceTexture.objectReferenceValue = null;
                    }
                }
            }
        }

        static void UpdateLightIntensityUnit(SerializedHDLight serialized, Editor owner)
        {
            LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();
            // Box are local directional light
            if (lightType == LightType.Directional || lightType == LightType.Box)
            {
                serialized.lightUnit.SetEnumValue((LightUnit)DirectionalLightUnit.Lux);
                // We need to reset luxAtDistance to neutral when changing to (local) directional light, otherwise first display value ins't correct
                serialized.luxAtDistance.floatValue = 1.0f;
            }
        }

        internal static LightUnit DrawLightIntensityUnitPopup(Rect rect, LightUnit value, LightType type)
        {
            switch (type)
            {
                case LightType.Directional:
                    return (LightUnit)EditorGUI.EnumPopup(rect, (DirectionalLightUnit)value);
                case LightType.Point:
                    return (LightUnit)EditorGUI.EnumPopup(rect, (PunctualLightUnit)value);
                case LightType.Spot:
                case LightType.Pyramid:
                        return (LightUnit)EditorGUI.EnumPopup(rect, (PunctualLightUnit)value);
                case LightType.Box:
                        return (LightUnit)EditorGUI.EnumPopup(rect, (DirectionalLightUnit)value);
                default:
                    return (LightUnit)EditorGUI.EnumPopup(rect, (AreaLightUnit)value);
            }
        }

        static void DrawLightIntensityUnitPopup(Rect rect, SerializedHDLight serialized, Editor owner)
        {
            LightUnit oldLigthUnit = serialized.lightUnit.GetEnumValue<LightUnit>();

            EditorGUI.BeginChangeCheck();

            EditorGUI.BeginProperty(rect, GUIContent.none, serialized.lightUnit);
            EditorGUI.showMixedValue = serialized.lightUnit.hasMultipleDifferentValues;
            var selectedLightUnit = DrawLightIntensityUnitPopup(rect, serialized.lightUnit.GetEnumValue<LightUnit>(), serialized.settings.lightType.GetEnumValue<LightType>());
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();

            if (EditorGUI.EndChangeCheck())
            {
                ConvertLightIntensity(oldLigthUnit, selectedLightUnit, serialized, owner);
                serialized.lightUnit.SetEnumValue(selectedLightUnit);
            }
        }

        internal static void ConvertLightIntensity(LightUnit oldLightUnit, LightUnit newLightUnit, SerializedHDLight serialized, Editor owner)
        {
            serialized.intensity.floatValue = ConvertLightIntensity(oldLightUnit, newLightUnit, serialized, owner, serialized.intensity.floatValue);
        }

        internal static float ConvertLightIntensity(LightUnit oldLightUnit, LightUnit newLightUnit, SerializedHDLight serialized, Editor owner, float intensity)
        {
            if (serialized.HasMultipleLightTypes(owner))
                return intensity;

            Light light = (Light)owner.target;

            // For punctual lights
            LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();
            switch (lightType)
            {
                case LightType.Directional:
                case LightType.Point:
                case LightType.Spot:
                case LightType.Pyramid:
                case LightType.Box:
                    // Lumen ->
                    if (oldLightUnit == LightUnit.Lumen && newLightUnit == LightUnit.Candela)
                        intensity = LightUtils.ConvertPunctualLightLumenToCandela(lightType, intensity, light.intensity, serialized.enableSpotReflector.boolValue);
                    else if (oldLightUnit == LightUnit.Lumen && newLightUnit == LightUnit.Lux)
                        intensity = LightUtils.ConvertPunctualLightLumenToLux(lightType, intensity, light.intensity, serialized.enableSpotReflector.boolValue,
                            serialized.luxAtDistance.floatValue);
                    else if (oldLightUnit == LightUnit.Lumen && newLightUnit == LightUnit.Ev100)
                        intensity = LightUtils.ConvertPunctualLightLumenToEv(lightType, intensity, light.intensity, serialized.enableSpotReflector.boolValue);
                    // Candela ->
                    else if (oldLightUnit == LightUnit.Candela && newLightUnit == LightUnit.Lumen)
                        intensity = LightUtils.ConvertPunctualLightCandelaToLumen(lightType, intensity, serialized.enableSpotReflector.boolValue,
                            light.spotAngle, serialized.aspectRatio.floatValue);
                    else if (oldLightUnit == LightUnit.Candela && newLightUnit == LightUnit.Lux)
                        intensity = LightUtils.ConvertCandelaToLux(intensity, serialized.luxAtDistance.floatValue);
                    else if (oldLightUnit == LightUnit.Candela && newLightUnit == LightUnit.Ev100)
                        intensity = LightUtils.ConvertCandelaToEv(intensity);
                    // Lux ->
                    else if (oldLightUnit == LightUnit.Lux && newLightUnit == LightUnit.Lumen)
                        intensity = LightUtils.ConvertPunctualLightLuxToLumen(lightType, intensity, serialized.enableSpotReflector.boolValue,
                            light.spotAngle, serialized.aspectRatio.floatValue, serialized.luxAtDistance.floatValue);
                    else if (oldLightUnit == LightUnit.Lux && newLightUnit == LightUnit.Candela)
                        intensity = LightUtils.ConvertLuxToCandela(intensity, serialized.luxAtDistance.floatValue);
                    else if (oldLightUnit == LightUnit.Lux && newLightUnit == LightUnit.Ev100)
                        intensity = LightUtils.ConvertLuxToEv(intensity, serialized.luxAtDistance.floatValue);
                    // EV100 ->
                    else if (oldLightUnit == LightUnit.Ev100 && newLightUnit == LightUnit.Lumen)
                        intensity = LightUtils.ConvertPunctualLightEvToLumen(lightType, intensity, serialized.enableSpotReflector.boolValue,
                            light.spotAngle, serialized.aspectRatio.floatValue);
                    else if (oldLightUnit == LightUnit.Ev100 && newLightUnit == LightUnit.Candela)
                        intensity = LightUtils.ConvertEvToCandela(intensity);
                    else if (oldLightUnit == LightUnit.Ev100 && newLightUnit == LightUnit.Lux)
                        intensity = LightUtils.ConvertEvToLux(intensity, serialized.luxAtDistance.floatValue);
                    break;

                case LightType.Rectangle:
                case LightType.Tube:
                case LightType.Disc:
                    if (oldLightUnit == LightUnit.Lumen && newLightUnit == LightUnit.Nits)
                        intensity = LightUtils.ConvertAreaLightLumenToLuminance(lightType, intensity, serialized.shapeWidth.floatValue, serialized.shapeHeight.floatValue);
                    if (oldLightUnit == LightUnit.Nits && newLightUnit == LightUnit.Lumen)
                        intensity = LightUtils.ConvertAreaLightLuminanceToLumen(lightType, intensity, serialized.shapeWidth.floatValue, serialized.shapeHeight.floatValue);
                    if (oldLightUnit == LightUnit.Nits && newLightUnit == LightUnit.Ev100)
                        intensity = LightUtils.ConvertLuminanceToEv(intensity);
                    if (oldLightUnit == LightUnit.Ev100 && newLightUnit == LightUnit.Nits)
                        intensity = LightUtils.ConvertEvToLuminance(intensity);
                    if (oldLightUnit == LightUnit.Ev100 && newLightUnit == LightUnit.Lumen)
                        intensity = LightUtils.ConvertAreaLightEvToLumen(lightType, intensity, serialized.shapeWidth.floatValue, serialized.shapeHeight.floatValue);
                    if (oldLightUnit == LightUnit.Lumen && newLightUnit == LightUnit.Ev100)
                        intensity = LightUtils.ConvertAreaLightLumenToEv(lightType, intensity, serialized.shapeWidth.floatValue, serialized.shapeHeight.floatValue);
                    break;
            }

            return intensity;
        }

        static void DrawLightIntensityGUILayout(SerializedHDLight serialized, Editor owner)
        {
            // Match const defined in EditorGUI.cs
            const int k_IndentPerLevel = 15;

            const int k_ValueUnitSeparator = 2;
            const int k_UnitWidth = 100;

            float indent = k_IndentPerLevel * EditorGUI.indentLevel;

            Rect lineRect = EditorGUILayout.GetControlRect();
            Rect labelRect = lineRect;
            labelRect.width = EditorGUIUtility.labelWidth;

            // Expand to reach both lines of the intensity field.
            var interlineOffset = EditorGUIUtility.singleLineHeight + 2f;
            labelRect.height += interlineOffset;

            //handling of prefab overrides in a parent label
            GUIContent parentLabel = s_Styles.lightIntensity;
            parentLabel = EditorGUI.BeginProperty(labelRect, parentLabel, serialized.lightUnit);
            parentLabel = EditorGUI.BeginProperty(labelRect, parentLabel, serialized.intensity);
            {
                // Restore the original rect for actually drawing the label.
                labelRect.height -= interlineOffset;

                EditorGUI.LabelField(labelRect, parentLabel);
            }
            EditorGUI.EndProperty();
            EditorGUI.EndProperty();

            // Draw the light unit slider + icon + tooltip
            Rect lightUnitSliderRect = lineRect; // TODO: Move the value and unit rects to new line
            lightUnitSliderRect.x += EditorGUIUtility.labelWidth + k_ValueUnitSeparator;
            lightUnitSliderRect.width -= EditorGUIUtility.labelWidth + k_ValueUnitSeparator;

            var lightType = serialized.settings.lightType.GetEnumValue<LightType>();
            var lightUnit = serialized.lightUnit.GetEnumValue<LightUnit>();
            k_LightUnitSliderUIDrawer.SetSerializedObject(serialized.serializedObject);
            k_LightUnitSliderUIDrawer.Draw(lightType, lightUnit, serialized.intensity, lightUnitSliderRect, serialized, owner);

            // We use PropertyField to draw the value to keep the handle at left of the field
            // This will apply the indent again thus we need to remove it time for alignment
            Rect valueRect = EditorGUILayout.GetControlRect();
            labelRect.width = EditorGUIUtility.labelWidth;
            valueRect.width += indent - k_ValueUnitSeparator - k_UnitWidth;
            Rect unitRect = valueRect;
            unitRect.x += valueRect.width - indent + k_ValueUnitSeparator;
            unitRect.width = k_UnitWidth + .5f;

            // Draw the unit textfield
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(valueRect, serialized.intensity, CoreEditorStyles.empty);
            DrawLightIntensityUnitPopup(unitRect, serialized, owner);

            if (EditorGUI.EndChangeCheck())
            {
                serialized.intensity.floatValue = Mathf.Max(serialized.intensity.floatValue, 0.0f);
            }
        }

        static void DrawEmissionContent(SerializedHDLight serialized, Editor owner)
        {
            LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();
            LightUnit lightUnit = serialized.lightUnit.GetEnumValue<LightUnit>();

            if (lightType != LightType.Directional
                // Box are local directional light and shouldn't display the Lux At widget. It use only lux
                && lightType != LightType.Box
                && lightUnit == (LightUnit)PunctualLightUnit.Lux)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.luxAtDistance, s_Styles.luxAtDistance);
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.luxAtDistance.floatValue = Mathf.Max(serialized.luxAtDistance.floatValue, 0.01f);
                }
                EditorGUI.indentLevel--;
            }

            if ((lightType == LightType.Spot || lightType == LightType.Pyramid)
                // Display reflector only when showing additional properties.
                && (lightUnit == (int)PunctualLightUnit.Lumen && k_AdditionalPropertiesState[AdditionalProperties.Emission]))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serialized.enableSpotReflector, s_Styles.enableSpotReflector);
                EditorGUI.indentLevel--;
            }

            if (lightType != LightType.Directional)
            {
                EditorGUI.BeginChangeCheck();
#if UNITY_2020_1_OR_NEWER
                serialized.settings.DrawRange();
#else
                serialized.settings.DrawRange(false);
#endif
                // Make sure the range is not 0.0
                serialized.settings.range.floatValue = Mathf.Max(0.001f, serialized.settings.range.floatValue);

                if (EditorGUI.EndChangeCheck())
                {
                    // For GI we need to detect any change on additional data and call SetLightDirty + For intensity we need to detect light shape change
                    serialized.needUpdateAreaLightEmissiveMeshComponents = true;
                    SetLightsDirty(owner); // Should be apply only to parameter that's affect GI, but make the code cleaner
                }
            }

            serialized.settings.DrawBounceIntensity();

            EditorGUI.BeginChangeCheck(); // For GI we need to detect any change on additional data and call SetLightDirty

            if (!lightType.IsArea())
            {
                serialized.settings.DrawCookie();

                if (serialized.settings.cookie is Texture cookie && cookie != null)
                {
                    // When directional light use a cookie, it can control the size
                    if (lightType == LightType.Directional)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUI.BeginChangeCheck();
                        var size = new Vector2(serialized.shapeWidth.floatValue, serialized.shapeHeight.floatValue);
                        size = EditorGUILayout.Vector2Field(s_Styles.cookieSize, size);
                        if (EditorGUI.EndChangeCheck())
                        {
                            serialized.shapeWidth.floatValue = size.x;
                            serialized.shapeHeight.floatValue = size.y;
                        }
                        EditorGUI.indentLevel--;
                    }
                    else if (lightType == LightType.Point && cookie.dimension != TextureDimension.Cube)
                    {
                        Debug.LogError($"The cookie texture '{cookie.name}' isn't compatible with the Point Light type. Only Cube textures are supported.");
                        serialized.settings.cookieProp.objectReferenceValue = null;
                    }
                    else if (lightType.IsSpot() && cookie.dimension != TextureDimension.Tex2D)
                    {
                        Debug.LogError($"The cookie texture '{cookie.name}' isn't compatible with the Spot Light type. Only 2D textures are supported.");
                        serialized.settings.cookieProp.objectReferenceValue = null;
                    }
                }

                ShowCookieTextureWarnings(serialized.settings.cookie, serialized.settings.isCompletelyBaked || serialized.settings.isBakedOrMixed);
            }
            else if (lightType == LightType.Rectangle || lightType == LightType.Disc)
            {
                serialized.settings.DrawCookieProperty(serialized.areaLightCookie, s_Styles.areaLightCookie, lightType);
                ShowCookieTextureWarnings(serialized.areaLightCookie.objectReferenceValue as Texture, serialized.settings.isCompletelyBaked || serialized.settings.isBakedOrMixed);
            }
            if (lightType == LightType.Point || lightType.IsSpot() || lightType == LightType.Rectangle)
            {
                EditorGUI.BeginChangeCheck();
                UnityEngine.Object iesAsset = EditorGUILayout.ObjectField(
                    s_Styles.iesTexture,
                    lightType == LightType.Point ? serialized.iesPoint.objectReferenceValue : serialized.iesSpot.objectReferenceValue,
                    typeof(IESObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    SerializedProperty pointTex = serialized.iesPoint;
                    SerializedProperty spotTex = serialized.iesSpot;
                    if (iesAsset == null)
                    {
                        pointTex.objectReferenceValue = null;
                        spotTex.objectReferenceValue = null;
                    }
                    else
                    {
                        string guid;
                        long localID;
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(iesAsset, out guid, out localID);
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        UnityEngine.Object[] textures = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
                        foreach (var subAsset in textures)
                        {
                            if (AssetDatabase.IsSubAsset(subAsset) && subAsset.name.EndsWith("-Cube-IES"))
                            {
                                pointTex.objectReferenceValue = subAsset;
                            }
                            else if (AssetDatabase.IsSubAsset(subAsset) && subAsset.name.EndsWith("-2D-IES"))
                            {
                                spotTex.objectReferenceValue = subAsset;
                            }
                        }
                    }
                    serialized.iesPoint.serializedObject.ApplyModifiedProperties();
                    serialized.iesSpot.serializedObject.ApplyModifiedProperties();
                }

                if (lightType == LightType.Spot && serialized.iesSpot.objectReferenceValue != null)
                {
                    EditorGUILayout.PropertyField(serialized.spotIESCutoffPercent, s_Styles.spotIESCutoffPercent);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                serialized.needUpdateAreaLightEmissiveMeshComponents = true;
                SetLightsDirty(owner); // Should be apply only to parameter that's affect GI, but make the code cleaner
            }
        }

        static void ShowCookieTextureWarnings(Texture cookie, bool useBaking)
        {
            if (cookie == null)
                return;

            // The texture type is stored in the texture importer so we need to get it:
            TextureImporter texImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(cookie)) as TextureImporter;

            if (texImporter != null && texImporter.textureType == TextureImporterType.Cookie)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int indentSpace = (int)EditorGUI.IndentedRect(new Rect()).x;
                    GUILayout.Space(indentSpace);
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        int oldIndentLevel = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        GUIStyle wordWrap = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                        EditorGUILayout.LabelField(s_Styles.cookieTextureTypeError, wordWrap);
                        if (GUILayout.Button("Fix", GUILayout.ExpandHeight(true)))
                        {
                            texImporter.textureType = TextureImporterType.Default;
                            texImporter.SaveAndReimport();
                        }
                        EditorGUI.indentLevel = oldIndentLevel;
                    }
                }
            }

            if (useBaking && !UnityEditor.EditorSettings.enableCookiesInLightmapper)
                EditorGUILayout.HelpBox(s_Styles.cookieBaking, MessageType.Warning);
            if (cookie.width != cookie.height)
                EditorGUILayout.HelpBox(s_Styles.cookieNonPOT, MessageType.Warning);
            if (cookie.width < LightCookieManager.k_MinCookieSize || cookie.height < LightCookieManager.k_MinCookieSize)
                EditorGUILayout.HelpBox(s_Styles.cookieTooSmall, MessageType.Warning);
        }

        static void DrawEmissionAdditionalContent(SerializedHDLight serialized, Editor owner)
        {
            LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();
            EditorGUI.BeginChangeCheck(); // For GI we need to detect any change on additional data and call SetLightDirty

            bool bakedOnly = serialized.settings.isCompletelyBaked;
            if (!bakedOnly)
            {
                EditorGUILayout.PropertyField(serialized.affectDiffuse, s_Styles.affectDiffuse);
                EditorGUILayout.PropertyField(serialized.affectSpecular, s_Styles.affectSpecular);
                if (lightType != LightType.Directional)
                {
                    EditorGUILayout.PropertyField(serialized.applyRangeAttenuation, s_Styles.applyRangeAttenuation);
                    EditorGUILayout.PropertyField(serialized.fadeDistance, s_Styles.fadeDistance);
                }
                EditorGUILayout.PropertyField(serialized.lightDimmer, s_Styles.lightDimmer);
            }
            else if (lightType == LightType.Point || lightType.IsSpot())
                EditorGUILayout.PropertyField(serialized.applyRangeAttenuation, s_Styles.applyRangeAttenuation);

            // Emissive mesh for area light only (and not supported on Disc currently)
            if (lightType == LightType.Rectangle || lightType == LightType.Tube)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.displayAreaLightEmissiveMesh, s_Styles.displayAreaLightEmissiveMesh);
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.FetchAreaLightEmissiveMeshComponents();
                    serialized.needUpdateAreaLightEmissiveMeshComponents = true;
                }

                bool showSubArea = serialized.displayAreaLightEmissiveMesh.boolValue && !serialized.displayAreaLightEmissiveMesh.hasMultipleDifferentValues;
                ++EditorGUI.indentLevel;

                Rect lineRect = EditorGUILayout.GetControlRect();
                ShadowCastingMode newCastShadow;
                EditorGUI.showMixedValue = serialized.areaLightEmissiveMeshCastShadow.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                using (new SerializedHDLight.AreaLightEmissiveMeshDrawScope(lineRect, s_Styles.areaLightEmissiveMeshCastShadow, showSubArea, serialized.areaLightEmissiveMeshCastShadow, serialized.deportedAreaLightEmissiveMeshCastShadow))
                {
                    newCastShadow = (ShadowCastingMode)EditorGUI.EnumPopup(lineRect, s_Styles.areaLightEmissiveMeshCastShadow, (ShadowCastingMode)serialized.areaLightEmissiveMeshCastShadow.intValue);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.UpdateAreaLightEmissiveMeshCastShadow(newCastShadow);
                }
                EditorGUI.showMixedValue = false;

                lineRect = EditorGUILayout.GetControlRect();
                SerializedHDLight.MotionVector newMotionVector;
                EditorGUI.showMixedValue = serialized.areaLightEmissiveMeshMotionVector.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                using (new SerializedHDLight.AreaLightEmissiveMeshDrawScope(lineRect, s_Styles.areaLightEmissiveMeshMotionVector, showSubArea, serialized.areaLightEmissiveMeshMotionVector, serialized.deportedAreaLightEmissiveMeshMotionVector))
                {
                    newMotionVector = (SerializedHDLight.MotionVector)EditorGUI.EnumPopup(lineRect, s_Styles.areaLightEmissiveMeshMotionVector, (SerializedHDLight.MotionVector)serialized.areaLightEmissiveMeshMotionVector.intValue);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.UpdateAreaLightEmissiveMeshMotionVectorGeneration(newMotionVector);
                }
                EditorGUI.showMixedValue = false;

                EditorGUI.showMixedValue = serialized.areaLightEmissiveMeshLayer.hasMultipleDifferentValues || serialized.lightLayer.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                bool toggle;
                using (new SerializedHDLight.AreaLightEmissiveMeshDrawScope(lineRect, s_Styles.areaLightEmissiveMeshSameLayer, showSubArea, serialized.areaLightEmissiveMeshLayer, serialized.deportedAreaLightEmissiveMeshLayer))
                {
                    toggle = EditorGUILayout.Toggle(s_Styles.areaLightEmissiveMeshSameLayer, serialized.areaLightEmissiveMeshLayer.intValue == -1);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    serialized.UpdateAreaLightEmissiveMeshLayer(serialized.lightLayer.intValue);
                    if (toggle)
                        serialized.areaLightEmissiveMeshLayer.intValue = -1;
                }
                EditorGUI.showMixedValue = false;

                ++EditorGUI.indentLevel;
                if (toggle || serialized.areaLightEmissiveMeshLayer.hasMultipleDifferentValues)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        lineRect = EditorGUILayout.GetControlRect();
                        EditorGUI.showMixedValue = serialized.areaLightEmissiveMeshLayer.hasMultipleDifferentValues || serialized.lightLayer.hasMultipleDifferentValues;
                        EditorGUI.LayerField(lineRect, s_Styles.areaLightEmissiveMeshCustomLayer, serialized.lightLayer.intValue);
                        EditorGUI.showMixedValue = false;
                    }
                }
                else
                {
                    EditorGUI.showMixedValue = serialized.areaLightEmissiveMeshLayer.hasMultipleDifferentValues;
                    lineRect = EditorGUILayout.GetControlRect();
                    int layer;
                    EditorGUI.BeginChangeCheck();
                    using (new SerializedHDLight.AreaLightEmissiveMeshDrawScope(lineRect, s_Styles.areaLightEmissiveMeshCustomLayer, showSubArea, serialized.areaLightEmissiveMeshLayer, serialized.deportedAreaLightEmissiveMeshLayer))
                    {
                        layer = EditorGUI.LayerField(lineRect, s_Styles.areaLightEmissiveMeshCustomLayer, serialized.areaLightEmissiveMeshLayer.intValue);
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        serialized.UpdateAreaLightEmissiveMeshLayer(layer);
                    }
                    // or if the value of layer got changed using the layer change including child mechanism (strangely apply even if object not editable),
                    // discard the change: the child is not saved anyway so the value in HDAdditionalLightData is the only serialized one.
                    else if (!EditorGUI.showMixedValue
                             && serialized.deportedAreaLightEmissiveMeshLayer != null
                             && !serialized.deportedAreaLightEmissiveMeshLayer.Equals(null)
                             && serialized.areaLightEmissiveMeshLayer.intValue != serialized.deportedAreaLightEmissiveMeshLayer.intValue)
                    {
                        GUI.changed = true; //force register change to handle update and apply later
                        serialized.UpdateAreaLightEmissiveMeshLayer(layer);
                    }
                    EditorGUI.showMixedValue = false;
                }
                --EditorGUI.indentLevel;

                --EditorGUI.indentLevel;
            }

            EditorGUILayout.PropertyField(serialized.includeForRayTracing, s_Styles.includeLightForRayTracing);

            if (EditorGUI.EndChangeCheck())
            {
                serialized.needUpdateAreaLightEmissiveMeshComponents = true;
                serialized.fadeDistance.floatValue = Mathf.Max(serialized.fadeDistance.floatValue, 0.01f);
                SetLightsDirty(owner); // Should be apply only to parameter that's affect GI, but make the code cleaner
            }
        }

        static void DrawVolumetric(SerializedHDLight serialized, Editor owner)
        {
            LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();

            // Right now the only supported area light type in path tracing is rectangle lights.
            // Modify this if this changes to add new area light shapees.
            if (lightType == LightType.Rectangle)
            {
                EditorGUILayout.HelpBox(s_Styles.areaLightVolumetricsWarning.text, MessageType.Warning);
            }

            EditorGUILayout.PropertyField(serialized.useVolumetric, s_Styles.volumetricEnable);
            {
                using (new EditorGUI.DisabledScope(!serialized.useVolumetric.boolValue))
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(serialized.volumetricDimmer, s_Styles.volumetricDimmer);
                    EditorGUILayout.Slider(serialized.volumetricShadowDimmer, 0.0f, 1.0f, s_Styles.volumetricShadowDimmer);
                    if (lightType != LightType.Directional)
                    {
                        EditorGUILayout.PropertyField(serialized.volumetricFadeDistance, s_Styles.volumetricFadeDistance);
                    }
                }
            }
        }

        static bool DrawEnableShadowMap(SerializedHDLight serialized, Editor owner)
        {
            Rect lineRect = EditorGUILayout.GetControlRect();
            bool newShadowsEnabled;

            EditorGUI.BeginProperty(lineRect, s_Styles.enableShadowMap, serialized.settings.shadowsType);
            {
                bool oldShadowEnabled = serialized.settings.shadowsType.GetEnumValue<LightShadows>() != LightShadows.None;
                newShadowsEnabled = EditorGUI.Toggle(lineRect, s_Styles.enableShadowMap, oldShadowEnabled);
                if (oldShadowEnabled ^ newShadowsEnabled)
                {
                    serialized.settings.shadowsType.SetEnumValue(newShadowsEnabled ? LightShadows.Hard : LightShadows.None);
                }
            }
            EditorGUI.EndProperty();

            return newShadowsEnabled;
        }

        // Needed to work around the need for CED Group with no return value
        static void DrawEnableShadowMapInternal(SerializedHDLight serialized, Editor owner)
        {
            DrawEnableShadowMap(serialized, owner);
        }

        static IntScalableSetting ShadowResolutionUnknown3Levels = new IntScalableSetting(new[] { -1, -1, -1,}, ScalableSettingSchemaId.With3Levels);
        static IntScalableSetting ShadowResolutionUnknown4Levels = new IntScalableSetting(new[] { -1, -1, -1, -1 }, ScalableSettingSchemaId.With4Levels);

        static void DrawShadowMapContent(SerializedHDLight serialized, Editor owner)
        {
            var hdrp = HDRenderPipeline.currentAsset;
            bool newShadowsEnabled = DrawEnableShadowMap(serialized, owner);


            LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();

            using (new EditorGUI.DisabledScope(!newShadowsEnabled))
            {
                EditorGUILayout.PropertyField(serialized.shadowUpdateMode, s_Styles.shadowUpdateMode);

                EditorGUI.indentLevel++;

                if (serialized.shadowUpdateMode.intValue > 0 && lightType != LightType.Directional)
                {
                    if (owner.targets.Length == 1)
                    {
                        HDLightEditor editor = owner as HDLightEditor;
                        var additionalLightData = editor.GetAdditionalDataForTargetIndex(0);
                        // If the light was registered, but not placed it means it doesn't fit.
                        if (additionalLightData.lightIdxForCachedShadows >= 0 && !HDCachedShadowManager.instance.LightHasBeenPlacedInAtlas(additionalLightData))
                        {
                            string warningMessage = "The shadow for this light doesn't fit the cached shadow atlas and therefore won't be rendered. Please ensure you have enough space in the cached shadow atlas. You can use the light explorer (Window->Rendering->Light Explorer) to see which lights fit and which don't.\nConsult HDRP Shadow documentation for more information about cached shadow management.";
                            // Loop backward in "tile" size to check
                            const int slotSize = HDCachedShadowManager.k_MinSlotSize;

                            bool showFitButton = false;
                            if (HDCachedShadowManager.instance.WouldFitInAtlas(slotSize, lightType))
                            {
                                warningMessage += "\nAlternatively, click the button below to find the resolution that will fit the shadow in the atlas.";
                                showFitButton = true;
                            }
                            else
                            {
                                warningMessage += "\nThe atlas is completely full so either change the resolution of other shadow maps or increase atlas size.";
                            }
                            EditorGUILayout.HelpBox(warningMessage, MessageType.Warning);

                            Rect rect = EditorGUILayout.GetControlRect();
                            rect = EditorGUI.IndentedRect(rect);

                            if (showFitButton)
                            {
                                if (GUI.Button(rect, "Set resolution to the maximum that fits"))
                                {
                                    var scalableSetting = ScalableSettings.ShadowResolution(lightType, hdrp);
                                    int res = additionalLightData.GetResolutionFromSettings(lightType, hdrp.currentPlatformRenderPipelineSettings.hdShadowInitParams);
                                    int foundResFit = -1;
                                    // Round up to multiple of slotSize
                                    res = HDUtils.DivRoundUp(res, slotSize) * slotSize;
                                    for (int testRes = res; testRes >= slotSize; testRes -= slotSize)
                                    {
                                        if (HDCachedShadowManager.instance.WouldFitInAtlas(Mathf.Max(testRes, slotSize), lightType))
                                        {
                                            foundResFit = Mathf.Max(testRes, slotSize);
                                            break;
                                        }
                                    }
                                    if (foundResFit > 0)
                                    {
                                        serialized.shadowResolution.useOverride.boolValue = true;
                                        serialized.shadowResolution.@override.intValue = foundResFit;
                                    }
                                    else
                                    {
                                        // Should never reach this point.
                                        Debug.LogWarning("The atlas is completely full.");
                                    }
                                }
                            }
                        }
                    }
                }
#if UNITY_2021_1_OR_NEWER

                if (serialized.shadowUpdateMode.intValue > 0)
                {
                    EditorGUILayout.PropertyField(serialized.shadowUpdateUponTransformChange, s_Styles.shadowUpdateOnLightTransformChange);

                    HDShadowInitParameters hdShadowInitParameters = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
                    if (lightType == LightType.Directional)
                    {
                        if (hdShadowInitParameters.allowDirectionalMixedCachedShadows)
                            EditorGUILayout.PropertyField(serialized.shadowAlwaysDrawDynamic, s_Styles.shadowAlwaysDrawDynamic);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(serialized.shadowAlwaysDrawDynamic, s_Styles.shadowAlwaysDrawDynamic);
                    }

                }

#endif

                EditorGUI.indentLevel--;

                using (var change = new EditorGUI.ChangeCheckScope())
                {
                    if (serialized.HasMultipleLightTypes(owner))
                    {
                        // Get the schema for the first light type selected
                        var scalableSetting = ScalableSettings.ShadowResolution(lightType, hdrp);

                        serialized.shadowResolution.LevelAndIntGUILayout(
                            s_Styles.shadowResolution,
                            scalableSetting.schemaId.Equals(ScalableSettingSchemaId.With3Levels) ? ShadowResolutionUnknown3Levels : ShadowResolutionUnknown4Levels,
                            hdrp.name
                        );
                    }
                    else
                    {
                        var scalableSetting = ScalableSettings.ShadowResolution(lightType, hdrp);

                        serialized.shadowResolution.LevelAndIntGUILayout(
                            s_Styles.shadowResolution, scalableSetting, hdrp.name
                        );
                    }

                    if (change.changed)
                        serialized.shadowResolution.@override.intValue =  serialized.shadowResolution.@override.intValue is >= 1 and <= HDShadowManager.k_MinShadowMapResolution - 1 ? HDShadowManager.k_MinShadowMapResolution
                            : Mathf.Max(0, serialized.shadowResolution.@override.intValue >= HDShadowManager.k_MaxShadowMapResolution ? HDShadowManager.k_MaxShadowMapResolution : serialized.shadowResolution.@override.intValue);

                }

                if (lightType != LightType.Directional)
                    EditorGUILayout.Slider(serialized.shadowNearPlane, 0, HDShadowUtils.k_MaxShadowNearPlane, s_Styles.shadowNearPlane);

                bool fullShadowMask = false;
                if (serialized.settings.isMixed)
                {
                    bool enabled = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.supportShadowMask;
                    if (Lightmapping.TryGetLightingSettings(out var settings))
                        enabled &= settings.mixedBakeMode == MixedLightingMode.Shadowmask;
                    using (new EditorGUI.DisabledScope(!enabled))
                    {
                        Rect nonLightmappedOnlyRect = EditorGUILayout.GetControlRect();
                        EditorGUI.BeginProperty(nonLightmappedOnlyRect, s_Styles.nonLightmappedOnly, serialized.nonLightmappedOnly);
                        {
                            EditorGUI.BeginChangeCheck();
                            ShadowmaskMode shadowmask = serialized.nonLightmappedOnly.boolValue ? ShadowmaskMode.Shadowmask : ShadowmaskMode.DistanceShadowmask;
                            shadowmask = (ShadowmaskMode)EditorGUI.EnumPopup(nonLightmappedOnlyRect, s_Styles.nonLightmappedOnly, shadowmask);
                            fullShadowMask = shadowmask == ShadowmaskMode.Shadowmask;

                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObjects(owner.targets, "Light Update Shadowmask Mode");
                                serialized.nonLightmappedOnly.boolValue = fullShadowMask;
                                foreach (Light target in owner.targets)
                                    target.lightShadowCasterMode = shadowmask == ShadowmaskMode.Shadowmask ? LightShadowCasterMode.NonLightmappedOnly : LightShadowCasterMode.Everything;
                            }
                        }
                        EditorGUI.EndProperty();
                    }
                }

                if (lightType == LightType.Rectangle)
                {
                    EditorGUILayout.Slider(serialized.areaLightShadowCone, HDAdditionalLightData.k_MinAreaLightShadowCone, HDAdditionalLightData.k_MaxAreaLightShadowCone, s_Styles.areaLightShadowCone);
                }

                if (HDRenderPipeline.assetSupportsRayTracing && HDRenderPipeline.pipelineSupportsScreenSpaceShadows)
                {
                    bool isPunctual = lightType == LightType.Point || lightType.IsSpot();
                    if (isPunctual || lightType == LightType.Rectangle)
                    {
                        using (new EditorGUI.DisabledScope(fullShadowMask))
                        {
                            EditorGUILayout.PropertyField(serialized.useRayTracedShadows, s_Styles.useRayTracedShadows);
                            if (serialized.useRayTracedShadows.boolValue)
                            {
                                if (hdrp != null && lightType == LightType.Rectangle
                                    && (hdrp.currentPlatformRenderPipelineSettings.supportedLitShaderMode != RenderPipelineSettings.SupportedLitShaderMode.DeferredOnly))
                                    EditorGUILayout.HelpBox("Ray traced area light shadows are approximated for the Lit shader when not in deferred mode.", MessageType.Warning);

                                EditorGUI.indentLevel++;

                                // We only support semi transparent shadows for punctual lights
                                if (isPunctual)
                                    EditorGUILayout.PropertyField(serialized.semiTransparentShadow, s_Styles.semiTransparentShadow);

                                EditorGUILayout.PropertyField(serialized.numRayTracingSamples, s_Styles.numRayTracingSamples);
                                EditorGUILayout.PropertyField(serialized.filterTracedShadow, s_Styles.denoiseTracedShadow);
                                EditorGUI.indentLevel++;
                                EditorGUILayout.PropertyField(serialized.filterSizeTraced, s_Styles.denoiserRadius);
                                // We only support distance based filtering if we have a punctual light source (point or spot)
                                if (isPunctual)
                                    EditorGUILayout.PropertyField(serialized.distanceBasedFiltering, s_Styles.distanceBasedFiltering);
                                EditorGUI.indentLevel--;
                                EditorGUI.indentLevel--;
                            }
                        }
                    }
                }

                // For the moment, we only support screen space rasterized shadows for directional lights
                if (lightType == LightType.Directional && HDRenderPipeline.pipelineSupportsScreenSpaceShadows)
                {
                    EditorGUILayout.PropertyField(serialized.useScreenSpaceShadows, s_Styles.useScreenSpaceShadows);
                    if (HDRenderPipeline.assetSupportsRayTracing)
                    {
                        bool showRayTraced = serialized.useScreenSpaceShadows.boolValue && !fullShadowMask;
                        using (new EditorGUI.DisabledScope(!showRayTraced))
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(serialized.useRayTracedShadows, s_Styles.useRayTracedShadows);
                            using (new EditorGUI.DisabledScope(!serialized.useRayTracedShadows.boolValue))
                            {
                                EditorGUI.indentLevel++;
                                EditorGUILayout.PropertyField(serialized.numRayTracingSamples, s_Styles.numRayTracingSamples);
                                EditorGUILayout.PropertyField(serialized.colorShadow, s_Styles.colorShadow);
                                EditorGUILayout.PropertyField(serialized.filterTracedShadow, s_Styles.denoiseTracedShadow);
                                using (new EditorGUI.DisabledScope(!serialized.filterTracedShadow.boolValue))
                                {
                                    EditorGUI.indentLevel++;
                                    EditorGUILayout.PropertyField(serialized.filterSizeTraced, s_Styles.denoiserRadius);
                                    EditorGUI.indentLevel--;
                                }
                                EditorGUI.indentLevel--;
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
        }

        static void DrawShadowMapAdditionalContent(SerializedHDLight serialized, Editor owner)
        {
            using (new EditorGUI.DisabledScope(serialized.settings.shadowsType.GetEnumValue<LightShadows>() == LightShadows.None))
            {
                LightType lightType = serialized.settings.lightType.GetEnumValue<LightType>();

                if (lightType == LightType.Rectangle)
                {
                    if (HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams.areaShadowFilteringQuality == HDAreaShadowFilteringQuality.High)
                    {
                        EditorGUILayout.Slider(serialized.slopeBias, 0.0f, 1.0f, s_Styles.slopeBias);
                        EditorGUILayout.Slider(serialized.normalBias, 0.0f, 5.0f, s_Styles.normalBias);
                    }
                    else
                    {
                        EditorGUILayout.Slider(serialized.evsmExponent, HDAdditionalLightData.k_MinEvsmExponent, HDAdditionalLightData.k_MaxEvsmExponent, s_Styles.evsmExponent);
                        EditorGUILayout.Slider(serialized.evsmLightLeakBias, HDAdditionalLightData.k_MinEvsmLightLeakBias, HDAdditionalLightData.k_MaxEvsmLightLeakBias, s_Styles.evsmLightLeakBias);
                        EditorGUILayout.Slider(serialized.evsmVarianceBias, HDAdditionalLightData.k_MinEvsmVarianceBias, HDAdditionalLightData.k_MaxEvsmVarianceBias, s_Styles.evsmVarianceBias);
                        EditorGUILayout.IntSlider(serialized.evsmBlurPasses, HDAdditionalLightData.k_MinEvsmBlurPasses, HDAdditionalLightData.k_MaxEvsmBlurPasses, s_Styles.evsmAdditionalBlurPasses);
                    }
                }
                else
                {
                    EditorGUILayout.Slider(serialized.slopeBias, 0.0f, 1.0f, s_Styles.slopeBias);
                    EditorGUILayout.Slider(serialized.normalBias, 0.0f, 5.0f, s_Styles.normalBias);

                    if (lightType == LightType.Spot || lightType == LightType.Pyramid)
                    {
                        EditorGUILayout.PropertyField(serialized.useCustomSpotLightShadowCone, s_Styles.useCustomSpotLightShadowCone);
                        if (serialized.useCustomSpotLightShadowCone.boolValue)
                        {
                            EditorGUILayout.Slider(serialized.customSpotLightShadowCone, 1.0f, serialized.settings.spotAngle.floatValue, s_Styles.customSpotLightShadowCone);
                        }
                    }
                }

                // Dimmer and Tint don't have effect on baked shadow
                if (!serialized.settings.isCompletelyBaked)
                {
                    EditorGUILayout.Slider(serialized.shadowDimmer, 0.0f, 1.0f, s_Styles.shadowDimmer);
                    EditorGUILayout.PropertyField(serialized.shadowTint, s_Styles.shadowTint);
                    EditorGUILayout.PropertyField(serialized.penumbraTint, s_Styles.penumbraTint);
                }

                if (lightType != LightType.Directional)
                {
                    EditorGUILayout.PropertyField(serialized.shadowFadeDistance, s_Styles.shadowFadeDistance);
                }

                // Shadow Layers
                if (HDUtils.hdrpSettings.supportLightLayers)
                {
                    using (var change = new EditorGUI.ChangeCheckScope())
                    {
                        Rect lineRect = EditorGUILayout.GetControlRect();
                        EditorGUI.BeginProperty(lineRect, s_Styles.unlinkLightAndShadowLayersText, serialized.linkShadowLayers);
                        bool savedHasMultipleDifferentValue = EditorGUI.showMixedValue;
                        EditorGUI.showMixedValue = serialized.linkShadowLayers.hasMultipleDifferentValues;
                        bool newValue = !EditorGUI.Toggle(lineRect, s_Styles.unlinkLightAndShadowLayersText, !serialized.linkShadowLayers.boolValue);
                        EditorGUI.showMixedValue = savedHasMultipleDifferentValue;
                        EditorGUI.EndProperty();

                        // Undo the changes in the light component because the SyncLightAndShadowLayers will change the value automatically when link is ticked
                        if (change.changed)
                        {
                            Undo.RecordObjects(owner.targets, "Undo Light Layers Changed");
                            serialized.linkShadowLayers.boolValue = newValue;
                            if (!newValue)
                            {
                                serialized.Apply(); //we need to push above modification the modification on object as it is used to sync
                                SyncLightAndShadowLayers(serialized, owner);
                            }
                        }
                    }
                    //
                    if (serialized.linkShadowLayers.hasMultipleDifferentValues || !serialized.linkShadowLayers.boolValue)
                    {
                        using (new EditorGUI.DisabledGroupScope(serialized.linkShadowLayers.hasMultipleDifferentValues))
                        {
                            ++EditorGUI.indentLevel;
                            HDEditorUtils.DrawRenderingLayerMask(serialized.settings.renderingLayerMask, s_Styles.shadowLayerMaskText);
                            --EditorGUI.indentLevel;
                        }
                    }
                }
            }
        }

        static void SyncLightAndShadowLayers(SerializedHDLight serialized, Editor owner)
        {
            // If we're not in decoupled mode for light layers, we sync light with shadow layers.
            // In mixed state, it make sens to do it only on Light that links the mode.
            HDLightEditor editor = owner as HDLightEditor;
            for (int i = 0; i < owner.targets.Length; ++i)
            {
                HDAdditionalLightData additionalData = editor.GetAdditionalDataForTargetIndex(i);
                if (!additionalData.linkShadowLayers)
                    continue;

                Light target = owner.targets[i] as Light;
                if (target.renderingLayerMask != serialized.lightlayersMask.intValue)
                    target.renderingLayerMask = serialized.lightlayersMask.intValue;
            }
        }

        static void DrawContactShadowsContent(SerializedHDLight serialized, Editor owner)
        {
            var hdrp = HDRenderPipeline.currentAsset;
            SerializedScalableSettingValueUI.LevelAndToggleGUILayout(
                serialized.contactShadows,
                s_Styles.contactShadows,
                HDAdditionalLightData.ScalableSettings.UseContactShadow(hdrp),
                hdrp.name
            );
            if (HDRenderPipeline.assetSupportsRayTracing
                && serialized.contactShadows.@override.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serialized.rayTracedContactShadow, s_Styles.rayTracedContactShadow);
                EditorGUI.indentLevel--;
            }
        }

        static void DrawBakedShadowsContent(SerializedHDLight serialized, Editor owner)
        {
            DrawEnableShadowMap(serialized, owner);
            if (serialized.settings.lightType.GetEnumValue<LightType>() != LightType.Directional)
                EditorGUILayout.Slider(serialized.shadowNearPlane, 0, HDShadowUtils.k_MaxShadowNearPlane, s_Styles.shadowNearPlane);
        }

        static bool HasPunctualShadowQualitySettingsUI(HDShadowFilteringQuality quality, SerializedHDLight serialized, Editor owner)
        {
            // Handle quality where there is nothing to draw directly here
            // No PCSS for now with directional light
            if (quality == HDShadowFilteringQuality.Medium || quality == HDShadowFilteringQuality.Low)
                return false;

            // Draw shadow settings using the current shadow algorithm

            HDShadowInitParameters hdShadowInitParameters = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
            return hdShadowInitParameters.punctualShadowFilteringQuality == quality;
        }

        static bool HasDirectionalShadowQualitySettingsUI(HDShadowFilteringQuality quality, SerializedHDLight serialized, Editor owner)
        {
            // Handle quality where there is nothing to draw directly here
            // No PCSS for now with directional light
            if (quality == HDShadowFilteringQuality.Medium || quality == HDShadowFilteringQuality.Low)
                return false;

            // Draw shadow settings using the current shadow algorithm

            HDShadowInitParameters hdShadowInitParameters = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
            return hdShadowInitParameters.directionalShadowFilteringQuality == quality;
        }

        static bool HasAreaShadowQualitySettingsUI(HDAreaShadowFilteringQuality quality, SerializedHDLight serialized, Editor owner)
        {
            // Handle quality where there is nothing to draw directly here
            // No PCSS for now with directional light
            if (quality == HDAreaShadowFilteringQuality.Medium)
                return false;

            // Draw shadow settings using the current shadow algorithm

            HDShadowInitParameters hdShadowInitParameters = HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams;
            return hdShadowInitParameters.areaShadowFilteringQuality == quality;
        }

        static void DrawLowShadowSettingsContent(SerializedHDLight serialized, Editor owner)
        {
            // Currently there is nothing to display here
            // when adding something, update IsShadowSettings
        }

        static void DrawMediumShadowSettingsContent(SerializedHDLight serialized, Editor owner)
        {
            // Currently there is nothing to display here
            // when adding something, update IsShadowSettings
        }

        static void DrawHighShadowSettingsContent(SerializedHDLight serialized, Editor owner)
        {
            if (serialized.settings.lightType.GetEnumValue<LightType>() == LightType.Directional)
            {
                EditorGUILayout.PropertyField(serialized.dirLightPCSSMaxBlockerDistance, s_Styles.dirLightPCSSMaxBlockerDistance);
                EditorGUILayout.PropertyField(serialized.dirLightPCSSMaxSamplingDistance, s_Styles.dirLightPCSSMaxSamplingDistance);
                EditorGUILayout.PropertyField(serialized.dirLightPCSSMinFilterSizeTexels, s_Styles.dirLightPCSSMinFilterSizeTexels);
                EditorGUILayout.PropertyField(serialized.dirLightPCSSMinFilterMaxAngularDiameter, s_Styles.dirLightPCSSMinFilterMaxAngularDiameter);
                EditorGUILayout.PropertyField(serialized.dirLightPCSSBlockerSearchAngularDiameter, s_Styles.dirLightPCSSBlockerSearchAngularDiameter);
                EditorGUILayout.PropertyField(serialized.dirLightPCSSBlockerSamplingClumpExponent, s_Styles.dirLightPCSSBlockerSamplingClumpExponent);
                EditorGUILayout.PropertyField(serialized.dirLightPCSSBlockerSampleCount, s_Styles.dirLightPCSSBlockerSampleCount);
                EditorGUILayout.PropertyField(serialized.dirLightPCSSFilterSampleCount, s_Styles.dirLightPCSSFilterSampleCount);
            }
            else
            {
                EditorGUILayout.PropertyField(serialized.blockerSampleCount, s_Styles.blockerSampleCount);
                EditorGUILayout.PropertyField(serialized.filterSampleCount, s_Styles.filterSampleCount);
                EditorGUILayout.PropertyField(serialized.minFilterSize, s_Styles.minFilterSize);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serialized.scaleForSoftness, s_Styles.radiusScaleForSoftness);
                if (EditorGUI.EndChangeCheck())
                {
                    //Clamp the value and also affect baked shadows
                    serialized.scaleForSoftness.floatValue = Mathf.Max(serialized.scaleForSoftness.floatValue, 0);
                }
            }
        }

        static void SetLightsDirty(Editor owner)
        {
            foreach (Light light in owner.targets)
                light.SetLightDirty(); // Should be apply only to parameter that's affect GI, but make the code cleaner
        }
    }
}
