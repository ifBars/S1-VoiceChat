#if S1VOICECHAT_STEAMNETWORKLIB
using System;
using System.Collections.Generic;
using S1VoiceChat.Routing;
using S1VoiceChat.Runtime;
using UnityEngine;

namespace S1VoiceChat.Playback;

public sealed class UnityVoicePlaybackSink : IDisposable
{
    private const float RadioLowPassCutoffHz = 3500f;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly short[] _pcmBuffer;
    private readonly float[] _floatBuffer;
    private readonly VoiceSettings _settings;
    private readonly Dictionary<ulong, SpeakerPlaybackSource> _sources = new();
    private readonly Dictionary<ulong, Vector3> _positions = new();
    private GameObject? _gameObject;
    private bool _disposed;

    public UnityVoicePlaybackSink(int sampleRate, int channels, int maxFrameSamples)
        : this(sampleRate, channels, maxFrameSamples, new VoiceSettings())
    {
    }

    public UnityVoicePlaybackSink(int sampleRate, int channels, int maxFrameSamples, VoiceSettings settings)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _pcmBuffer = new short[maxFrameSamples * channels];
        _floatBuffer = new float[_pcmBuffer.Length];
        _settings = settings;
    }

    public void Update(VoiceSession session)
    {
        if (_disposed)
            return;

        EnsureRootObject();

        foreach (var stream in session.RemoteStreams.Values)
            PlayAvailableFrames(stream);
    }

    public void PlayPcm(ulong peerId, VoiceChannel channel, Vector3? position, ReadOnlySpan<short> pcm)
    {
        if (_disposed || pcm.IsEmpty)
            return;

        EnsureRootObject();
        PlayFrame(peerId, channel, position, pcm);
    }

    public void UpdateSpeakerPositions(IReadOnlyDictionary<ulong, Vector3> positions)
    {
        if (_disposed || positions.Count == 0)
            return;

        foreach (var position in positions)
        {
            _positions[position.Key] = position.Value;
            if (_sources.TryGetValue(position.Key, out var source))
                source.UpdatePosition(position.Value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_gameObject != null)
            UnityEngine.Object.Destroy(_gameObject);

        _sources.Clear();
        _gameObject = null;
        _disposed = true;
    }

    private void EnsureRootObject()
    {
        if (_gameObject != null)
            return;

        _gameObject = new GameObject("S1VoiceChat Playback");
        UnityEngine.Object.DontDestroyOnLoad(_gameObject);
    }

    private void PlayAvailableFrames(RemoteVoiceStream stream)
    {
        if (_gameObject == null)
            return;

        var read = stream.ReadPcm(_pcmBuffer);
        if (read <= 0)
            return;

        _positions.TryGetValue(stream.PeerId, out var position);
        var hasPosition = _positions.ContainsKey(stream.PeerId);
        PlayFrame(stream.PeerId, stream.LastChannel, hasPosition ? position : null, _pcmBuffer.AsSpan(0, read));
    }

    private void PlayFrame(ulong peerId, VoiceChannel channel, Vector3? position, ReadOnlySpan<short> pcm)
    {
        if (_gameObject == null)
            return;

        var sampleCount = Math.Min(pcm.Length, _floatBuffer.Length);
        var volume = Mathf.Clamp01(_settings.OutputVolume);
        for (var i = 0; i < sampleCount; i++)
            _floatBuffer[i] = pcm[i] / 32768f * volume;

        var clip = AudioClip.Create($"S1VoiceChat Remote {peerId}", sampleCount / Math.Max(_channels, 1), _channels, _sampleRate, stream: false);
        clip.SetData(_floatBuffer.AsSpan(0, sampleCount).ToArray(), 0);

        var source = GetOrCreateSource(peerId);
        source.Configure(channel, position, GetMaxDistance(channel));
        source.AudioSource.PlayOneShot(clip);
        UnityEngine.Object.Destroy(clip, clip.length + 0.25f);
    }

    private float GetMaxDistance(VoiceChannel channel)
    {
        return channel switch
        {
            VoiceChannel.Whisper => Math.Max(1f, _settings.WhisperRangeMeters),
            VoiceChannel.Proximity => Math.Max(1f, _settings.ProximityRangeMeters),
            VoiceChannel.Shout => Math.Max(1f, _settings.ShoutRangeMeters),
            _ => Math.Max(1f, _settings.ShoutRangeMeters)
        };
    }

    private SpeakerPlaybackSource GetOrCreateSource(ulong peerId)
    {
        if (_sources.TryGetValue(peerId, out var source))
            return source;

        var sourceObject = new GameObject($"S1VoiceChat Speaker {peerId}");
        sourceObject.transform.SetParent(_gameObject!.transform, worldPositionStays: false);
        var audioSource = sourceObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = Math.Max(1f, _settings.ShoutRangeMeters);
        var radioFilter = sourceObject.AddComponent<AudioLowPassFilter>();
        radioFilter.cutoffFrequency = RadioLowPassCutoffHz;
        radioFilter.enabled = false;

        source = new SpeakerPlaybackSource(sourceObject, audioSource, radioFilter);
        _sources[peerId] = source;
        return source;
    }

    private sealed class SpeakerPlaybackSource
    {
        public SpeakerPlaybackSource(GameObject gameObject, AudioSource audioSource, AudioLowPassFilter radioFilter)
        {
            GameObject = gameObject;
            AudioSource = audioSource;
            RadioFilter = radioFilter;
        }

        public GameObject GameObject { get; }

        public AudioSource AudioSource { get; }

        public AudioLowPassFilter RadioFilter { get; }

        public void Configure(VoiceChannel channel, Vector3? position, float maxDistance)
        {
            if (position.HasValue)
                UpdatePosition(position.Value);

            var spatial = position.HasValue && channel != VoiceChannel.Global && channel != VoiceChannel.Radio;
            AudioSource.spatialBlend = spatial ? 1f : 0f;
            AudioSource.maxDistance = maxDistance;
            RadioFilter.enabled = channel == VoiceChannel.Radio;
        }

        public void UpdatePosition(Vector3 position)
        {
            GameObject.transform.position = position;
        }
    }
}
#endif
