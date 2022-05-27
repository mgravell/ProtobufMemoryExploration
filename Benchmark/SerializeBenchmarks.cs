using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Google.Protobuf;
using HandCranked;
using ProtoBuf;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TestProxyPBN;

namespace GrpcTestService; // for shared namespace just for code simplicity

[SimpleJob(RuntimeMoniker.Net60), MemoryDiagnoser]
public class SerializeBenchmarks
{
    private const int BatchSize = 3500;
    private const int RequestContextSize = 32 * 1024;
    private const int ItemContextSize = 64;

    static readonly byte[] end = Encoding.UTF8.GetBytes("end"); // "end"u8 in C# future

    //private byte[]? requestPayloadBA, responsePayloadBA;
    //private ReadOnlyMemory<byte> requestPayloadROM, responsePayloadROM;
    //private MemoryStream? requestPayloadMS, responsePayloadMS;

    //static void TestTypes()
    //{
    //    static void Throw(string field) => throw new InvalidOperationException("Data error in field: " + field);
    //    var tmp0 = new byte[] { 1, 2, 3 };
    //    var tmp1 = new byte[] { 4, 5, 6 };
    //    var tmp2 = new byte[] { 7, 8, 9 };
    //    {
    //        using var orig = new ForwardRequest
    //        {
    //            traceId = "abc",
    //            requestContextInfo = tmp0,
    //            itemRequests = { new ForwardPerItemRequest(tmp1, tmp2) }
    //        };
    //        using var clone = CustomTypeModel.Instance.DeepClone(orig);
    //        if (clone.traceId != "abc") Throw(nameof(ForwardRequest.traceId));
    //        if (!clone.requestContextInfo.Span.SequenceEqual(tmp0)) Throw(nameof(ForwardRequest.requestContextInfo));
    //        if (clone.itemRequests.Count != 1) Throw(nameof(ForwardRequest.itemRequests));
    //        if (!clone.itemRequests[0].itemId.Span.SequenceEqual(tmp1)) Throw(nameof(ForwardPerItemRequest.itemId));
    //        if (!clone.itemRequests[0].itemContext.Span.SequenceEqual(tmp2)) Throw(nameof(ForwardPerItemRequest.itemContext));
    //        Console.WriteLine(nameof(ForwardRequest) + " validated");
    //    }
    //    {
    //        using var orig = new ForwardResponse
    //        {
    //            routeLatencyInUs = 42,
    //            routeStartTimeInTicks = 92,
    //            itemResponses = { new ForwardPerItemResponse(123F, tmp0) }
    //        };
    //        using var clone = CustomTypeModel.Instance.DeepClone(orig);
    //        if (clone.routeLatencyInUs != 42) Throw(nameof(ForwardResponse.routeLatencyInUs));
    //        if (clone.routeStartTimeInTicks != 92) Throw(nameof(ForwardResponse.routeStartTimeInTicks));
    //        if (clone.itemResponses.Count != 1) Throw(nameof(ForwardResponse.itemResponses));
    //        if (clone.itemResponses[0].Result != 123F) Throw(nameof(ForwardPerItemResponse.Result));
    //        if (!clone.itemResponses[0].extraResult.Span.SequenceEqual(tmp0)) Throw(nameof(ForwardPerItemResponse.extraResult));
    //        Console.WriteLine(nameof(ForwardResponse) + " validated");
    //    }
    //}

    private static readonly Memory<byte> SharedExtraResult = new byte[32];

    ForwardRequest _pbnRequest;
    ForwardResponse _pbnResponse;
    TestProxy.ForwardRequest _gpbRequest;
    TestProxy.ForwardResponse _gpbResponse;
    TestProxyHacked.ForwardRequest _gpbhRequest;
    TestProxyHacked.ForwardResponse _gpbhResponse;
    HCForwardRequest _hcRequest;
    HCForwardResponse _hcResponse;
    

    [GlobalSetup]
    public void Setup()
    {
        var pbnRequest = Program.EnableObjectCache ? ObjectCache.GetForwardRequest() : new ForwardRequest();
        pbnRequest.traceId = Guid.NewGuid().ToString("N");
        Span<byte> scratch = stackalloc byte[16];
        static void Throw() => throw new InvalidOperationException("Unable to format " + nameof(ForwardPerItemRequest.itemId));
        for (int i = 0; i < BatchSize; ++i)
        {
            if (!Utf8Formatter.TryFormat(i, scratch, out int bytes))
                Throw();
            var itemId = SlabAllocator.Rent(bytes);
            scratch.Slice(0, bytes).CopyTo(itemId.Span);
            var itemContext = SlabAllocator.Rent(ItemContextSize);
            itemContext.Span.Fill((byte)'b');
            pbnRequest.itemRequests.Add(new ForwardPerItemRequest(itemId, itemContext));
        }

        var requestContextInfo = SlabAllocator.Rent(RequestContextSize + 3);
        requestContextInfo.Span.Slice(0, RequestContextSize).Fill((byte)'a');
        end.CopyTo(requestContextInfo.Span.Slice(RequestContextSize));
        pbnRequest.requestContextInfo = requestContextInfo;

        _pbnRequest = pbnRequest;

        var pbnResponse = Program.EnableObjectCache ? ObjectCache.GetForwardResponse() : new ForwardResponse();
        foreach (var itemRequest in pbnRequest.itemRequests)
        {
            pbnResponse.itemResponses.Add(new ForwardPerItemResponse(100, SharedExtraResult));
        }
        _pbnResponse = pbnResponse;

        var gpbRequest = new TestProxy.ForwardRequest();
        gpbRequest.TraceId = pbnRequest.traceId;
        gpbRequest.RequestContextInfo = ByteString.CopyFrom(pbnRequest.requestContextInfo.Span);
        foreach (var itemRequest in pbnRequest.itemRequests)
        {
            gpbRequest.ItemRequests.Add(new TestProxy.ForwardPerItemRequest
            {
                ItemId = Encoding.UTF8.GetString(itemRequest.itemId.Span),
                ItemContext = Google.Protobuf.ByteString.CopyFrom(itemRequest.itemContext.Span),
            });
        }
        _gpbRequest = gpbRequest;

        var gpbResponse = new TestProxy.ForwardResponse();
        foreach (var itemResponse in pbnResponse.itemResponses)
        {
            gpbResponse.ItemResponses.Add(new TestProxy.ForwardPerItemResponse
            {
                Result = itemResponse.Result,
                ExtraResult = Google.Protobuf.ByteString.CopyFrom(itemResponse.extraResult.Span),
            });
        }
        _gpbResponse = gpbResponse;

        var gpbhRequest = new TestProxyHacked.ForwardRequest();
        gpbhRequest.TraceId = pbnRequest.traceId;
        gpbhRequest.RequestContextInfo = pbnRequest.requestContextInfo;
        foreach (var itemRequest in pbnRequest.itemRequests)
        {
            gpbhRequest.ItemRequests.Add(new TestProxyHacked.ForwardPerItemRequest
            {
                ItemId = Encoding.UTF8.GetString(itemRequest.itemId.Span),
                ItemContext = itemRequest.itemContext,
            });
        }
        _gpbhRequest = gpbhRequest;

        var gpbhResponse = new TestProxyHacked.ForwardResponse();
        foreach (var itemResponse in pbnResponse.itemResponses)
        {
            gpbhResponse.ItemResponses.Add(new TestProxyHacked.ForwardPerItemResponse
            {
                Result = itemResponse.Result,
                ExtraResult = itemResponse.extraResult,
            });
        }
        _gpbhResponse = gpbhResponse;



        var hcRequests = SlabAllocator<HCForwardPerItemRequest>.Rent(pbnRequest.itemRequests.Count);
        var index = 0;
        foreach (var itemRequest in pbnRequest.itemRequests)
        {
            hcRequests.Span[index++] = new HCForwardPerItemRequest(itemRequest.itemId, itemRequest.itemContext);
        }
        _hcRequest = new HCForwardRequest(pbnRequest.traceId.AsMemory(), hcRequests, pbnRequest.requestContextInfo);


        var hcResponses = SlabAllocator<HCForwardPerItemResponse>.Rent(pbnResponse.itemResponses.Count);
        index = 0;
        foreach (var itemResponse in pbnResponse.itemResponses)
        {
            hcResponses.Span[index++] = new HCForwardPerItemResponse(itemResponse.Result, itemResponse.extraResult);
        }
        _hcResponse = new HCForwardResponse(hcResponses, 0, 0);
    }

    readonly MemoryStream _out = new MemoryStream();
    private MemoryStream Empty()
    {
        _out.Position = 0;
        _out.SetLength(0);
        return _out;
    }

    [Benchmark]
    public int MeasureRequestGoogle()
    {
        return _gpbhRequest.CalculateSize();
    }
    [Benchmark]
    public ulong MeasureRequestHandCranked()
    {
        return HCForwardRequest.Measure(_hcRequest);
    }

    [Benchmark]
    public int MeasureResponseGoogle()
    {
        return _gpbResponse.CalculateSize();
    }
    [Benchmark]
    public ulong MeasureResponseHandCranked()
    {
        return HCForwardResponse.Measure(_hcResponse);
    }

    //[Benchmark]
    //public void SerializeRequestGoogle_MS()
    //{
    //    _gpbRequest.WriteTo(Empty());
    //}
    //[Benchmark]
    //public void SerializeRequestGoogle_MS_H()
    //{
    //    _gpbhRequest.WriteTo(Empty());
    //}

    //[Benchmark]
    //public void SerializeRequestPBN_MS()
    //{
    //    CustomTypeModel.Instance.Serialize(Empty(), _pbnRequest);
    //}

    //[Benchmark]
    //public void SerializeResponseGoogle_MS()
    //{
    //    _gpbResponse.WriteTo(Empty());
    //}

    //[Benchmark]
    //public void SerializeResponseGoogle_MS_H()
    //{
    //    _gpbhResponse.WriteTo(Empty());
    //}

    //[Benchmark]
    //public void SerializeResponsePBN_MS()
    //{
    //    CustomTypeModel.Instance.Serialize(Empty(), _pbnResponse);
    //}

    //[Benchmark]
    //public void MeasureSerializeRequestGPB_BW()
    //{   // simulate what happens in generated __Helper_SerializeMessage
    //    _bw.Clear();
    //    _gpbRequest.CalculateSize();
    //    MessageExtensions.WriteTo(_gpbRequest, _bw);
    //}

    //[Benchmark]
    //public void MeasureSerializeRequestGPB_BW_H()
    //{   // simulate what happens in generated __Helper_SerializeMessage
    //    _bw.Clear();
    //    _gpbhRequest.CalculateSize();
    //    MessageExtensions.WriteTo(_gpbhRequest, _bw);
    //}

    //[Benchmark]
    //public void MeasureSerializeRequestPBN_BW()
    //{
    //    // simulate what happens in ContextualSerialize
    //    _bw.Clear();
    //    IMeasuredProtoOutput<IBufferWriter<byte>> measuredSerializer = CustomTypeModel.Instance;
    //    using var measured = measuredSerializer.Measure(_pbnRequest);
    //    int len = checked((int)measured.Length);
    //    measuredSerializer.Serialize(measured, _bw);
    //}

    MyBufferWriter _bw = new MyBufferWriter();

    sealed class MyBufferWriter : IBufferWriter<byte>
    {
        byte[] _arr = Array.Empty<byte>();
        int _space;

        public void Clear() => _space = _arr.Length;
        public void Advance(int count) => _space -= count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            if (sizeHint < 10) sizeHint = 10;
            if (sizeHint > _space) Resize(sizeHint);
            return new Memory<byte>(_arr, _arr.Length - _space, _space);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (sizeHint < 10) sizeHint = 10;
            if (sizeHint > _space) Resize(sizeHint);
            return new Span<byte>(_arr, _arr.Length - _space, _space);
        }

        private void Resize(int sizeHint)
        {
            var usedBytes = _arr.Length - _space;
            var newMinBytes = usedBytes + sizeHint;
            var newArr = ArrayPool<byte>.Shared.Rent(newMinBytes);
            Buffer.BlockCopy(_arr, 0, newArr, 0, usedBytes);
            ArrayPool<byte>.Shared.Return(_arr);
            _arr = newArr;
            _space = newArr.Length - _space;
        }
    }




    //[Benchmark]
    //public void DeserializeRequestPBN_ROM()
    //{
    //    using var obj = CustomTypeModel.Instance.Deserialize<ForwardRequest>(requestPayloadROM);
    //}
    //[Benchmark]
    //public void DeserializeRequestPBN_MS()
    //{
    //    requestPayloadMS!.Position = 0;
    //    using var obj = CustomTypeModel.Instance.Deserialize<ForwardRequest>(requestPayloadMS);
    //}

    //[Benchmark]
    //public void DeserializeResponseGoogle_BA()
    //{
    //    _ = TestProxy.ForwardResponse.Parser.ParseFrom(responsePayloadBA);
    //}

    //[Benchmark]
    //public void DeserializeResponseGoogle_MS()
    //{
    //    responsePayloadMS!.Position = 0;
    //    _ = TestProxy.ForwardResponse.Parser.ParseFrom(responsePayloadMS);
    //}

    //[Benchmark]
    //public void DeserializeResponsePBN_ROM()
    //{
    //    using var obj = CustomTypeModel.Instance.Deserialize<ForwardResponse>(responsePayloadROM);
    //}

    //[Benchmark]
    //public void DeserializeResponsePBN_MS()
    //{
    //    responsePayloadMS!.Position = 0;
    //    using var obj = CustomTypeModel.Instance.Deserialize<ForwardResponse>(responsePayloadMS);
    //}
}