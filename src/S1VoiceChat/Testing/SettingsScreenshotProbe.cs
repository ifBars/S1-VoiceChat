#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

#if IL2CPPMELON
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppScheduleOne.UI.Settings;
#elif MONOMELON
using ScheduleOne.UI.MainMenu;
using ScheduleOne.UI.Settings;
#endif

namespace S1VoiceChat.Testing;

internal sealed class SettingsScreenshotProbe
{
    private readonly MelonLogger.Instance _logger;
    private readonly string _screenshotPath;
    private readonly bool _quitAfterCapture;
    private float _captureAtRealtime;
    private float _quitAtRealtime;
    private bool _opened;
    private bool _scrolled;
    private bool _captured;
    private bool _hiddenDisclaimer;

    private SettingsScreenshotProbe(MelonLogger.Instance logger, string screenshotPath, bool quitAfterCapture)
    {
        _logger = logger;
        _screenshotPath = screenshotPath;
        _quitAfterCapture = quitAfterCapture;
    }

    public static SettingsScreenshotProbe? TryCreate(MelonLogger.Instance logger)
    {
        var args = Environment.GetCommandLineArgs();
        if (!HasFlag(args, "--s1vc-settings-screenshot"))
            return null;

        var screenshotPath = GetArgValue(args, "--s1vc-settings-screenshot-path");
        if (string.IsNullOrWhiteSpace(screenshotPath))
        {
            screenshotPath = Path.Combine(
                AppContext.BaseDirectory,
                "UserData",
                "S1VoiceChat",
                $"settings-audio-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
        }

        return new SettingsScreenshotProbe(
            logger,
            screenshotPath,
            HasFlag(args, "--s1vc-quit-after-screenshot"));
    }

    public void Update()
    {
        if (_captured)
        {
            if (_quitAfterCapture && Time.realtimeSinceStartup >= _quitAtRealtime)
                Application.Quit();

            return;
        }

        var screen = UnityEngine.Object.FindObjectOfType<SettingsScreen>(true);
        if (screen == null)
            return;

        HideDisclaimerOverlay();

        if (!_opened)
        {
            OpenAudioSettings(screen);
            _captureAtRealtime = Time.realtimeSinceStartup + 1.25f;
            _opened = true;
            return;
        }

        if (Time.realtimeSinceStartup < _captureAtRealtime)
        {
            if (_scrolled)
            {
                ShowAudioCategory(screen);
                ScrollToBottom(screen);
            }

            return;
        }

        if (!_scrolled)
        {
            ShowAudioCategory(screen);
            ScrollToBottom(screen);
            _captureAtRealtime = Time.realtimeSinceStartup + 0.75f;
            _scrolled = true;
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_screenshotPath)!);
        ScreenCapture.CaptureScreenshot(_screenshotPath);
        _logger.Msg($"Settings screenshot captured: {_screenshotPath}");
        _captured = true;
        _quitAtRealtime = Time.realtimeSinceStartup + 2f;
    }

    private static void OpenAudioSettings(SettingsScreen screen)
    {
        screen.Open(closePrevious: true);
        ShowAudioCategory(screen);
    }

    private static void ShowAudioCategory(SettingsScreen screen)
    {
        var audioIndex = FindAudioCategoryIndex(screen);
        if (audioIndex >= 0)
            screen.ShowCategory(audioIndex);
    }

    private static int FindAudioCategoryIndex(SettingsScreen screen)
    {
        if (screen.Categories == null)
            return -1;

        for (var i = 0; i < screen.Categories.Length; i++)
        {
            var category = screen.Categories[i];
            if (category?.Panel != null && category.Panel.GetComponentInChildren<AudioSlider>(true) != null)
                return i;
        }

        return -1;
    }

    private static void ScrollToBottom(SettingsScreen screen)
    {
        Canvas.ForceUpdateCanvases();

        var scrollRects = screen.GetComponentsInChildren<ScrollRect>(true);
        for (var i = 0; i < scrollRects.Length; i++)
        {
            var scrollRect = scrollRects[i];
            if (scrollRect == null || !scrollRect.gameObject.activeInHierarchy || !scrollRect.vertical)
                continue;

            scrollRect.verticalNormalizedPosition = 0f;
        }

        Canvas.ForceUpdateCanvases();
    }

    private void HideDisclaimerOverlay()
    {
        if (_hiddenDisclaimer)
            return;

        _hiddenDisclaimer = true;
        var objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (var i = 0; i < objects.Length; i++)
        {
            var gameObject = objects[i];
            if (gameObject != null && gameObject.name == "Disclaimer")
                gameObject.SetActive(false);
        }
    }

    private static bool HasFlag(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
#endif
