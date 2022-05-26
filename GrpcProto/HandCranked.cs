#nullable enable
using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HandCranked;

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
    private static readonly UTF8Encoding UTF8 = new(false);

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
        Memory<T> target;
        int count = itemRequests.Length;
        if (count == 0)
        {
            target = SlabAllocator<T>.Rent(sizeHint);
        }
        else
        {
            target = SlabAllocator<T>.Rent(count + sizeHint);
            itemRequests.CopyTo(target);
        }
        itemRequests.Release();

        var oldEnd = _objectEnd;
        var targetSpan = target.Span;
        do
        {
            var length = ReadLengthPrefix();
            _objectEnd = Position + length;
            if (count == targetSpan.Length)
            {
                var expanded = SlabAllocator<T>.Rent(Math.Min(2 * count, count + 64000));
                target.CopyTo(expanded);
                target.Release();
                target = expanded;
                targetSpan = target.Span;
            }
            targetSpan[count++] = reader(ref this);
            _objectEnd = oldEnd;
        } while (TryReadTag(tag));

        Debug.Assert(oldEnd >= Position);

        target.TryRecover(count);
        return target.Slice(0, count);
    }

    //public unsafe ReadOnlyMemory<T> Append<T>(ReadOnlyMemory<T> itemRequests, delegate*<ref Reader, T> reader, int sizeHint = 8)
    //{
    //    throw new NotImplementedException();
    //}

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

public sealed class HCForwardRequest : IDisposable
{
    private ReadOnlyMemory<char> _traceId;
    private ReadOnlyMemory<HCForwardPerItemRequest> _itemRequests;
    private ReadOnlyMemory<byte> _requestContextInfo;

    internal static readonly MessageReader<HCForwardRequest> Reader = (ref Reader reader) => Merge(null, ref reader);

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

    public ReadOnlyMemory<char> TraceId => _traceId;
    public ReadOnlyMemory<HCForwardPerItemRequest> ItemRequests => _itemRequests;
    public ReadOnlyMemory<byte> RequestContextInfo => _requestContextInfo;

    public void Dispose()
    {
        _traceId.Release();
        _itemRequests.ReleaseAll();
        _requestContextInfo.Release();
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

    internal static readonly MessageReader<HCForwardPerItemRequest> Reader = (ref Reader reader) => new HCForwardPerItemRequest(in Default, ref reader);

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

    internal static readonly MessageReader<HCForwardPerItemResponse> Reader = (ref Reader reader) => new HCForwardPerItemResponse(in Default, ref reader);

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


    internal static readonly MessageReader<HCForwardResponse> Reader = (ref Reader reader) => Merge(null, ref reader);

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
    }
}
