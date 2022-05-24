#define HACKUP

using Grpc.Net.Client;
using GrpcTestClient;
using GrpcTestService;
using ProtoBuf;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Meta;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using TestProxyPBN;

namespace GrpcTestService // for shared namespace just for code simplicity
{
    internal class Program
    {
        public const bool EnableObjectCache = true;
        private const int ThreadNum = 1;

        private const int BatchSize = 3500;
        private const int RequestContextSize = 32 * 1024;
        private const int ItemContextSize = 64;

        private static void Main(string[] args)
        {
            ServicePointManager.DefaultConnectionLimit = 1000;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            if (EnableObjectCache)
            {
                RuntimeTypeModel.Default.Add<ForwardResponse>().SetFactory(typeof(ObjectCache).GetMethod(nameof(ObjectCache.GetForwardResponse), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                // RuntimeTypeModel.Default.Add<ForwardPerItemResponse>().SetFactory(typeof(ObjectCache).GetMethod(nameof(ObjectCache.GetForwardPerItemResponse), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            }

            List<Thread> threads = new List<Thread>();
            for (int i = 0; i < ThreadNum; i++)
            {
                threads.Add(new Thread(() => RunCore()));
            }

            foreach (var th in threads)
            {
                th.Start();
            }

            foreach (var th in threads)
            {
                th.Join();
            }
        }

        private static GrpcChannel CreateChannel()
        {
            /*
            return GrpcChannel.ForAddress(
                $"http://localhost:81",
                new GrpcChannelOptions
                {
                    Credentials = ChannelCredentials.Insecure,
                    MaxReceiveMessageSize = 1 * 1024 * 1024,
                    HttpHandler = new SocketsHttpHandler
                    {
                        EnableMultipleHttp2Connections = true,
                        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                        KeepAlivePingTimeout = TimeSpan.FromSeconds(1000),
                        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                        PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                        InitialHttp2StreamWindowSize = 1 * 1024 * 1024,
                    },
                });
            */

            return GrpcChannel.ForAddress(
                $"http://localhost:81");
        }
        static byte[] end = Encoding.UTF8.GetBytes("end"); // "end"u8 in C# future

#if HACKUP
        private static long RunCore()
        {
            using (var channel = CreateChannel())
            {
                Span<byte> scratch = stackalloc byte[16];
                static void Throw() => throw new InvalidOperationException("Unable to format " + nameof(ForwardPerItemRequest.itemId));

                var client = new TestProxyPBN.TestProxy.TestProxyClient(channel);
                var request = new TestProxyPBN.ForwardRequest { traceId = Guid.NewGuid().ToString("N") };

                for (int i = 0; i < BatchSize; ++i)
                {
                    if (!Utf8Formatter.TryFormat(i, scratch, out int bytes))
                        Throw();
                    var itemId = SlabAllocator.Rent(bytes);
                    scratch.Slice(0, bytes).CopyTo(itemId.Span);

                    var itemContext = SlabAllocator.Rent(ItemContextSize);
                    itemContext.Span.Fill((byte)'b');

                    request.itemRequests = TestProxyPBN.MemoryExtensions.Add(request.itemRequests, new TestProxyPBN.ForwardPerItemRequest(itemId, itemContext));
                }

                var requestContextInfo = SlabAllocator.Rent(RequestContextSize + 3);
                requestContextInfo.Span.Slice(0, RequestContextSize).Fill((byte)'a');
                end.CopyTo(requestContextInfo.Span.Slice(RequestContextSize));
                request.requestContextInfo = requestContextInfo;

                // warm up like establishing connection
                while (true)
                {
                    request.traceId = Guid.NewGuid().ToString("N");
                    var ret = SendRequest(client, request, true);
                    if (ret > 0)
                    {
                        break;
                    }

                    Thread.Sleep(1000);
                }

                Console.WriteLine("Ready to send simulated requests");

                var latenciesInUs = new long[10000];
                Array.Clear(latenciesInUs);
                int startIndex = 0;
                int totalCount = 0;
                DateTime startTime = DateTime.Now;
                while (true)
                {
                    totalCount++;
                    request.traceId = Guid.NewGuid().ToString("N");
                    var latencyInUs = SendRequest(client, request, false);
                    if (latencyInUs > 0)
                    {
                        latenciesInUs[startIndex++] = latencyInUs;
                    }

                    if (startIndex == latenciesInUs.Length)
                    {
                        Array.Sort(latenciesInUs);
                        DateTime endTime = DateTime.Now;
                        var qps = totalCount * 1.0 / (endTime - startTime).TotalSeconds;
                        var successRate = latenciesInUs.Length * 1.0 / totalCount;

                        Console.WriteLine($"successful rate {successRate} = {latenciesInUs.Length}/{totalCount}");
                        Console.WriteLine($"qps {qps} = {totalCount}/({endTime.ToString("yyyy-MM-dd HH:mm:ss")} - {startTime.ToString("yyyy-MM-dd HH:mm:ss")})");
                        Console.WriteLine($"min   latency in Us: {latenciesInUs.First()}");
                        Console.WriteLine($"max   latency in Us: {latenciesInUs.Last()}");
                        Console.WriteLine($"50%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.5)]}");
                        Console.WriteLine($"90%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.9)]}");
                        Console.WriteLine($"95%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.95)]}");
                        Console.WriteLine($"99%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.99)]}");
                        Console.WriteLine($"99.9% latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.999)]}");

                        startIndex = 0;
                        totalCount = 0;
                        startTime = DateTime.Now;
                        Array.Clear(latenciesInUs);
                    }
                }
            }
        }

        private static long SendRequest(TestProxyPBN.TestProxy.TestProxyClient client, TestProxyPBN.ForwardRequest request, bool logResponse)
        {
            long latencyInUs = -1;
            try
            {
                var gen0Before = GC.CollectionCount(0);
                var gen1Before = GC.CollectionCount(1);
                var gen2Before = GC.CollectionCount(2);
                var watch = StopwatchWrapper.StartNew();
                watch.Start();
                var response = client.ForwardAsync(request).ResponseAsync.Result;
                watch.Stop();

                if (logResponse)
                {
                    Console.WriteLine($"Response is {JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true })}");
                }

                var gapLatencyInUs = watch.ElapsedInUs - response.routeLatencyInUs;

                /*
                if (gapLatencyInUs > 5000)
                {
                    var gen0After = GC.CollectionCount(0);
                    var gen1After = GC.CollectionCount(1);
                    var gen2After = GC.CollectionCount(2);
#pragma warning disable SA1118 // Parameter should not span multiple lines
                    Console.WriteLine(
                        $"It takes {gapLatencyInUs}us to send {request.ItemRequests.Count} items on network by trace id {request.TraceId}. " +
                        $"ClientSendingTime={watch.StartTime.ToString("HH:mm:ss:fff")}, RouteStartTimeOnServer={new DateTime(response.RouteStartTimeInTicks).ToString("HH:mm:ss:fff")}, " +
                        $"RouteLatencyOnServerInUs={response.RouteLatencyInUs}, RouteClientLatencyInUs={watch.ElapsedInUs}. " +
                        $"Gen0/1/2 Before {gen0Before}/{gen1Before}/{gen2Before} After {gen0After}/{gen1After}/{gen2After}.");
#pragma warning restore SA1118 // Parameter should not span multiple lines
                }
                */

                latencyInUs = watch.ElapsedInUs;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"failed to send a request. Exception : {ex}");
            }

            return latencyInUs;
        }
#else
        private static long RunCore()
        {
            using (var channel = CreateChannel())
            {
                var client = channel.CreateGrpcService<ITestProxy>();
                var request = new ForwardRequest { traceId = Guid.NewGuid().ToString("N") };
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
                    request.itemRequests.Add(new ForwardPerItemRequest(itemId, itemContext));
                }

                var requestContextInfo = SlabAllocator.Rent(RequestContextSize + 3);
                requestContextInfo.Span.Slice(0, RequestContextSize).Fill((byte)'a');
                end.CopyTo(requestContextInfo.Span.Slice(RequestContextSize));
                request.requestContextInfo = requestContextInfo;

                // warm up like establishing connection
                while (true)
                {
                    request.traceId = Guid.NewGuid().ToString("N"); // note: protobuf-net has native Guid=>string support, if needed
                    var ret = SendRequest(client, request, true);
                    if (ret > 0)
                    {
                        break;
                    }

                    Thread.Sleep(1000);
                }

                Console.WriteLine("Ready to send simulated requests");

                var latenciesInUs = new long[10000];
                Array.Clear(latenciesInUs);
                int startIndex = 0;
                int totalCount = 0;
                DateTime startTime = DateTime.Now;
                while (true)
                {
                    totalCount++;
                    request.traceId = Guid.NewGuid().ToString("N");
                    var latencyInUs = SendRequest(client, request, false);
                    if (latencyInUs > 0)
                    {
                        latenciesInUs[startIndex++] = latencyInUs;
                    }

                    if (startIndex == latenciesInUs.Length)
                    {
                        Array.Sort(latenciesInUs);
                        DateTime endTime = DateTime.Now;
                        var qps = totalCount * 1.0 / (endTime - startTime).TotalSeconds;
                        var successRate = latenciesInUs.Length * 1.0 / totalCount;

                        Console.WriteLine($"successful rate {successRate} = {latenciesInUs.Length}/{totalCount}");
                        Console.WriteLine($"qps {qps} = {totalCount}/({endTime.ToString("yyyy-MM-dd HH:mm:ss")} - {startTime.ToString("yyyy-MM-dd HH:mm:ss")})");
                        Console.WriteLine($"min   latency in Us: {latenciesInUs.First()}");
                        Console.WriteLine($"max   latency in Us: {latenciesInUs.Last()}");
                        Console.WriteLine($"50%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.5)]}");
                        Console.WriteLine($"90%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.9)]}");
                        Console.WriteLine($"95%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.95)]}");
                        Console.WriteLine($"99%   latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.99)]}");
                        Console.WriteLine($"99.9% latency in Us: {latenciesInUs[(int)(latenciesInUs.Length * 0.999)]}");

                        startIndex = 0;
                        totalCount = 0;
                        startTime = DateTime.Now;
                        Array.Clear(latenciesInUs);
                    }
                }
                // request.Dispose(); // never reached
            }
        }

        private static long SendRequest(ITestProxy client, ForwardRequest request, bool logResponse)
        {
            long latencyInUs = -1;
            try
            {
                var gen0Before = GC.CollectionCount(0);
                var gen1Before = GC.CollectionCount(1);
                var gen2Before = GC.CollectionCount(2);
                var watch = StopwatchWrapper.StartNew();
                watch.Start();
                var response = client.ForwardAsync(request).Result;
                watch.Stop();

                if (logResponse)
                {
                    // Console.WriteLine($"Response is {JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true })}");
                }

                var gapLatencyInUs = watch.ElapsedInUs - response.routeLatencyInUs;

                /*
                if (gapLatencyInUs > 5000)
                {
                    var gen0After = GC.CollectionCount(0);
                    var gen1After = GC.CollectionCount(1);
                    var gen2After = GC.CollectionCount(2);
#pragma warning disable SA1118 // Parameter should not span multiple lines
                    Console.WriteLine(
                        $"It takes {gapLatencyInUs}us to send {request.itemRequests.Count} items on network by trace id {request.traceId}. " +
                        $"ClientSendingTime={watch.StartTime.ToString("HH:mm:ss:fff")}, RouteStartTimeOnServer={new DateTime(response.routeStartTimeInTicks).ToString("HH:mm:ss:fff")}, " +
                        $"RouteLatencyOnServerInUs={response.routeLatencyInUs}, RouteClientLatencyInUs={watch.ElapsedInUs}. " +
                        $"Gen0/1/2 Before {gen0Before}/{gen1Before}/{gen2Before} After {gen0After}/{gen1After}/{gen2After}.");
#pragma warning restore SA1118 // Parameter should not span multiple lines
                }
                */

                latencyInUs = watch.ElapsedInUs;

                response.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"failed to send a request. Exception : {ex}");
            }

            return latencyInUs;
        }
#endif
    }
}
