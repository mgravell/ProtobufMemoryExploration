using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System;
using System.Buffers.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TestProxyPBN;

namespace GrpcTestService; // for shared namespace just for code simplicity

[SimpleJob(RuntimeMoniker.Net60), MemoryDiagnoser]
public class DeserializeBenchmarks
{
    private const int BatchSize = 3500;
    private const int RequestContextSize = 32 * 1024;
    private const int ItemContextSize = 64;

    static readonly byte[] end = Encoding.UTF8.GetBytes("end"); // "end"u8 in C# future

    private byte[]? requestPayloadBA, responsePayloadBA;
    private ReadOnlyMemory<byte> requestPayloadROM, responsePayloadROM;
    private MemoryStream? requestPayloadMS, responsePayloadMS;

    static void TestTypes()
    {
        static void Throw(string field) => throw new InvalidOperationException("Data error in field: " + field);
        var tmp0 = new byte[] { 1, 2, 3 };
        var tmp1 = new byte[] { 4, 5, 6 };
        var tmp2 = new byte[] { 7, 8, 9 };
        {
            using var orig = new ForwardRequest
            {
                traceId = "abc",
                requestContextInfo = tmp0,
            };
            orig.itemRequests = TestProxyPBN.MemoryExtensions.Add(orig.itemRequests, new ForwardPerItemRequest(tmp1, tmp2));
            using var clone = CustomTypeModel.Instance.DeepClone(orig);
            if (clone.traceId != "abc") Throw(nameof(ForwardRequest.traceId));
            if (!clone.requestContextInfo.Span.SequenceEqual(tmp0)) Throw(nameof(ForwardRequest.requestContextInfo));
            if (clone.itemRequests.Length != 1) Throw(nameof(ForwardRequest.itemRequests));
            if (!clone.itemRequests.Span[0].itemId.Span.SequenceEqual(tmp1)) Throw(nameof(ForwardPerItemRequest.itemId));
            if (!clone.itemRequests.Span[0].itemContext.Span.SequenceEqual(tmp2)) Throw(nameof(ForwardPerItemRequest.itemContext));
            Console.WriteLine(nameof(ForwardRequest) + " validated");
        }
        {
            using var orig = new ForwardResponse
            {
                routeLatencyInUs = 42,
                routeStartTimeInTicks = 92,
            };
            orig.itemResponses = TestProxyPBN.MemoryExtensions.Add(orig.itemResponses, new ForwardPerItemResponse(123F, tmp0));
            using var clone = CustomTypeModel.Instance.DeepClone(orig);
            if (clone.routeLatencyInUs != 42) Throw(nameof(ForwardResponse.routeLatencyInUs));
            if (clone.routeStartTimeInTicks != 92) Throw(nameof(ForwardResponse.routeStartTimeInTicks));
            if (clone.itemResponses.Length != 1) Throw(nameof(ForwardResponse.itemResponses));
            if (clone.itemResponses.Span[0].Result != 123F) Throw(nameof(ForwardPerItemResponse.Result));
            if (!clone.itemResponses.Span[0].extraResult.Span.SequenceEqual(tmp0)) Throw(nameof(ForwardPerItemResponse.extraResult));
            Console.WriteLine(nameof(ForwardResponse) + " validated");
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        if (Program.EnableObjectCache)
        {
            _ = ObjectCache.Singleton; // pre-init
        }

        TestTypes();

        using var request = Program.EnableObjectCache ? ObjectCache.GetForwardRequest() : new ForwardRequest();
        request.traceId = Guid.NewGuid().ToString("N");
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
            request.itemRequests = TestProxyPBN.MemoryExtensions.Add(request.itemRequests, new ForwardPerItemRequest(itemId, itemContext));
        }

        var requestContextInfo = SlabAllocator.Rent(RequestContextSize + 3);
        requestContextInfo.Span.Slice(0, RequestContextSize).Fill((byte)'a');
        end.CopyTo(requestContextInfo.Span.Slice(RequestContextSize));
        request.requestContextInfo = requestContextInfo;

        var ms = new MemoryStream();
        CustomTypeModel.Instance.Serialize(ms, request);
        requestPayloadBA = ms.ToArray();
        requestPayloadROM = requestPayloadBA;
        requestPayloadMS = new MemoryStream(requestPayloadBA);

        using var response = Program.EnableObjectCache ? ObjectCache.GetForwardResponse() : new ForwardResponse();
        foreach (ref readonly var itemRequest in request.itemRequests.Span)
        {
            response.itemResponses = TestProxyPBN.MemoryExtensions.Add(response.itemResponses, new ForwardPerItemResponse(100, SharedExtraResult));
        }
        ms.Position = 0;
        ms.SetLength(0);
        CustomTypeModel.Instance.Serialize(ms, response);
        responsePayloadBA = ms.ToArray();
        responsePayloadROM = responsePayloadBA;
        responsePayloadMS = new MemoryStream(responsePayloadBA);

        Console.WriteLine($"Request: {requestPayloadBA.Length} bytes; response: {responsePayloadBA.Length} bytes");
    }

    private static readonly Memory<byte> SharedExtraResult = new byte[32];

    [Benchmark]
    public void DeserializeRequestGoogle_BA()
    {
        _ = TestProxy.ForwardRequest.Parser.ParseFrom(requestPayloadBA);
    }

    [Benchmark]
    public void DeserializeRequestGoogle_MS()
    {
        requestPayloadMS!.Position = 0;
        _ = TestProxy.ForwardRequest.Parser.ParseFrom(requestPayloadMS);
    }

    [Benchmark]
    public void DeserializeRequestGoogle_BA_H()
    {
        using var obj = TestProxyHacked.ForwardRequest.Parser.ParseFrom(requestPayloadBA);
    }

    [Benchmark]
    public void DeserializeRequestGoogle_MS_H()
    {
        requestPayloadMS!.Position = 0;
        using var obj = TestProxyHacked.ForwardRequest.Parser.ParseFrom(requestPayloadMS);
    }

    [Benchmark]
    public void DeserializeRequestPBN_ROM()
    {
        using var obj = CustomTypeModel.Instance.Deserialize<ForwardRequest>(requestPayloadROM);
    }
    [Benchmark]
    public void DeserializeRequestPBN_MS()
    {
        requestPayloadMS!.Position = 0;
        using var obj = CustomTypeModel.Instance.Deserialize<ForwardRequest>(requestPayloadMS);
    }

    [Benchmark]
    public void DeserializeResponseGoogle_BA()
    {
        _ = TestProxy.ForwardResponse.Parser.ParseFrom(responsePayloadBA);
    }

    [Benchmark]
    public void DeserializeResponseGoogle_MS()
    {
        responsePayloadMS!.Position = 0;
        _ = TestProxy.ForwardResponse.Parser.ParseFrom(responsePayloadMS);
    }

    [Benchmark]
    public void DeserializeResponseGoogle_BA_H()
    {
        using var obj = TestProxyHacked.ForwardResponse.Parser.ParseFrom(responsePayloadBA);
    }

    [Benchmark]
    public void DeserializeResponseGoogle_MS_H()
    {
        responsePayloadMS!.Position = 0;
        using var obj = TestProxyHacked.ForwardResponse.Parser.ParseFrom(responsePayloadMS);
    }

    [Benchmark]
    public void DeserializeResponsePBN_ROM()
    {
        using var obj = CustomTypeModel.Instance.Deserialize<ForwardResponse>(responsePayloadROM);
    }

    [Benchmark]
    public void DeserializeResponsePBN_MS()
    {
        responsePayloadMS!.Position = 0;
        using var obj = CustomTypeModel.Instance.Deserialize<ForwardResponse>(responsePayloadMS);
    }
}