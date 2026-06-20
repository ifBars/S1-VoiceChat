#if S1VOICECHAT_STEAMNETWORKLIB
using System;
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
    private const string SliderObjectName = "S1VoiceChatVolumeSlider";
    private const string ToggleObjectName = "S1VoiceChatOpenMicToggle";
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

        if (audioPanel.transform.Find(SliderObjectName) == null)
        {
            var sourceSlider = audioPanel.GetComponentInChildren<AudioSlider>(true)
                ?? UnityEngine.Object.FindObjectOfType<AudioSlider>(true);
            if (sourceSlider == null)
            {
                LogMissingSliderOnce("Could not find AudioSlider to clone.");
                return;
            }

            CreateVolumeSlider(audioPanel, sourceSlider);
        }

        if (audioPanel.transform.Find(ToggleObjectName) == null)
        {
            var sourceToggle = FindSourceToggle(settingsScreen);
            if (sourceToggle == null)
            {
                LogMissingSliderOnce("Could not find Toggle to clone.");
                return;
            }

            CreateOpenMicToggle(audioPanel, sourceToggle);
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

    private static Toggle? FindSourceToggle(SettingsScreen settingsScreen)
    {
        if (settingsScreen.Categories == null)
            return UnityEngine.Object.FindObjectOfType<Toggle>(true);

        for (var i = 0; i < settingsScreen.Categories.Length; i++)
        {
            var category = settingsScreen.Categories[i];
            if (category == null || category.Panel == null)
                continue;

            var toggle = category.Panel.GetComponentInChildren<Toggle>(true);
            if (toggle != null)
                return toggle;
        }

        return UnityEngine.Object.FindObjectOfType<Toggle>(true);
    }

    private void CreateVolumeSlider(GameObject audioPanel, AudioSlider sourceSlider)
    {
        var sliderObject = UnityEngine.Object.Instantiate(sourceSlider.gameObject, audioPanel.transform);
        sliderObject.name = SliderObjectName;
        sliderObject.SetActive(true);

        var rectTransform = sliderObject.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.localScale = sourceSlider.transform.localScale;
            rectTransform.SetAsLastSibling();
        }

        var copiedAudioSlider = sliderObject.GetComponent<AudioSlider>();
        if (copiedAudioSlider != null)
            UnityEngine.Object.Destroy(copiedAudioSlider);

        SetLabel(sliderObject, "S1 Voice Chat");

        var slider = sliderObject.GetComponent<Slider>();
        if (slider == null)
        {
            LogMissingSliderOnce("Cloned AudioSlider object did not contain a Slider component.");
            UnityEngine.Object.Destroy(sliderObject);
            return;
        }

        slider.minValue = VoiceChatPreferences.MinVolumePercent;
        slider.maxValue = VoiceChatPreferences.MaxVolumePercent;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(VoiceChatPreferences.OutputVolumePercent);
        slider.onValueChanged.RemoveAllListeners();
        var valueLabel = FindSliderValueLabel(slider);
        SetSliderValueLabel(valueLabel, VoiceChatPreferences.OutputVolumePercent);
#if IL2CPPMELON
        slider.onValueChanged.AddListener(new Action<float>(value => OnVolumeChanged(value, valueLabel)));
#else
        slider.onValueChanged.AddListener(value => OnVolumeChanged(value, valueLabel));
#endif
        slider.interactable = true;

        _logger.Msg("S1 Voice Chat volume slider added to Audio settings.");
    }

    private void CreateOpenMicToggle(GameObject audioPanel, Toggle sourceToggle)
    {
        var toggleObject = UnityEngine.Object.Instantiate(sourceToggle.gameObject, audioPanel.transform);
        toggleObject.name = ToggleObjectName;
        toggleObject.SetActive(true);

        var rectTransform = toggleObject.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.localScale = sourceToggle.transform.localScale;
            rectTransform.SetAsLastSibling();
        }

        var copiedSettingsToggle = toggleObject.GetComponent<SettingsToggle>();
        if (copiedSettingsToggle != null)
            UnityEngine.Object.Destroy(copiedSettingsToggle);

        SetLabel(toggleObject, "Open Mic");

        var toggle = toggleObject.GetComponent<Toggle>();
        if (toggle == null)
        {
            LogMissingSliderOnce("Cloned settings toggle object did not contain a Toggle component.");
            UnityEngine.Object.Destroy(toggleObject);
            return;
        }

        toggle.SetIsOnWithoutNotify(VoiceChatPreferences.OpenMicEnabled);
        toggle.onValueChanged.RemoveAllListeners();
#if IL2CPPMELON
        toggle.onValueChanged.AddListener(new Action<bool>(OnOpenMicChanged));
#else
        toggle.onValueChanged.AddListener(OnOpenMicChanged);
#endif
        toggle.interactable = true;

        _logger.Msg("S1 Voice Chat open mic toggle added to Audio settings.");
    }

    private static void SetLabel(GameObject sliderObject, string labelText)
    {
        var labelTransform = sliderObject.transform.Find("Label");
        var label = labelTransform != null
            ? labelTransform.GetComponent<TextMeshProUGUI>()
            : sliderObject.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label != null)
            label.text = labelText;
    }

    private static TextMeshProUGUI? FindSliderValueLabel(Slider slider)
    {
        if (slider.handleRect == null)
            return null;

        var valueTransform = slider.handleRect.Find("Value");
        return valueTransform != null ? valueTransform.GetComponent<TextMeshProUGUI>() : null;
    }

    private static void SetSliderValueLabel(TextMeshProUGUI? valueLabel, float value)
    {
        if (valueLabel == null)
            return;

        valueLabel.text = Mathf.RoundToInt(value).ToString();
        valueLabel.enabled = true;
    }

    private static void OnVolumeChanged(float value, TextMeshProUGUI? valueLabel)
    {
        VoiceChatPreferences.SetOutputVolumePercent(value);
        SetSliderValueLabel(valueLabel, value);
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
