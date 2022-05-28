#nullable enable
// #define USE_SPANS
using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;

namespace HandCranked;


public
#if USE_SPANS
    ref
#endif
    struct Writer
#if !USE_SPANS
    : IDisposable
#endif
{
#if USE_SPANS
    private Span<byte> _buffer;
#else
    private byte[] _buffer;
#endif

    int _index, _end;
#if !USE_SPANS
    int _start;
#endif
    private long _positionBase;
    private object _state;

    public Writer(IBufferWriter<byte> target)
    {
        _state = target;
        _positionBase = 0;

        const int BUFFER_SIZE = 300000;
#if USE_SPANS
        _buffer = target.GetSpan(BUFFER_SIZE);
        _index = 0;
        _end = _buffer.Length;
#else
        var memory = target.GetMemory(BUFFER_SIZE);
        if (MemoryMarshal.TryGetArray<byte>(memory, out var segment))
        {
            _buffer = segment.Array!;
            _start = _index = segment.Offset;
            _end = segment.Offset + segment.Count;
        }
        else
        {
            throw new InvalidOperationException();
        }
#endif
    }

    private void Flush()
    {
        if (_state is IBufferWriter<byte> bw)
        {
#if USE_SPANS
            bw.Advance(_index);
            _end = _index = 0;
#else
            bw.Advance(_index - _start);
            _end = _index = _start = 0;
#endif

        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint MeasureVarint32(uint value)
    {
        if (Lzcnt.IsSupported)
        {
            var bits = 32 - Lzcnt.LeadingZeroCount(value);
            return bits == 0 ? 1 : ((bits + 6) / 7);
        }
        else
        {
            if ((value & (~0U << 7)) == 0) return 1;
            if ((value & (~0U << 14)) == 0) return 2;
            if ((value & (~0U << 21)) == 0) return 3;
            if ((value & (~0U << 28)) == 0) return 4;
            return 5;
        }
    }

    public void Dispose() => Flush();

    internal static uint MeasureVarint64(ulong value)
    {
        if (Lzcnt.X64.IsSupported)
        {
            var bits = 64 - (uint)Lzcnt.X64.LeadingZeroCount(value);
            return bits == 0 ? 1 : ((bits + 6) / 7);
        }
        else
        {
            if ((value & (~0UL << 7)) == 0) return 1;
            if ((value & (~0UL << 14)) == 0) return 2;
            if ((value & (~0UL << 21)) == 0) return 3;
            if ((value & (~0UL << 28)) == 0) return 4;
            if ((value & (~0UL << 35)) == 0) return 5;
            if ((value & (~0UL << 42)) == 0) return 6;
            if ((value & (~0UL << 49)) == 0) return 7;
            if ((value & (~0UL << 56)) == 0) return 8;
            if ((value & (~0UL << 63)) == 0) return 9;
            return 10;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong MeasureWithLengthPrefix(ulong bytes) => MeasureVarint64(bytes) + bytes;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong MeasureWithLengthPrefix(uint bytes) => MeasureVarint64(bytes) + (ulong)bytes;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong MeasureWithLengthPrefix(ReadOnlyMemory<char> value)
        => MeasureWithLengthPrefix((uint)Reader.UTF8.GetByteCount(value.Span));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong MeasureWithLengthPrefix(ReadOnlyMemory<byte> value)
        => MeasureWithLengthPrefix((uint)value.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteTag(uint value) => WriteVarintUInt32(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteTag(byte value)
    {
        if (_index < _end & (value & 0x80) == 0)
        {
            _buffer[_index++] = value;
        }
        else
        {
            WriteVarintUInt32(value);
        }
    }

    private void WriteVarintUInt32(uint value)
    {
        if (_index + 5 <= _end)
        {
            if (Lzcnt.IsSupported)
            {
                var bits = 32 - Lzcnt.LeadingZeroCount(value);
                const uint HI_BIT = 0b10000000;

                switch ((bits + 6) / 7)
                {
                    case 0:
                    case 1:
                        Debug.Assert(MeasureVarint32(value) == 1);
                        _buffer[_index++] = (byte)value;
                        return;
                    case 2:
                        Debug.Assert(MeasureVarint32(value) == 2);
                        _buffer[_index++] = (byte)(value | HI_BIT);
                        _buffer[_index++] = (byte)(value >> 7);
                        return;
                    case 3:
                        Debug.Assert(MeasureVarint32(value) == 3);
                        _buffer[_index++] = (byte)(value | HI_BIT);
                        _buffer[_index++] = (byte)((value >> 7) | HI_BIT);
                        _buffer[_index++] = (byte)(value >> 14);
                        return;
                    case 4:
                        Debug.Assert(MeasureVarint32(value) == 4);
                        _buffer[_index++] = (byte)(value | HI_BIT);
                        _buffer[_index++] = (byte)((value >> 7) | HI_BIT);
                        _buffer[_index++] = (byte)((value >> 14) | HI_BIT);
                        _buffer[_index++] = (byte)(value >> 21);
                        return;
                    default:
                        Debug.Assert(MeasureVarint32(value) == 5);
                        _buffer[_index++] = (byte)(value | HI_BIT);
                        _buffer[_index++] = (byte)((value >> 7) | HI_BIT);
                        _buffer[_index++] = (byte)((value >> 14) | HI_BIT);
                        _buffer[_index++] = (byte)((value >> 21) | HI_BIT);
                        _buffer[_index++] = (byte)(value >> 28);
                        return;
                }
            }
            else
            {
                Throw();
                static void Throw() => throw new NotImplementedException();
            }
        }
        else
        {
            WriteVarintUInt32Slow(value);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteVarintUInt32Slow(uint value)
        => throw new NotImplementedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteString(ReadOnlyMemory<char> value)
        => WriteString(value.Span);

    internal void WriteString(ReadOnlySpan<char> value)
    {
        var bytes = Reader.UTF8.GetByteCount(value);
        WriteVarintUInt32((uint)bytes);
        if (_index + bytes <= _end)
        {
#if USE_SPANS
            var actualBytes = Reader.UTF8.GetBytes(value, _buffer.Slice(_index));
#else
            var actualBytes = Reader.UTF8.GetBytes(value, new Span<byte>(_buffer, _index, bytes));
#endif
            Debug.Assert(actualBytes == bytes);
            _index += bytes;
        }
        else
        {
            WriteStringBytesSlow(value);
        }
    }
    private void WriteStringBytesSlow(ReadOnlySpan<char> value)
        => throw new NotImplementedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteVarintUInt64(ulong value)
    {
        if ((value >> 32) == 0) WriteVarintUInt32((uint)value);
        else WriteVarintUInt64Full(value);
    }
    private void WriteVarintUInt64Full(ulong value)
        => throw new NotImplementedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteBytes(ReadOnlyMemory<byte> value)
        => WriteBytes(value.Span);

    internal void WriteBytes(ReadOnlySpan<byte> value)
    {
        var bytes = value.Length;
        WriteVarintUInt32((uint)bytes);
        if (_index + bytes <= _end)
        {
#if USE_SPANS
            value.CopyTo(_buffer.Slice(_index));
#else
            value.CopyTo(new Span<byte>(_buffer, _index, bytes));
#endif
            _index += bytes;
        }
        else
        {
            WriteBytesBytesSlow(value);
        }
    }
    private void WriteBytesBytesSlow(ReadOnlySpan<byte> value)
        => throw new NotImplementedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSingle(float value)
    {
        if (BitConverter.IsLittleEndian && _index + 4 <= _end)
        {
            Unsafe.WriteUnaligned<float>(ref _buffer[_index], value);
            _index += 4;
        }
        else
        {
            WriteSingleSlow(value);
        }
    }
    private void WriteSingleSlow(float value) => throw new NotImplementedException();
}
public struct Reader : IDisposable
{
    public void Dispose()
    {
        var tmp = _buffer;
        _buffer = null!;
        if (_returnToArrayPool)
        {
            _returnToArrayPool = false;
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }
    byte[] _buffer;
    int _index, _end;
    long _positionBase;
    long Position => _positionBase + _index;
    long _objectEnd;
    bool _returnToArrayPool;
    internal static readonly UTF8Encoding UTF8 = new(false);

    public Reader(Memory<byte> value)
    {
        if (MemoryMarshal.TryGetArray<byte>(value, out var segment))
        {
            _buffer = segment.Array!;
            _index = segment.Offset;
            _end = segment.Offset + segment.Count;
            _returnToArrayPool = false;
        }
        else
        {
            _buffer = ArrayPool<byte>.Shared.Rent(value.Length);
            _index = 0;
            _end = value.Length;
            value.Span.CopyTo(_buffer);
            _returnToArrayPool = true;
        }
        _positionBase = 0;
        _objectEnd = _end;
    }
    public ReadOnlyMemory<byte> ReadBytes(ReadOnlyMemory<byte> value)
    {
        var bytes = ReadLengthPrefix();
        if (bytes == 0) return Empty(value);

        Memory<byte> mutable;
        if (bytes <= value.Length && value.GetRefCount() == 1)
        {
            mutable = MemoryMarshal.AsMemory(value.Slice(0, bytes));
        }
        else
        {
            value.Release();
            mutable = SlabAllocator<byte>.Rent(bytes);
        }

        if (_index + bytes <= _end)
        {
            new Span<byte>(_buffer, _index, bytes).CopyTo(mutable.Span);
            _index += bytes;
        }
        else
        {
            ReadBytesSlow(mutable);
        }
        return mutable;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReadBytesSlow(Memory<byte> value) => throw new NotImplementedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadLengthPrefix()
    {
        var len = ReadVarintInt32();
        if (len < 0) ThrowNegative();
        return len;

        static void ThrowNegative() => throw new InvalidOperationException("Negative length");
    }
    private static ReadOnlyMemory<T> Empty<T>(ReadOnlyMemory<T> value)
    {
        value.Release();
        return default;
    }
    public ReadOnlyMemory<char> ReadString(ReadOnlyMemory<char> value)
    {
        var bytes = ReadLengthPrefix();
        if (bytes == 0) return Empty(value);

        if (_index + bytes <= _end)
        {
            var expectedChars = UTF8.GetCharCount(_buffer, _index, bytes);

            Memory<char> mutable;
            if (expectedChars <= value.Length && value.GetRefCount() == 1)
            {
                mutable = MemoryMarshal.AsMemory(value.Slice(0, expectedChars));
            }
            else
            {
                value.Release();
                mutable = SlabAllocator<char>.Rent(expectedChars);
            }
            var actualChars = UTF8.GetChars(new ReadOnlySpan<byte>(_buffer, _index, bytes), mutable.Span);
            Debug.Assert(expectedChars == actualChars);
            _index += bytes;
            return mutable;
        }
        else
        {
            return ReadStringSlow(bytes, value);
        }
    }
    private ReadOnlyMemory<char> ReadStringSlow(int bytes, ReadOnlyMemory<char> value)
    {
        throw new NotImplementedException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadTag()
    {
        if (Position >= _objectEnd) return 0;
        return ReadVarintUInt32();
    }

    private bool TryReadTag(uint tag)
    {
        if (Position >= _objectEnd) return false;

        if (tag < 128 && _index < _end)
        {
            if (_buffer[_index] == tag)
            {
                _index++;
                return true;
            }
            return false;
        }
        return TryReadTagSlow(tag);
    }
    private bool TryReadTagSlow(uint tag)
    {
        var snapshot = this;
        if (snapshot.ReadTag() == tag)
        {   // confirmed; update state
            this = snapshot;
        }
        return false;
    }


    public ReadOnlyMemory<T> AppendLengthPrefixed<T>(ReadOnlyMemory<T> itemRequests, MessageReader<T> reader, uint tag, int sizeHint)
    {
        Memory<T> target = SlabAllocator<T>.Expand(itemRequests, sizeHint);
        int count = itemRequests.Length;

        var oldEnd = _objectEnd;
        var targetSpan = target.Span;
        do
        {
            var subItemLength = ReadLengthPrefix();
            _objectEnd = Position + subItemLength;
            if (count == targetSpan.Length)
            {
                target = SlabAllocator<T>.Expand(target, sizeHint);;
                targetSpan = target.Span;
            }
            targetSpan[count++] = reader(ref this);
            _objectEnd = oldEnd;
        } while (TryReadTag(tag));

        Debug.Assert(oldEnd >= Position);

        target.TryRecover(count);
        return target.Slice(0, count);
    }

    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public unsafe ReadOnlyMemory<T> UnsafeAppendLengthPrefixed<T>(ReadOnlyMemory<T> itemRequests, delegate*<ref Reader, T> reader, uint tag, int sizeHint)
    {
        Memory<T> target = SlabAllocator<T>.Expand(itemRequests, sizeHint);
        int count = itemRequests.Length;

        var oldEnd = _objectEnd;
        var targetSpan = target.Span;
        do
        {
            var subItemLength = ReadLengthPrefix();
            _objectEnd = Position + subItemLength;
            if (count == targetSpan.Length)
            {
                target = SlabAllocator<T>.Expand(target, sizeHint); ;
                targetSpan = target.Span;
            }
            targetSpan[count++] = reader(ref this);
            _objectEnd = oldEnd;
        } while (TryReadTag(tag));

        Debug.Assert(oldEnd >= Position);

        target.TryRecover(count);
        return target.Slice(0, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingle()
    {
        if (BitConverter.IsLittleEndian && _index + 4 <= _end)
        {
            var value = Unsafe.ReadUnaligned<float>(ref _buffer[_index]);
            _index += 4;
            return value;
        }
        return ReadSingleSlow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private float ReadSingleSlow() => throw new NotImplementedException();

    internal ulong ReadVarintUInt64()
    {
        if (_index + 10 <= _end)
        {
            var buffer = _buffer;
            ulong result = buffer[_index++];
            if (result < 128)
            {
                return result;
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = buffer[_index++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return result;
                }
                shift += 7;
            }
            while (shift < 64);

            ThrowMalformed();
        }
        return ReadVarintUInt64Slow();
    }

    private ulong ReadVarintUInt64Slow() => throw new NotImplementedException();
    internal uint ReadVarintUInt32()
    {
        if (_index + 5 <= _end)
        {
            var buffer = _buffer;
            int tmp = buffer[_index++];
            if (tmp < 128)
            {
                return (uint)tmp;
            }
            int result = tmp & 0x7f;
            if ((tmp = buffer[_index++]) < 128)
            {
                result |= tmp << 7;
            }
            else
            {
                result |= (tmp & 0x7f) << 7;
                if ((tmp = buffer[_index++]) < 128)
                {
                    result |= tmp << 14;
                }
                else
                {
                    result |= (tmp & 0x7f) << 14;
                    if ((tmp = buffer[_index++]) < 128)
                    {
                        result |= tmp << 21;
                    }
                    else
                    {
                        result |= (tmp & 0x7f) << 21;
                        result |= (tmp = buffer[_index++]) << 28;
                        if (tmp >= 128)
                        {
                            // Discard upper 32 bits.
                            // Note that this has to use ReadRawByte() as we only ensure we've
                            // got at least 5 bytes at the start of the method. This lets us
                            // use the fast path in more cases, and we rarely hit this section of code.
                            for (int i = 0; i < 5; i++)
                            {
                                if (ReadRawByte() < 128)
                                {
                                    return (uint)result;
                                }
                            }
                            ThrowMalformed();
                        }
                    }
                }
            }
            return (uint)result;
        }
        return ReadVarintUInt32Slow();
    }

    static void ThrowMalformed() => throw new InvalidOperationException("malformed varint");

    private byte ReadRawByte()
    {
        if (_index < _end)
        {
            return _buffer[_index++];
        }
        return ReadRawByteSlow();
    }
    private byte ReadRawByteSlow() => throw new NotImplementedException();

    private uint ReadVarintUInt32Slow() => throw new NotImplementedException();
    internal int ReadVarintInt32() => (int)ReadVarintUInt32();

    internal long ReadVarintInt64() => (long)ReadVarintUInt64();
}

static class WireTypes
{
    public const int Varint = 0;
    public const int Fixed64 = 1;
    public const int LengthDelimited = 2;
    public const int StartGroup = 3;
    public const int EndGroup = 4;
    public const int Fixed32 = 5;
}
/*
message ForwardRequest {
  string traceId = 1;
  repeated ForwardPerItemRequest itemRequests = 2;
  bytes requestContextInfo = 3;
}
*/
public delegate T MessageReader<T>(ref Reader reader);
public delegate void MessageWriter<T>(T value, ref Writer reader);

public sealed class HCForwardRequest : IDisposable
{
    private ReadOnlyMemory<char> _traceId;
    private ReadOnlyMemory<HCForwardPerItemRequest> _itemRequests;
    private ReadOnlyMemory<byte> _requestContextInfo;

    internal static readonly MessageReader<HCForwardRequest> Reader = ReadSingle;
    internal static readonly MessageWriter<HCForwardRequest> WriterInst = WriteSingle;

    internal static readonly MessageReader<HCForwardRequest> Reader2 = ReadSingle2;
    internal static readonly unsafe delegate*<ref Reader, HCForwardRequest> UnsafeReader = &ReadSingle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HCForwardRequest ReadSingle(ref Reader reader) => Merge(null, ref reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteSingle(HCForwardRequest value, ref Writer writer)
    {
        if (!value._traceId.IsEmpty)
        {
            writer.WriteTag((1 << 3) | WireTypes.LengthDelimited);
            writer.WriteString(value._traceId);
        }
        if (!value._itemRequests.IsEmpty)
        {
            foreach (ref readonly var item in value._itemRequests.Span)
            {
                writer.WriteTag((2 << 3) | WireTypes.LengthDelimited);
                writer.WriteVarintUInt64(HCForwardPerItemRequest.Measure(item));
                HCForwardPerItemRequest.WriteSingle(in item, ref writer);
            }
        }
        if (!value._requestContextInfo.IsEmpty)
        {
            writer.WriteTag((3 << 3) | WireTypes.LengthDelimited);
            writer.WriteBytes(value._requestContextInfo);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static HCForwardRequest Merge(HCForwardRequest? value, ref Reader reader)
    {
        value ??= new(default, default, default);
        uint tag;
        while ((tag = reader.ReadTag()) != 0)
        {
            switch (tag)
            {
                case (1 << 3) | WireTypes.LengthDelimited:
                    value._traceId = reader.ReadString(value._traceId);
                    break;
                case (2 << 3) | WireTypes.LengthDelimited:
                    value._itemRequests = reader.AppendLengthPrefixed(value._itemRequests, HCForwardPerItemRequest.Reader, (2 << 3) | WireTypes.LengthDelimited, 4000);
                    break;
                case (3 << 3) | WireTypes.LengthDelimited:
                    value._requestContextInfo = reader.ReadBytes(value._requestContextInfo);
                    break;
            }
        }
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HCForwardRequest ReadSingle2(ref Reader reader) => Merge2(null, ref reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe HCForwardRequest Merge2(HCForwardRequest? value, ref Reader reader)
    {
        value ??= new(default, default, default);
        uint tag;
        while ((tag = reader.ReadTag()) != 0)
        {
            switch (tag)
            {
                case (1 << 3) | WireTypes.LengthDelimited:
                    value._traceId = reader.ReadString(value._traceId);
                    break;
                case (2 << 3) | WireTypes.LengthDelimited:
                    value._itemRequests = reader.UnsafeAppendLengthPrefixed(value._itemRequests, HCForwardPerItemRequest.UnsafeReader, (2 << 3) | WireTypes.LengthDelimited, 4000);
                    break;
                case (3 << 3) | WireTypes.LengthDelimited:
                    value._requestContextInfo = reader.ReadBytes(value._requestContextInfo);
                    break;
            }
        }
        return value;
    }

    public ReadOnlyMemory<char> TraceId => _traceId;
    public ReadOnlyMemory<HCForwardPerItemRequest> ItemRequests => _itemRequests;
    public ReadOnlyMemory<byte> RequestContextInfo => _requestContextInfo;

    public void Dispose()
    {
        _traceId.Release();
        _itemRequests.ReleaseAll();
        _requestContextInfo.Release();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Measure(HCForwardRequest value)
    {
        ulong length = 0;
        if (!value._traceId.IsEmpty)
        {
            length += 1 + Writer.MeasureWithLengthPrefix(value._traceId);
        }
        if (!value._itemRequests.IsEmpty)
        {
            length += 1 * (uint)value._itemRequests.Length;
            foreach (ref readonly var item in value._itemRequests.Span)
            {
                length += Writer.MeasureWithLengthPrefix(HCForwardPerItemRequest.Measure(item));
            }
        }
        if (!value._requestContextInfo.IsEmpty)
        {
            length += 1 + Writer.MeasureWithLengthPrefix((uint)value._requestContextInfo.Length);
        }
        return length;
    }

    public HCForwardRequest(ReadOnlyMemory<char> traceId, ReadOnlyMemory<HCForwardPerItemRequest> itemRequests, ReadOnlyMemory<byte> requestContextInfo)
    {
        _traceId = traceId;
        _itemRequests = itemRequests;
        _requestContextInfo = requestContextInfo;
    }
}

/*
message ForwardPerItemRequest
{
  bytes itemId = 1;
  bytes itemContext = 2;
}
*/
public readonly struct HCForwardPerItemRequest : IDisposable
{
    private readonly ReadOnlyMemory<byte> _itemId;
    private readonly ReadOnlyMemory<byte> _itemContext;

    private static readonly HCForwardPerItemRequest Default;

    internal static readonly MessageReader<HCForwardPerItemRequest> Reader = ReadSingle;
    internal static readonly unsafe delegate*<ref Reader, HCForwardPerItemRequest> UnsafeReader = &ReadSingle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HCForwardPerItemRequest ReadSingle(ref Reader reader) => new HCForwardPerItemRequest(in Default, ref reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HCForwardPerItemRequest(in HCForwardPerItemRequest value, ref Reader reader)
    {
        this = value;
        uint tag;
        while ((tag = reader.ReadTag()) != 0)
        {
            switch (tag)
            {
                case (1 << 3) | WireTypes.LengthDelimited:
                    _itemId = reader.ReadBytes(_itemId);
                    break;
                case (2 << 3) | WireTypes.LengthDelimited:
                    _itemContext = reader.ReadBytes(_itemContext);
                    break;
            }
        }
    }
    public HCForwardPerItemRequest(ReadOnlyMemory<byte> itemId, ReadOnlyMemory<byte> itemContext)
    {
        _itemId = itemId;
        _itemContext = itemContext;
    }

    public ReadOnlyMemory<byte> ItemId => _itemId;
    public ReadOnlyMemory<byte> ItemContext => _itemContext;

    public void Dispose()
    {
        _itemId.Release();
        _itemContext.Release();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Measure(in HCForwardPerItemRequest value)
    {
        ulong length = 0;
        if (!value._itemId.IsEmpty)
        {
            length += 1 + Writer.MeasureWithLengthPrefix((uint)value._itemId.Length);
        }
        if (!value._itemContext.IsEmpty)
        {
            length += 1 + Writer.MeasureWithLengthPrefix((uint)value._itemContext.Length);
        }
        return length;
    }

    internal static void WriteSingle(in HCForwardPerItemRequest value, ref Writer writer)
    {
        if (!value._itemId.IsEmpty)
        {
            writer.WriteTag((1 << 3) | WireTypes.LengthDelimited);
            writer.WriteBytes(value._itemId);
        }
        if (!value._itemContext.IsEmpty)
        {
            writer.WriteTag((2 << 3) | WireTypes.LengthDelimited);
            writer.WriteBytes(value._itemContext);
        }
    }
}

/*
message ForwardPerItemResponse {
  float result = 1;
  bytes extraResult = 2;
}
*/
public readonly struct HCForwardPerItemResponse : IDisposable
{
    private readonly float _result;
    private readonly ReadOnlyMemory<byte> _extraResult;

    private static readonly HCForwardPerItemResponse Default;

    internal static readonly MessageReader<HCForwardPerItemResponse> Reader = ReadSingle;
    internal static readonly unsafe delegate*<ref Reader, HCForwardPerItemResponse> UnsafeReader = &ReadSingle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HCForwardPerItemResponse ReadSingle(ref Reader reader) => new HCForwardPerItemResponse(in Default, ref reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    internal HCForwardPerItemResponse(in HCForwardPerItemResponse value, ref Reader reader)
    {
        this = value;
        uint tag;
        while ((tag = reader.ReadTag()) != 0)
        {
            switch (tag)
            {
                case (1 << 3) | WireTypes.Fixed32:
                    _result = reader.ReadSingle();
                    break;
                case (2 << 3) | WireTypes.LengthDelimited:
                    _extraResult = reader.ReadBytes(_extraResult);
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Measure(in HCForwardPerItemResponse value)
    {
        ulong length = 0;
        if (value._result != 0)
        {
            length += 1 + 4;
        }
        if (!value._extraResult.IsEmpty)
        {
            length += 1 + Writer.MeasureWithLengthPrefix((uint)value._extraResult.Length);
        }
        return length;
    }

    public HCForwardPerItemResponse(float result, ReadOnlyMemory<byte> extraResult)
    {
        _result = result;
        _extraResult = extraResult;
    }

    public float Result => _result;
    public ReadOnlyMemory<byte> ExtraResult => _extraResult;

    public void Dispose()
    {
        _extraResult.Release();
    }

    internal static void WriteSingle(in HCForwardPerItemResponse value, ref Writer writer)
    {
        if (value._result != 0)
        {
            writer.WriteTag((1 << 3) | WireTypes.Fixed32);
            writer.WriteSingle(value._result);
        }
        if (!value._extraResult.IsEmpty)
        {
            writer.WriteTag((2 << 3) | WireTypes.LengthDelimited);
            writer.WriteBytes(value._extraResult);
        }
    }
}

/*
message ForwardResponse {
  repeated ForwardPerItemResponse itemResponses = 1;
  int64 routeLatencyInUs = 2;
  int64 routeStartTimeInTicks = 3;
}
*/
public sealed class HCForwardResponse : IDisposable
{
    private ReadOnlyMemory<HCForwardPerItemResponse> _itemResponses;
    private long _routeLatencyInUs;
    private long _routeStartTimeInTicks;

    internal static ulong Measure(HCForwardResponse value)
    {
        ulong length = 0;
        if (!value.ItemResponses.IsEmpty)
        {
            length += 1 * (uint)value.ItemResponses.Length;
            foreach (ref readonly var item in value._itemResponses.Span)
            {
                length += Writer.MeasureWithLengthPrefix(HCForwardPerItemResponse.Measure(item));
            }
        }
        if (value._routeLatencyInUs != 0)
        {
            length += 1 + Writer.MeasureVarint64((ulong)value._routeLatencyInUs);
        }
        if (value._routeStartTimeInTicks != 0)
        {
            length += 1 + Writer.MeasureVarint64((ulong)value._routeStartTimeInTicks);
        }
        return length;
    }

    internal static void WriteSingle(HCForwardResponse value, ref Writer writer)
    {
        if (!value.ItemResponses.IsEmpty)
        {
            foreach (ref readonly var item in value._itemResponses.Span)
            {
                writer.WriteTag((1 << 3) | WireTypes.LengthDelimited);
                writer.WriteVarintUInt64(HCForwardPerItemResponse.Measure(item));
                HCForwardPerItemResponse.WriteSingle(in item, ref writer);
            }
        }
        if (value._routeLatencyInUs != 0)
        {
            writer.WriteTag((2 << 3) | WireTypes.Varint);
            writer.WriteVarintUInt64((ulong)value._routeLatencyInUs);
        }
        if (value._routeStartTimeInTicks != 0)
        {
            writer.WriteTag((3 << 3) | WireTypes.Varint);
            writer.WriteVarintUInt64((ulong)value._routeStartTimeInTicks);
        }
    }

    internal static readonly MessageWriter<HCForwardResponse> WriterInst = WriteSingle;
    internal static readonly MessageReader<HCForwardResponse> Reader = ReadSingle;
    internal static readonly MessageReader<HCForwardResponse> Reader2 = ReadSingle2;
    internal static readonly unsafe delegate*<ref Reader, HCForwardResponse> UnsafeReader = &ReadSingle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HCForwardResponse ReadSingle(ref Reader reader) => Merge(null, ref reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static HCForwardResponse ReadSingle2(ref Reader reader) => Merge2(null, ref reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static HCForwardResponse Merge(HCForwardResponse? value, ref Reader reader)
    {
        value ??= new(default, 0, 0);
        uint tag;
        while ((tag = reader.ReadTag()) != 0)
        {
            switch (tag)
            {
                case (1 << 3) | WireTypes.LengthDelimited:
                    value._itemResponses = reader.AppendLengthPrefixed(value._itemResponses, HCForwardPerItemResponse.Reader, (1 << 3) | WireTypes.LengthDelimited, 4000);
                    break;
                case (2 << 3) | WireTypes.Varint:
                    value._routeLatencyInUs = reader.ReadVarintInt64();
                    break;
                case (3 << 3) | WireTypes.Varint:
                    value._routeStartTimeInTicks = reader.ReadVarintInt64();
                    break;
            }
        }
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal unsafe static HCForwardResponse Merge2(HCForwardResponse? value, ref Reader reader)
    {
        value ??= new(default, 0, 0);
        uint tag;
        while ((tag = reader.ReadTag()) != 0)
        {
            switch (tag)
            {
                case (1 << 3) | WireTypes.LengthDelimited:
                    value._itemResponses = reader.UnsafeAppendLengthPrefixed(value._itemResponses, HCForwardPerItemResponse.UnsafeReader, (1 << 3) | WireTypes.LengthDelimited, 4000);
                    break;
                case (2 << 3) | WireTypes.Varint:
                    value._routeLatencyInUs = reader.ReadVarintInt64();
                    break;
                case (3 << 3) | WireTypes.Varint:
                    value._routeStartTimeInTicks = reader.ReadVarintInt64();
                    break;
            }
        }
        return value;
    }

    public HCForwardResponse(ReadOnlyMemory<HCForwardPerItemResponse> itemResponses, long routeLatencyInUs, long routeStartTimeInTicks)
    {
        _itemResponses = itemResponses;
        _routeLatencyInUs = routeLatencyInUs;
        _routeStartTimeInTicks = routeStartTimeInTicks;
    }

    public ReadOnlyMemory<HCForwardPerItemResponse> ItemResponses => _itemResponses;
    public long RouteLatencyInUs => _routeLatencyInUs;
    public long RouteStartTimeInTicks => _routeStartTimeInTicks;

    public void Dispose()
    {
        _itemResponses.ReleaseAll();
    }
}
static class MemoryTools
{
    public static int GetRefCount<T>(this ReadOnlyMemory<T> value)
    {
        return MemoryMarshal.TryGetMemoryManager<T, SlabAllocator<T>.PerThreadSlab>(value, out var manager) ? manager.RefCount : -1;
    }
    public static void Release<T>(this ReadOnlyMemory<T> value)
    {
        if (MemoryMarshal.TryGetMemoryManager<T, SlabAllocator<T>.PerThreadSlab>(value, out var manager))
        {
            manager.Release();
        }
    }
    public static void Release<T>(this Memory<T> value)
    {
        if (MemoryMarshal.TryGetMemoryManager<T, SlabAllocator<T>.PerThreadSlab>(value, out var manager))
        {
            manager.Release();
        }
    }
    internal static void TryRecover<T>(this Memory<T> value, int count)
    {
        if (MemoryMarshal.TryGetMemoryManager<T, SlabAllocator<T>.PerThreadSlab>(value, out var manager, out var start, out var length))
        {
            manager.TryRecoverForCurrentThread(start, length, count);
        }
    }
    public static void ReleaseAll<T>(this ReadOnlyMemory<T> value) where T : struct, IDisposable
    {
        foreach (ref readonly var item in value.Span)
        {
            item.Dispose();
        }
        value.Release();
    }
}
internal static class SlabAllocator<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Memory<T> Expand(ReadOnlyMemory<T> value, int sizeHint)
    {
        int countHint, length;
        if (MemoryMarshal.TryGetMemoryManager<T, SlabAllocator<T>.PerThreadSlab>(value, out var manager, out var start, out length))
        {
            countHint = Math.Max(length, sizeHint); // double, or size hint: whichever is bigger
            if (manager.TryExpandForCurrentThread(start, length, countHint))
            {
                return manager.Memory.Slice(start, length + countHint);
            }
            var newValue = Rent(value.Length + countHint);
            value.CopyTo(newValue);
            manager.Release();
            return newValue;
        }
        else
        {
            length = value.Length;
            countHint = Math.Max(length, sizeHint); // double, or size hint: whichever is bigger
            if (length == 0)
            {
                return Rent(countHint);
            }
            var newValue = Rent(value.Length + countHint);
            value.CopyTo(newValue);
            return newValue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Memory<T> Rent(int length)
    {
        if (length == 0) return default;
        var slab = s_ThreadLocal;
        if (length > 0 && slab is not null && slab.TryRent(length, out var value)) return value;
        return RentSlow(length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Memory<T> RentSlow(int length)
    {
        if (length < 0) ThrowOutOfRange();

        if (length > SlabSize) return new T[length]; // give up for over-sized

        s_ThreadLocal?.Release();
        if (!(s_ThreadLocal = new PerThreadSlab()).TryRent(length, out var value)) ThrowUnableToRent();
        return value;


        static void ThrowOutOfRange() => throw new ArgumentOutOfRangeException(nameof(length));
        static void ThrowUnableToRent() => throw new InvalidOperationException("Unable to allocate from slab!");
    }

    [ThreadStatic]
    private static PerThreadSlab? s_ThreadLocal;

    readonly static int SlabSize = (512 * 1024) / Unsafe.SizeOf<T>(); // , MaxChunkSize = 64 * 1024;

    internal sealed class PerThreadSlab : MemoryManager<T>
    {
        public override Span<T> GetSpan() => _array;
        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            segment = new ArraySegment<T>(_array);
            return true;
        }
        public override MemoryHandle Pin(int elementIndex = 0)
            => throw new NotSupportedException(); // can do if needed; I'm just being lazy

        public override void Unpin()
            => throw new NotSupportedException(); // can do if needed; I'm just being lazy

        public PerThreadSlab()
        {
            _array = ArrayPool<T>.Shared.Rent(SlabSize);
#if DEBUG
                Console.Write("+");
#endif
            _remaining = _array.Length;
            _refCount = 1;
            _memory = base.Memory; // snapshot the underlying memory value, as this is non-trivial and we use it a lot
        }

        public override Memory<T> Memory => _memory;

        public int RefCount => Volatile.Read(ref _refCount);

        private readonly Memory<T> _memory;
        private readonly T[] _array;
        private int _remaining, _refCount;

        void ReturnArrayToPool()
        {
            ArrayPool<T>.Shared.Return(_array);
#if DEBUG
            Console.Write("-");
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Preserve()
        {
            if (Interlocked.Increment(ref _refCount) <= 1) PreserveFail();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        void PreserveFail()
        {
            Interlocked.Decrement(ref _refCount);
            throw new InvalidOperationException("already dead!");
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Release()
        {
            switch (Interlocked.Decrement(ref _refCount))
            {
                case 0:
                    ReturnArrayToPool();
                    break;
                case -1:
                    Throw();
                    break;
                    static void Throw() => throw new InvalidOperationException("released too many times!");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRent(int size, out Memory<T> value)
        {
            if (size <= _remaining && Interlocked.Increment(ref _refCount) > 1)
            {
                value = _memory.Slice(_array.Length - _remaining, size);
                _remaining -= size;
                return true;
            }
            return RentFail(size, out value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool RentFail(int size, out Memory<T> value)
        {
            value = default;
            bool decr = size <= _remaining;
            if (decr) Interlocked.Decrement(ref _refCount);
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Release();
        }

        internal void TryRecoverForCurrentThread(int start, int length, int count)
        {
            if (count < length && ReferenceEquals(this, s_ThreadLocal))
            {
                var localEnd = _array.Length - _remaining;
                var remoteEnd = start + length;
                if (localEnd == remoteEnd)
                {
                    // then we can claw some back!
                    _remaining += length - count;
                }
            }
        }

        internal bool TryExpandForCurrentThread(int start, int length, int count)
        {
            if (ReferenceEquals(this, s_ThreadLocal) && count <= _remaining)
            {
                var localEnd = _array.Length - _remaining;
                var remoteEnd = start + length;
                if (localEnd == remoteEnd)
                {
                    // then we can claw some back!
                    _remaining -= count;
                    return true;
                }
            }
            return false;
        }
    }
}
