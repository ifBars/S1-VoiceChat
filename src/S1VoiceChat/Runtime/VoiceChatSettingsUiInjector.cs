#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

#if IL2CPPMELON
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppScheduleOne.UI.Settings;
using Il2CppTMPro;
#elif MONOMELON
using ScheduleOne.UI.MainMenu;
using ScheduleOne.UI.Settings;
using TMPro;
#endif

namespace S1VoiceChat.Runtime;

internal sealed class VoiceChatSettingsUiInjector
{
    private const string VolumeRowObjectName = "S1VoiceChatVolume";
    private const string ToggleRowObjectName = "S1VoiceChatOpenMic";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly MelonLogger.Instance _logger;
    private DateTime _nextAttemptUtc = DateTime.MinValue;
    private bool _loggedMissingScreen;
    private bool _loggedMissingSlider;

    public VoiceChatSettingsUiInjector(MelonLogger.Instance logger)
    {
        _logger = logger;
    }

    public void Update()
    {
        if (Application.isBatchMode || DateTime.UtcNow < _nextAttemptUtc)
            return;

        _nextAttemptUtc = DateTime.UtcNow + RetryDelay;

        try
        {
            var screen = UnityEngine.Object.FindObjectOfType<SettingsScreen>(true);
            if (screen == null)
            {
                LogMissingScreenOnce();
                return;
            }

            Inject(screen);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Settings UI injection failed: {ex.Message}");
        }
    }

    private void Inject(SettingsScreen settingsScreen)
    {
        var audioPanel = FindAudioPanel(settingsScreen);
        if (audioPanel == null)
        {
            LogMissingSliderOnce("Could not find Audio settings panel.");
            return;
        }

        var volumeTemplate = FindBottomAudioRow(audioPanel, out var voiceColumn);
        if (volumeTemplate == null)
        {
            LogMissingSliderOnce("Could not find Audio settings row to clone.");
            return;
        }

        var existingVolumeRow = FindChildRecursive(audioPanel.transform, VolumeRowObjectName);
        if (existingVolumeRow == null)
        {
            var sourceSlider = volumeTemplate.GetComponentInChildren<AudioSlider>(true)
                ?? audioPanel.GetComponentInChildren<AudioSlider>(true)
                ?? UnityEngine.Object.FindObjectOfType<AudioSlider>(true);
            if (sourceSlider == null)
            {
                LogMissingSliderOnce("Could not find AudioSlider to clone.");
                return;
            }

            CreateVolumeSlider(voiceColumn, volumeTemplate);
        }
        else
        {
            RefreshVolumeRow(existingVolumeRow.gameObject);
        }

        var existingToggleRow = FindChildRecursive(audioPanel.transform, ToggleRowObjectName);
        if (existingToggleRow == null)
        {
            var anchorRow = FindChildRecursive(voiceColumn, VolumeRowObjectName)?.gameObject ?? volumeTemplate;
            CreateOpenMicToggle(voiceColumn, anchorRow);
        }
        else
        {
            RefreshOpenMicRow(existingToggleRow.gameObject);
        }
    }

    private GameObject? FindAudioPanel(SettingsScreen settingsScreen)
    {
        if (settingsScreen.Categories == null)
            return null;

        for (var i = 0; i < settingsScreen.Categories.Length; i++)
        {
            var category = settingsScreen.Categories[i];
            if (category == null || category.Panel == null)
                continue;

            if (category.Panel.GetComponentInChildren<AudioSlider>(true) != null)
                return category.Panel;
        }

        return null;
    }

    private static GameObject? FindBottomAudioRow(GameObject audioPanel, out Transform column)
    {
        column = audioPanel.transform;
        var sliders = audioPanel.GetComponentsInChildren<AudioSlider>(true);
        var rowsByParent = new Dictionary<Transform, List<GameObject>>();

        for (var i = 0; i < sliders.Length; i++)
        {
            var slider = sliders[i];
            if (slider == null || slider.transform.parent == null || slider.transform.parent.parent == null)
                continue;

            var row = slider.transform.parent.gameObject;
            var parent = row.transform.parent;
            if (!rowsByParent.TryGetValue(parent, out var rows))
            {
                rows = new List<GameObject>();
                rowsByParent[parent] = rows;
            }

            if (!rows.Contains(row))
                rows.Add(row);
        }

        List<GameObject>? selectedRows = null;
        foreach (var entry in rowsByParent)
        {
            if (selectedRows == null || entry.Value.Count > selectedRows.Count)
            {
                column = entry.Key;
                selectedRows = entry.Value;
            }
        }

        if (selectedRows == null || selectedRows.Count == 0)
            return null;

        GameObject? bottomRow = null;
        var bottomY = float.MaxValue;
        for (var i = 0; i < selectedRows.Count; i++)
        {
            var rectTransform = selectedRows[i].GetComponent<RectTransform>();
            if (rectTransform == null)
                continue;

            if (rectTransform.anchoredPosition.y < bottomY)
            {
                bottomY = rectTransform.anchoredPosition.y;
                bottomRow = selectedRows[i];
            }
        }

        return bottomRow ?? selectedRows[selectedRows.Count - 1];
    }

    private void CreateVolumeSlider(Transform column, GameObject templateRow)
    {
        var rowObject = UnityEngine.Object.Instantiate(templateRow, column, false);
        rowObject.name = VolumeRowObjectName;
        rowObject.SetActive(false);
        PositionAfter(rowObject, templateRow, column, 1);
        MatchLayoutElement(rowObject, templateRow);
        rowObject.transform.SetSiblingIndex(Mathf.Min(templateRow.transform.GetSiblingIndex() + 1, column.childCount - 1));
        ExpandContainerToFit(column, rowObject);

        var copiedAudioSlider = rowObject.GetComponentInChildren<AudioSlider>(true);
        if (copiedAudioSlider != null)
            DestroyImmediateSafe(copiedAudioSlider);

        RefreshVolumeRow(rowObject);

        var slider = rowObject.GetComponentInChildren<Slider>(true);
        if (slider == null)
        {
            LogMissingSliderOnce("Cloned AudioSlider object did not contain a Slider component.");
            UnityEngine.Object.Destroy(rowObject);
            return;
        }

        slider.gameObject.name = "Slider";
        slider.minValue = VoiceChatPreferences.MinVolumePercent;
        slider.maxValue = VoiceChatPreferences.MaxVolumePercent;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(VoiceChatPreferences.OutputVolumePercent);
        slider.onValueChanged.RemoveAllListeners();
        var valueLabel = FindSliderValueLabel(rowObject, slider);
        SetSliderValueLabels(rowObject, valueLabel, VoiceChatPreferences.OutputVolumePercent);
#if IL2CPPMELON
        slider.onValueChanged.AddListener(new Action<float>(value => OnVolumeChanged(value, valueLabel)));
#else
        slider.onValueChanged.AddListener(value => OnVolumeChanged(value, valueLabel));
#endif
        slider.interactable = true;
        rowObject.SetActive(true);
        RefreshVolumeRow(rowObject);
        RebuildLayout(column);

        _logger.Msg("S1 Voice Chat volume slider added to Audio settings.");
    }

    private void CreateOpenMicToggle(Transform column, GameObject anchorRow)
    {
        var rowObject = UnityEngine.Object.Instantiate(anchorRow, column, false);
        rowObject.name = ToggleRowObjectName;
        rowObject.SetActive(false);
        DestroyCopiedAudioSliders(rowObject);
        MatchRowSize(rowObject, anchorRow);
        PositionAfter(rowObject, anchorRow, column, 1);
        MatchLayoutElement(rowObject, anchorRow);
        rowObject.transform.SetSiblingIndex(Mathf.Min(anchorRow.transform.GetSiblingIndex() + 1, column.childCount - 1));
        ExpandContainerToFit(column, rowObject);

        RefreshOpenMicRow(rowObject);

        var copiedSlider = rowObject.GetComponentInChildren<Slider>(true);
        if (copiedSlider != null)
            copiedSlider.gameObject.SetActive(false);

        var toggle = CreateOpenMicToggleControl(rowObject.transform);
        toggle.SetIsOnWithoutNotify(VoiceChatPreferences.OpenMicEnabled);
        toggle.onValueChanged.RemoveAllListeners();
#if IL2CPPMELON
        toggle.onValueChanged.AddListener(new Action<bool>(OnOpenMicChanged));
#else
        toggle.onValueChanged.AddListener(OnOpenMicChanged);
#endif
        toggle.interactable = true;
        rowObject.SetActive(true);
        RefreshOpenMicRow(rowObject);
        RebuildLayout(column);

        _logger.Msg("S1 Voice Chat open mic toggle added to Audio settings.");
    }

    private static Toggle CreateOpenMicToggleControl(Transform row)
    {
        var toggleObject = new GameObject("Toggle");
        toggleObject.transform.SetParent(row, false);

        var toggleRect = toggleObject.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(1f, 0.5f);
        toggleRect.anchorMax = new Vector2(1f, 0.5f);
        toggleRect.pivot = new Vector2(0.5f, 0.5f);
        toggleRect.sizeDelta = new Vector2(28f, 28f);
        toggleRect.anchoredPosition = new Vector2(-95f, 0f);

        var toggle = toggleObject.AddComponent<Toggle>();

        var borderObject = new GameObject("Border");
        borderObject.transform.SetParent(toggleObject.transform, false);
        var borderRect = borderObject.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;
        var border = borderObject.AddComponent<Image>();
        border.color = new Color(0.74f, 0.78f, 0.82f, 0.95f);

        var backgroundObject = new GameObject("Background");
        backgroundObject.transform.SetParent(borderObject.transform, false);
        var backgroundRect = backgroundObject.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = new Vector2(3f, 3f);
        backgroundRect.offsetMax = new Vector2(-3f, -3f);
        var background = backgroundObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.09f, 0.10f, 0.96f);

        var checkmarkObject = new GameObject("Checkmark");
        checkmarkObject.transform.SetParent(backgroundObject.transform, false);
        var checkmarkRect = checkmarkObject.AddComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0.18f, 0.18f);
        checkmarkRect.anchorMax = new Vector2(0.82f, 0.82f);
        checkmarkRect.offsetMin = Vector2.zero;
        checkmarkRect.offsetMax = Vector2.zero;
        var checkmark = checkmarkObject.AddComponent<Image>();
        checkmark.color = new Color(0.30f, 0.95f, 0.22f, 1f);

        toggle.targetGraphic = border;
        toggle.graphic = checkmark;
        toggle.interactable = true;
        return toggle;
    }

    private static void PositionAfter(GameObject rowObject, GameObject anchorRow, Transform column, int rowsAfter)
    {
        var rowRect = rowObject.GetComponent<RectTransform>();
        var anchorRect = anchorRow.GetComponent<RectTransform>();
        if (rowRect == null || anchorRect == null)
            return;

        rowRect.anchorMin = anchorRect.anchorMin;
        rowRect.anchorMax = anchorRect.anchorMax;
        rowRect.pivot = anchorRect.pivot;
        rowRect.sizeDelta = anchorRect.sizeDelta;
        rowRect.localScale = anchorRect.localScale;

        var spacing = EstimateRowSpacing(anchorRect, column);
        rowRect.anchoredPosition = new Vector2(anchorRect.anchoredPosition.x, anchorRect.anchoredPosition.y - spacing * rowsAfter);
        rowRect.SetAsLastSibling();
    }

    private static void ExpandContainerToFit(Transform column, GameObject rowObject)
    {
        var rowRect = rowObject.GetComponent<RectTransform>();
        if (rowRect == null)
            return;

        var columnRect = column.GetComponent<RectTransform>();
        if (columnRect != null)
            ExpandColumnToFitRow(columnRect, rowRect);

        var container = column;
        while (container != null)
        {
            var containerRect = container.GetComponent<RectTransform>();
            if (containerRect != null)
                ExpandContainerToFit(containerRect, rowRect);

            if (container.GetComponent<ScrollRect>() != null)
                break;

            container = container.parent;
        }
    }

    private static void ExpandColumnToFitRow(RectTransform columnRect, RectTransform rowRect)
    {
        var rowBottom = Mathf.Abs(rowRect.anchoredPosition.y) + Mathf.Max(rowRect.sizeDelta.y, 50f) + 24f;
        if (rowBottom <= columnRect.sizeDelta.y)
            return;

        columnRect.sizeDelta = new Vector2(columnRect.sizeDelta.x, rowBottom);
    }

    private static void ExpandContainerToFit(RectTransform containerRect, RectTransform rowRect)
    {
        var rowY = GetAnchoredYRelativeTo(rowRect, containerRect);
        var requiredHeight = Mathf.Abs(rowY) + GetRectHeight(rowRect) + 24f;
        if (requiredHeight <= GetRectHeight(containerRect))
            return;

        containerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);
    }

    private static float GetAnchoredYRelativeTo(RectTransform rectTransform, RectTransform ancestor)
    {
        var y = 0f;
        var current = rectTransform;
        while (current != null)
        {
            y += current.anchoredPosition.y;
            if (current == ancestor || current.parent == null)
                break;

            current = current.parent.GetComponent<RectTransform>();
        }

        return y;
    }

    private static float GetRectHeight(RectTransform rectTransform)
    {
        return Mathf.Max(rectTransform.rect.height, rectTransform.sizeDelta.y);
    }

    private static void MatchRowSize(GameObject rowObject, GameObject anchorRow)
    {
        var rowRect = rowObject.GetComponent<RectTransform>();
        var anchorRect = anchorRow.GetComponent<RectTransform>();
        if (rowRect == null || anchorRect == null)
            return;

        rowRect.anchorMin = anchorRect.anchorMin;
        rowRect.anchorMax = anchorRect.anchorMax;
        rowRect.pivot = anchorRect.pivot;
        rowRect.sizeDelta = anchorRect.sizeDelta;
        rowRect.localScale = anchorRect.localScale;
    }

    private static void MatchLayoutElement(GameObject rowObject, GameObject anchorRow)
    {
        var layoutElement = rowObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = rowObject.AddComponent<LayoutElement>();

        var source = anchorRow.GetComponent<LayoutElement>();
        if (source != null)
        {
            layoutElement.ignoreLayout = source.ignoreLayout;
            layoutElement.minWidth = source.minWidth;
            layoutElement.minHeight = source.minHeight;
            layoutElement.preferredWidth = source.preferredWidth;
            layoutElement.preferredHeight = source.preferredHeight;
            layoutElement.flexibleWidth = source.flexibleWidth;
            layoutElement.flexibleHeight = source.flexibleHeight;
            layoutElement.layoutPriority = source.layoutPriority;
            return;
        }

        var anchorRect = anchorRow.GetComponent<RectTransform>();
        layoutElement.ignoreLayout = false;
        layoutElement.minHeight = anchorRect != null ? Mathf.Max(anchorRect.sizeDelta.y, 50f) : 50f;
        layoutElement.preferredHeight = layoutElement.minHeight;
        layoutElement.flexibleHeight = 0f;
    }

    private static void RebuildLayout(Transform column)
    {
        var rectTransform = column.GetComponent<RectTransform>();
        if (rectTransform == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        Canvas.ForceUpdateCanvases();
    }

    private static float EstimateRowSpacing(RectTransform anchorRect, Transform column)
    {
        var bestDelta = 0f;
        for (var i = 0; i < column.childCount; i++)
        {
            var sibling = column.GetChild(i).GetComponent<RectTransform>();
            if (sibling == null || sibling == anchorRect)
                continue;

            var delta = Mathf.Abs(anchorRect.anchoredPosition.y - sibling.anchoredPosition.y);
            if (delta > 1f && (bestDelta <= 0f || delta < bestDelta))
                bestDelta = delta;
        }

        return bestDelta > 0f ? bestDelta : Mathf.Max(anchorRect.sizeDelta.y + 20f, 65f);
    }

    private static void RemoveCopiedRowBehaviours(GameObject rowObject)
    {
        var behaviours = rowObject.GetComponents<MonoBehaviour>();
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            DestroyImmediateSafe(behaviour);
        }
    }

    private static void RefreshVolumeRow(GameObject rowObject)
    {
        DestroyCopiedAudioSliders(rowObject);

        SetPrimaryLabels(rowObject, "Voice Chat Volume");
        SetCanvasGroupsVisible(rowObject);

        var slider = rowObject.GetComponentInChildren<Slider>(true);
        if (slider == null)
            return;

        slider.SetValueWithoutNotify(VoiceChatPreferences.OutputVolumePercent);
        SetSliderValueLabels(rowObject, FindSliderValueLabel(rowObject, slider), VoiceChatPreferences.OutputVolumePercent);
    }

    private static void RefreshOpenMicRow(GameObject rowObject)
    {
        DestroyCopiedAudioSliders(rowObject);
        SetPrimaryLabels(rowObject, "Open Mic");
        SetCanvasGroupsVisible(rowObject);
    }

    private static void DestroyCopiedAudioSliders(GameObject rowObject)
    {
        var copiedAudioSliders = rowObject.GetComponentsInChildren<AudioSlider>(true);
        for (var i = 0; i < copiedAudioSliders.Length; i++)
            DestroyImmediateSafe(copiedAudioSliders[i]);
    }

    private static void DestroyImmediateSafe(UnityEngine.Object unityObject)
    {
        if (unityObject == null)
            return;

        UnityEngine.Object.DestroyImmediate(unityObject);
    }

    private static Transform? FindChildRecursive(Transform parent, string childName)
    {
        if (parent.name == childName)
            return parent;

        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            var match = FindChildRecursive(child, childName);
            if (match != null)
                return match;
        }

        return null;
    }

    private static void SetPrimaryLabels(GameObject rowObject, string labelText)
    {
        var labels = rowObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label == null)
                continue;

            if (!int.TryParse(label.text, out _))
                label.text = labelText;
        }
    }

    private static void SetCanvasGroupsVisible(GameObject rowObject)
    {
        var canvasGroups = rowObject.GetComponentsInChildren<CanvasGroup>(true);
        for (var i = 0; i < canvasGroups.Length; i++)
        {
            var canvasGroup = canvasGroups[i];
            if (canvasGroup == null)
                continue;

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private static TextMeshProUGUI? FindSliderValueLabel(GameObject rowObject, Slider slider)
    {
        if (slider.handleRect == null)
            return FindNumericLabel(rowObject);

        var valueTransform = slider.handleRect.Find("Value");
        var valueLabel = valueTransform != null ? valueTransform.GetComponent<TextMeshProUGUI>() : null;
        return valueLabel != null ? valueLabel : FindNumericLabel(rowObject);
    }

    private static TextMeshProUGUI? FindNumericLabel(GameObject rowObject)
    {
        var labels = rowObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        TextMeshProUGUI? best = null;
        var bestX = float.MinValue;

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label == null)
                continue;

            if (!int.TryParse(label.text, out _))
                continue;

            var rect = label.GetComponent<RectTransform>();
            var x = rect != null ? rect.position.x : label.transform.position.x;
            if (x > bestX)
            {
                bestX = x;
                best = label;
            }
        }

        return best;
    }

    private static void SetSliderValueLabels(GameObject rowObject, TextMeshProUGUI? valueLabel, float value)
    {
        var text = Mathf.RoundToInt(value).ToString();
        var labels = rowObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label == null || !int.TryParse(label.text, out _))
                continue;

            label.text = text;
            label.enabled = true;
        }

        if (valueLabel != null)
        {
            valueLabel.text = text;
            valueLabel.enabled = true;
        }
    }

    private static void OnVolumeChanged(float value, TextMeshProUGUI? valueLabel)
    {
        VoiceChatPreferences.SetOutputVolumePercent(value);
        if (valueLabel != null)
        {
            valueLabel.text = Mathf.RoundToInt(value).ToString();
            valueLabel.enabled = true;
        }
    }

    private static void OnOpenMicChanged(bool value)
    {
        VoiceChatPreferences.SetOpenMicEnabled(value);
    }

    private void LogMissingScreenOnce()
    {
        if (_loggedMissingScreen)
            return;

        _loggedMissingScreen = true;
        if (VoiceChatPreferences.DiagnosticLoggingEnabled)
            _logger.Msg("Settings screen not available yet; S1 Voice Chat settings UI will retry.");
    }

    private void LogMissingSliderOnce(string message)
    {
        if (_loggedMissingSlider)
            return;

        _loggedMissingSlider = true;
        _logger.Warning($"{message} S1 Voice Chat settings UI will retry.");
    }

}
#endif
