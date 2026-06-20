using System;

namespace S1VoiceChat.Playback;

public sealed class AudioRingBuffer
{
    private readonly short[] _buffer;
    private readonly object _lock = new();
    private int _readIndex;
    private int _writeIndex;
    private int _count;

    public AudioRingBuffer(int capacitySamples)
    {
        if (capacitySamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacitySamples));

        _buffer = new short[capacitySamples];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_lock)
                return _count;
        }
    }

    public int Write(ReadOnlySpan<short> samples)
    {
        lock (_lock)
        {
            var written = 0;
            foreach (var sample in samples)
            {
                if (_count == _buffer.Length)
                {
                    _readIndex = (_readIndex + 1) % _buffer.Length;
                    _count--;
                }

                _buffer[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % _buffer.Length;
                _count++;
                written++;
            }

            return written;
        }
    }

    public int Read(Span<short> destination)
    {
        lock (_lock)
        {
            var read = 0;
            while (read < destination.Length && _count > 0)
            {
                destination[read++] = _buffer[_readIndex];
                _readIndex = (_readIndex + 1) % _buffer.Length;
                _count--;
            }

            destination.Slice(read).Clear();
            return read;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _readIndex = 0;
            _writeIndex = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }
    }
}
