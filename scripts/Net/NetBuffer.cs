#nullable enable
using System;
using System.Text;
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// A growable little-endian byte writer for snapshots and event batches —
/// docs/design/multiplayer.md -> "Wire format". Positions are quantised to half
/// a pixel in 16 bits (±16 383 px, far more than the Rift arena), life to a
/// byte or 16 bits, angles to 16 bits. Reused per peer, never reallocated in
/// steady state.
/// </summary>
public sealed class NetWriter
{
    private byte[] _buffer;
    private int _length;

    public NetWriter(int capacity = 256) => _buffer = new byte[capacity];

    public int Length => _length;

    public void Clear() => _length = 0;

    public byte[] ToArray()
    {
        var copy = new byte[_length];
        Buffer.BlockCopy(_buffer, 0, copy, 0, _length);
        return copy;
    }

    private void Ensure(int extra)
    {
        if (_length + extra <= _buffer.Length)
        {
            return;
        }

        int size = _buffer.Length * 2;
        while (size < _length + extra)
        {
            size *= 2;
        }

        Array.Resize(ref _buffer, size);
    }

    public void U8(byte v)
    {
        Ensure(1);
        _buffer[_length++] = v;
    }

    public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);

    public void U16(ushort v)
    {
        Ensure(2);
        _buffer[_length++] = (byte)v;
        _buffer[_length++] = (byte)(v >> 8);
    }

    public void I16(short v) => U16(unchecked((ushort)v));

    /// <summary>Overwrites two bytes already written (a count known only later).</summary>
    public void PatchU16(int at, ushort v)
    {
        _buffer[at] = (byte)v;
        _buffer[at + 1] = (byte)(v >> 8);
    }

    public void U32(uint v)
    {
        Ensure(4);
        _buffer[_length++] = (byte)v;
        _buffer[_length++] = (byte)(v >> 8);
        _buffer[_length++] = (byte)(v >> 16);
        _buffer[_length++] = (byte)(v >> 24);
    }

    public void I32(int v) => U32(unchecked((uint)v));

    public void F32(float v) => U32(BitConverter.SingleToUInt32Bits(v));

    /// <summary>A UTF-8 string with a 16-bit length. Longer than 65 535 bytes, it is
    /// cut at the last whole character that fits (the old version wrote the full
    /// string after a clamped length, overrunning the buffer's bookkeeping).</summary>
    public void Str(string s)
    {
        int count = Encoding.UTF8.GetByteCount(s);
        if (count <= ushort.MaxValue)
        {
            U16((ushort)count);
            Ensure(count);
            Encoding.UTF8.GetBytes(s, 0, s.Length, _buffer, _length);
            _length += count;
            return;
        }

        byte[] all = Encoding.UTF8.GetBytes(s);
        int cut = ushort.MaxValue;
        while (cut > 0 && (all[cut] & 0xC0) == 0x80)
        {
            cut--; // all[cut] continues a character: cut before that character starts
        }

        U16((ushort)cut);
        Bytes(all.AsSpan(0, cut));
    }

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    /// <summary>A world position in half pixels, 16 bits per axis.</summary>
    public void Pos(Vector2 p)
    {
        I16(Quantise(p.X));
        I16(Quantise(p.Y));
    }

    /// <summary>An angle in 16 bits (0.0055° steps).</summary>
    public void Angle(float radians) => U16((ushort)Mathf.RoundToInt(Mathf.PosMod(radians, Mathf.Tau) / Mathf.Tau * 65535f));

    public void Colour(Color c)
    {
        U8((byte)Mathf.RoundToInt(Mathf.Clamp(c.R, 0f, 1f) * 255f));
        U8((byte)Mathf.RoundToInt(Mathf.Clamp(c.G, 0f, 1f) * 255f));
        U8((byte)Mathf.RoundToInt(Mathf.Clamp(c.B, 0f, 1f) * 255f));
        U8((byte)Mathf.RoundToInt(Mathf.Clamp(c.A, 0f, 1f) * 255f));
    }

    /// <summary>A unit-ish vector (move input) as two signed bytes.</summary>
    public void Dir8(Vector2 v)
    {
        U8(unchecked((byte)(sbyte)Mathf.Clamp(Mathf.RoundToInt(v.X * 127f), -127, 127)));
        U8(unchecked((byte)(sbyte)Mathf.Clamp(Mathf.RoundToInt(v.Y * 127f), -127, 127)));
    }

    private static short Quantise(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v * 2f), short.MinValue, short.MaxValue);
}

/// <summary>Reads what <see cref="NetWriter"/> wrote. Reading past the end
/// returns zeros instead of throwing (a malformed packet is just ignored).</summary>
public sealed class NetReader
{
    private readonly byte[] _data;
    private int _pos;

    public NetReader(byte[] data) => _data = data;

    public bool More => _pos < _data.Length;

    public int Remaining => _data.Length - _pos;

    public byte U8() => _pos < _data.Length ? _data[_pos++] : (byte)0;

    public bool Bool() => U8() != 0;

    public ushort U16() => (ushort)(U8() | (U8() << 8));

    public short I16() => unchecked((short)U16());

    public uint U32() => (uint)(U8() | (U8() << 8) | (U8() << 16) | (U8() << 24));

    public int I32() => unchecked((int)U32());

    public float F32() => BitConverter.UInt32BitsToSingle(U32());

    public string Str()
    {
        int count = U16();
        if (count > Remaining)
        {
            _pos = _data.Length;
            return "";
        }

        string s = Encoding.UTF8.GetString(_data, _pos, count);
        _pos += count;
        return s;
    }

    public ReadOnlySpan<byte> Bytes(int count)
    {
        count = Math.Min(count, Remaining);
        var span = new ReadOnlySpan<byte>(_data, _pos, count);
        _pos += count;
        return span;
    }

    public Vector2 Pos() => new(I16() / 2f, I16() / 2f);

    public float Angle() => U16() / 65535f * Mathf.Tau;

    public Color Colour() => new(U8() / 255f, U8() / 255f, U8() / 255f, U8() / 255f);

    public Vector2 Dir8() => new(unchecked((sbyte)U8()) / 127f, unchecked((sbyte)U8()) / 127f);
}
