﻿

namespace TestProxyPBN
{
    using System;
    using System.Threading;
    using GrpcTestService;
    using ProtoBuf;
    using ProtoBuf.Serializers;
    using System.Buffers;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using Google.Protobuf;

    public static class MemoryExtensions
    {
        public static bool TryPreserve(this Memory<byte> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<byte, SlabAllocator.PerThreadSlab>(value, out var manager))
            {
                manager.Preserve();
                return true;
            }
            return false;
        }
        public static bool TryPreserve(this ReadOnlyMemory<byte> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<byte, SlabAllocator.PerThreadSlab>(value, out var manager))
            {
                manager.Preserve();
                return true;
            }
            return false;
        }
        public static bool TryRelease(this Memory<byte> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<byte, SlabAllocator.PerThreadSlab>(value, out var manager))
            {
                manager.Release();
                return true;
            }
            return false;
        }
        public static bool TryRelease(this ReadOnlyMemory<byte> value)
        {
            if (MemoryMarshal.TryGetMemoryManager<byte, SlabAllocator.PerThreadSlab>(value, out var manager))
            {
                manager.Release();
                return true;
            }
            return false;
        }
        public static bool TryRecycle<T>(this Memory<T> value)
        {
            if (MemoryMarshal.TryGetArray<T>(value, out var segment))
            {
                ArrayPool<T>.Shared.Return(segment.Array);
                return true;
            }
            return false;
        }
        public static bool TryRecycle<T>(this ReadOnlyMemory<T> value)
        {
            if (MemoryMarshal.TryGetArray<T>(value, out var segment))
            {
                ArrayPool<T>.Shared.Return(segment.Array);
                return true;
            }
            return false;
        }
        public static bool IsTrivial(this Memory<byte> value)
            => value.IsEmpty && !MemoryMarshal.TryGetMemoryManager<byte, SlabAllocator.PerThreadSlab>(value, out _);

        internal static T[] GetBackingArray<T>(Memory<T> memory, out int count, bool forAppend)
        {
            if (MemoryMarshal.TryGetArray<T>(memory, out var segment) && segment.Offset == 0)
            {
                count = segment.Count;
                return segment.Array;
            }
            if (!memory.IsEmpty) Throw();
            count = 0;
            return forAppend ? ArrayPool<T>.Shared.Rent(InitialSize) : Array.Empty<T>();

            static void Throw() => throw new InvalidOperationException("Not a suitable backing array");
        }
        const int InitialSize = 16;
        internal static void EnsureCapacity<T>(ref T[] array, int capacity)
        {
            var len = array.Length;
            if (capacity > array.Length)
            {
                if (len == 0)
                {
                    array = ArrayPool<T>.Shared.Rent(capacity);
                }
                else
                {
                    var newArr = ArrayPool<T>.Shared.Rent(capacity);
                    Array.Copy(array, newArr, len);
                    ArrayPool<T>.Shared.Return(array);
                    array = newArr;
                }
            }
        }

        internal static void Extend<T>(ref T[] array)
        {
            var len = array.Length;
            if (len == 0)
            {
                array = ArrayPool<T>.Shared.Rent(InitialSize);
            }
            else
            {
                var newArr = ArrayPool<T>.Shared.Rent(len * 2);
                Array.Copy(array, newArr, len);
                ArrayPool<T>.Shared.Return(array);
                array = newArr;
            }
        }

        internal static Memory<T> Add<T>(Memory<T> memory, T value)
        {
            var arr = GetBackingArray(memory, out var count, true);
            if (count == arr.Length) Extend(ref arr);
            arr[count++] = value;
            return new Memory<T>(arr, 0, count);
        }
    }
    internal sealed class SlabAllocator : IMemoryConverter<Memory<byte>, byte>, IMemoryManager
    {
        private SlabAllocator() { }
        public static readonly SlabAllocator Instance = new SlabAllocator();

        Memory<byte> IMemoryConverter<Memory<byte>, byte>.NonNull(in Memory<byte> value) => value;

        int IMemoryConverter<Memory<byte>, byte>.GetLength(in Memory<byte> value) => value.Length;
        Memory<byte> IMemoryConverter<Memory<byte>, byte>.GetMemory(in Memory<byte> value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Memory<byte> IMemoryConverter<Memory<byte>, byte>.Expand(ISerializationContext context, ref Memory<byte> value, int additionalCapacity)
        {
            if (value.IsTrivial() && additionalCapacity <= MaxChunkSize)
            {   // optimize for smallish and single-read (i.e. no pre-existing value)
                return value = PerThreadSlab.RentMemory(additionalCapacity);
            }
            return ExpandNonTrivialOrLarge(context, ref value, additionalCapacity);
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        private Memory<byte> ExpandNonTrivialOrLarge(ISerializationContext context, ref Memory<byte> value, int additionalCapacity)
        {
            var oldValue = value;
            if (value.Length + additionalCapacity <= MaxChunkSize) // only use the slab allocator for smallish values
            {
                int oldCapacity = value.Length;
                value = PerThreadSlab.RentMemory(oldCapacity + additionalCapacity);
                if (oldCapacity != 0) oldValue.CopyTo(value);
                oldValue.TryRelease();
                return value.Slice(oldCapacity, additionalCapacity);
            }

            // use the default implementation instead
            IMemoryConverter<Memory<byte>, byte> obj = DefaultMemoryConverter<byte>.Instance;
            var result = obj.Expand(context, ref value, additionalCapacity);
            oldValue.TryRelease();
            return value;
        }

        const int SlabSize = 512 * 1024, MaxChunkSize = 64 * 1024;
        internal sealed class PerThreadSlab : MemoryManager<byte>
        {
            [ThreadStatic]
            private static PerThreadSlab? s_ThreadLocal;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static Memory<byte> RentMemory(int size)
            // optimistically hope that the existing obj (without worrying about volatility etc)
            // can handle the allocation directly
            {
                var tmp = s_ThreadLocal;
                return tmp is not null && tmp.TryRentMemory(size, out var value) ? value : RentWithMemoryAlloc(size);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Memory<byte> RentWithMemoryAlloc(int size)
            {
                // double-checked fallback; we might need to allocate a new 
                var tmp = s_ThreadLocal;
                Memory<byte> value;
                if (tmp is not null)
                {
                    if (tmp.TryRentMemory(size, out value))
                    {
                        return value;
                    }
                    tmp.Release();
                }
                tmp = s_ThreadLocal = new PerThreadSlab();
                if (!tmp.TryRentMemory(size, out value)) Throw();
                return value;

                static void Throw() => throw new InvalidOperationException("Unable to allocate from slab!");
            }

            public override Span<byte> GetSpan() => array;
            protected override bool TryGetArray(out ArraySegment<byte> segment)
            {
                segment = new ArraySegment<byte>(array);
                return true;
            }
            public override MemoryHandle Pin(int elementIndex = 0)
                => throw new NotSupportedException(); // can do if needed; I'm just being lazy

            public override void Unpin()
                => throw new NotSupportedException(); // can do if needed; I'm just being lazy

            public PerThreadSlab()
            {
                array = ArrayPool<byte>.Shared.Rent(SlabSize);
#if DEBUG
                Console.Write("+");
#endif
                remaining = array.Length;
                count = 1;
                _memory = base.Memory; // snapshot the underlying memory value, as this is non-trivial and we use it a lot
            }

            public override Memory<byte> Memory => _memory;

            private readonly Memory<byte> _memory;
            private readonly byte[] array;
            private int remaining, count;

            private static readonly Action<Memory<byte>> s_recycle = static memory =>
            {
                if (MemoryMarshal.TryGetMemoryManager<byte, PerThreadSlab>(memory, out var slab))
                {
                    slab.Release();
                }
            };

            void ReturnArrayToPool()
            {
                ArrayPool<byte>.Shared.Return(array);
#if DEBUG
                Console.Write("-");
#endif
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void Preserve()
            {
                if (Interlocked.Increment(ref count) <= 1) PreserveFail();
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            void PreserveFail()
            {
                Interlocked.Decrement(ref count);
                throw new InvalidOperationException("already dead!");
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void Release()
            {
                switch (Interlocked.Decrement(ref count))
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
            public bool TryRentMemory(int size, out Memory<byte> value)
            {
                if (size <= remaining && Interlocked.Increment(ref count) > 1)
                {
                    value = _memory.Slice(array.Length - remaining, size);
                    remaining -= size;
                    return true;
                }
                return RentMemoryFail(size, out value);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            bool RentMemoryFail(int size, out Memory<byte> value)
            {
                value = default;
                bool decr = size <= remaining;
                if (decr) Interlocked.Decrement(ref count);
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) Release();
            }
        }

        internal static Memory<byte> Rent(int bytes)
            => PerThreadSlab.RentMemory(bytes);

        Memory<byte> IMemoryManager.Rent(int size)
            => PerThreadSlab.RentMemory(size);

        void IMemoryManager.Return(Memory<byte> value)
            => value.TryRelease();
    }
    partial class SomeSerializer // implementation code
    {
        // provide our our memory API
        static partial void GetMemoryConverter(ref IMemoryConverter<Memory<byte>, byte> value)
            => value = SlabAllocator.Instance; // this is all we need to do to provide a custom allocator
    }
    sealed partial class ForwardRequest : IDisposable
    {
        public void Dispose()
        {
            var __oldMemory = requestContextInfo;
            requestContextInfo = default;
            __oldMemory.TryRelease();
            
            foreach (ref readonly ForwardPerItemRequest tmp in itemRequests.Span)
            {
                tmp.Dispose();
            }
            itemRequests.TryRecycle();
            itemRequests = default;
            traceId = "";
            if (Program.EnableObjectCache)
            {
                ObjectCache.ReturnForwardRequest(this);
            }
        }
    }
    partial struct ForwardPerItemRequest : IDisposable
    {
        public void Dispose()
        {
            itemId.TryRelease();
            itemContext.TryRelease();
        }
    }
    partial struct ForwardPerItemResponse : IDisposable
    {
        public void Dispose()
        {
            extraResult.TryRelease();
        }
    }
    sealed partial class ForwardResponse : IDisposable
    {
        public void Dispose()
        {
            foreach (ref readonly ForwardPerItemResponse tmp in itemResponses.Span)
            {
                tmp.Dispose();
            }
            itemResponses.TryRecycle();
            routeStartTimeInTicks = 0;
            routeLatencyInUs = 0;
            if (Program.EnableObjectCache)
            {
                ObjectCache.ReturnForwardResponse(this);
            }
        }
    }
}
namespace GrpcTestService
{
    using TestProxyPBN;
    public static class ForwardRequestPoolPolicy
    {
        public static ForwardRequest Create()
        {
            var tmp = new ForwardRequest();
            //tmp.itemRequests.EnsureCapacity(4096);
            return tmp;
        }
    }


    public static class ForwardResponsePoolPolicy
    {
        public static ForwardResponse Create()
        {
            var tmp = new ForwardResponse();
            return tmp;
        }
    }

    public class ObjectCache
    {
        private ObjectPool<ForwardRequest> forwardRequestPool;
        private ObjectPool<ForwardResponse> forwardResponsePool;

        public ObjectCache()
        {
            this.forwardRequestPool = new ObjectPool<ForwardRequest>(ForwardRequestPoolPolicy.Create, 1024);

            for (int i = 0; i < 5; i++)
            {
                var tmp = new ForwardRequest();
                //tmp.itemRequests.EnsureCapacity(4096);
                this.forwardRequestPool.Free(tmp);
            }

            this.forwardResponsePool = new ObjectPool<ForwardResponse>(ForwardResponsePoolPolicy.Create, 1024);

            for (int i = 0; i < 5; i++)
            {
                var tmp = new ForwardResponse();
                this.forwardResponsePool.Free(tmp);
            }
        }

        public static ObjectCache Singleton { get; private set; } = new ObjectCache();

        public static ForwardRequest GetForwardRequest()
        {
            return Singleton.forwardRequestPool.Allocate();
        }

        public static void ReturnForwardRequest(ForwardRequest obj)
        {
            Singleton.forwardRequestPool.Free(obj);
        }

        public static ForwardResponse GetForwardResponse()
        {
            return Singleton.forwardResponsePool.Allocate();
        }

        public static void ReturnForwardResponse(ForwardResponse obj)
        {
            Singleton.forwardResponsePool.Free(obj);
        }
    }
}
