#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using MelonLoader;
using UnityEngine;

namespace S1VoiceChat.Runtime;

internal sealed class PttHudOverlay : IDisposable
{
    private const float Width = 152f;
    private const float Height = 44f;
    private const float MarginLeft = 18f;
    private const float MarginBottom = 116f;
    private const float IconSize = 22f;

    private readonly MelonLogger.Instance _logger;
    private readonly KeyCode _pushToTalkKey;
    private Texture2D? _microphoneIcon;
    private Texture2D? _muteIcon;
    private GUIStyle? _labelStyle;
    private GUIStyle? _keyStyle;
    private GUIStyle? _stateStyle;
    private bool _loadAttempted;
    private bool _disposed;

    public PttHudOverlay(MelonLogger.Instance logger, KeyCode pushToTalkKey)
    {
        _logger = logger;
        _pushToTalkKey = pushToTalkKey;
    }

    public void Draw(bool transmitting, bool openMic)
    {
        if (_disposed || Event.current?.type != EventType.Repaint)
            return;

        EnsureLoaded();
        EnsureStyles();

        var alpha = transmitting ? 0.96f : 0.72f;
        var previousColor = GUI.color;
        var panelRect = new Rect(MarginLeft, Screen.height - MarginBottom, Width, Height);
        var iconRect = new Rect(panelRect.x + 11f, panelRect.y + 11f, IconSize, IconSize);
        var keyRect = new Rect(panelRect.x + 42f, panelRect.y + 7f, 36f, 30f);
        var labelRect = new Rect(panelRect.x + 84f, panelRect.y + 6f, 62f, 15f);
        var stateRect = new Rect(panelRect.x + 84f, panelRect.y + 22f, 62f, 15f);

        GUI.color = new Color(0.05f, 0.06f, 0.07f, 0.58f * alpha);
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture, ScaleMode.StretchToFill);

        GUI.color = transmitting ? new Color(0.34f, 0.86f, 0.54f, alpha) : new Color(0.9f, 0.94f, 1f, 0.54f * alpha);
        GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, 3f, panelRect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill);

        var icon = transmitting ? _microphoneIcon : _muteIcon;
        GUI.color = Color.white;
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, alphaBlend: true);

        GUI.Label(keyRect, openMic ? "ON" : _pushToTalkKey.ToString(), _keyStyle);
        GUI.Label(labelRect, "VOICE", _labelStyle);
        GUI.Label(stateRect, transmitting ? "LIVE" : "READY", _stateStyle);

        GUI.color = previousColor;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DestroyTexture(_microphoneIcon);
        DestroyTexture(_muteIcon);
        _microphoneIcon = null;
        _muteIcon = null;
        _disposed = true;
    }

    private void EnsureStyles()
    {
        if (_labelStyle != null)
            return;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 0)
        };
        _labelStyle.normal.textColor = new Color(0.88f, 0.92f, 0.98f, 0.76f);

        _stateStyle = new GUIStyle(_labelStyle)
        {
            fontSize = 11
        };
        _stateStyle.normal.textColor = new Color(0.99f, 1f, 1f, 0.94f);

        _keyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            padding = new RectOffset(0, 0, 0, 1)
        };
        _keyStyle.normal.textColor = Color.white;
    }

    private void EnsureLoaded()
    {
        if (_loadAttempted)
            return;

        _loadAttempted = true;
        _microphoneIcon = LoadIcon("microphone.png");
        _muteIcon = LoadIcon("mute.png");
    }

    private Texture2D? LoadIcon(string fileName)
    {
        try
        {
            var bytes = EmbeddedHudIconAssets.LoadIconBytes(fileName, _logger);
            if (bytes == null || bytes.Length == 0)
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            if (ImageConversion.LoadImage(texture, bytes, markNonReadable: false))
                return texture;

            DestroyTexture(texture);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to load embedded PTT HUD icon '{fileName}': {ex.Message}");
        }

        _logger.Warning($"Embedded PTT HUD icon not found: {fileName}");
        return null;
    }

    private static void DestroyTexture(Texture2D? texture)
    {
        if (texture != null)
            UnityEngine.Object.Destroy(texture);
    }
}
#endif
