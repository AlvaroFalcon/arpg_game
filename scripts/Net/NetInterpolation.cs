#nullable enable
using System;
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// A few timestamped samples of one entity's position, sampled at a render
/// time: interpolated between the two around it, held (with a short
/// extrapolation) past the newest when a sample is late. The hideout's
/// presence uses it (docs/design/multiplayer.md -> "Shared hideout"); it is the
/// same rule as the Rift client's tracks.
/// </summary>
public sealed class NetTrack
{
    private readonly double[] _times = new double[4];
    private readonly Vector2[] _positions = new Vector2[4];
    private int _count;

    public NetTrack(Vector2 at)
    {
        _positions[0] = at;
        _times[0] = double.MinValue;
        _count = 1;
    }

    public Vector2 Latest => _positions[_count - 1];

    public void Push(double time, Vector2 at)
    {
        if (_count > 0 && time <= _times[_count - 1])
        {
            return;
        }

        if (_count == _times.Length)
        {
            Array.Copy(_times, 1, _times, 0, _count - 1);
            Array.Copy(_positions, 1, _positions, 0, _count - 1);
            _count--;
        }

        _times[_count] = time;
        _positions[_count] = at;
        _count++;
    }

    public Vector2 Sample(double time)
    {
        if (_count == 1 || time <= _times[0])
        {
            return _positions[0];
        }

        for (int i = 1; i < _count; i++)
        {
            if (time <= _times[i])
            {
                double span = _times[i] - _times[i - 1];
                float t = span <= 0 ? 1f : (float)((time - _times[i - 1]) / span);
                return _positions[i - 1].Lerp(_positions[i], t);
            }
        }

        // Past the newest sample: extrapolate briefly, then hold.
        int last = _count - 1;
        double gap = _times[last] - _times[last - 1];
        if (gap <= 0 || gap > 0.5)
        {
            return _positions[last];
        }

        float ahead = (float)Math.Min((time - _times[last]) / gap, 0.5);
        return _positions[last] + (_positions[last] - _positions[last - 1]) * ahead;
    }
}
