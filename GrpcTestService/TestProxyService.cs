using Grpc.Core;
using ProtoBuf.Grpc;
using System;
using System.Threading.Tasks;
using TestProxyPBN;

namespace GrpcTestService
{
    internal class TestProxyService : ITestProxy
    {
        private static int gen0 = 0;
        private static int gen1 = 0;
        private static int gen2 = 0;

        private const int ExtraResultSize = 32;
        private static Memory<byte> SharedExtraResult = new byte[32];

        public ValueTask<ForwardResponse> ForwardAsync(ForwardRequest request, CallContext context = default)
        {

            /*
            var gen0After = GC.CollectionCount(0);
            var gen1After = GC.CollectionCount(1);
            var gen2After = GC.CollectionCount(2);

            if (gen0After != gen0 || gen1After != gen1 || gen2After != gen2)
            {
                Console.WriteLine(
                    $"CurrentTime={DateTime.Now.ToString("HH:mm:ss:fff")}, TraceId={request.TraceId}" +
                    $"Gen0/1/2 Before {gen0}/{gen1}/{gen2} After {gen0After}/{gen1After}/{gen2After}.");

                gen0 = gen0After;
                gen1 = gen1After;
                gen2 = gen2After;
            }
            */

            var e2eWatch = StopwatchWrapper.StartNew();
            var response = Program.EnableObjectCache ? ObjectCache.GetForwardResponse() : new ForwardResponse();
            foreach (var itemRequest in request.itemRequests)
            {
                response.itemResponses.Add(new ForwardPerItemResponse(100, SharedExtraResult));
            }
            e2eWatch.Stop();
            response.routeLatencyInUs = e2eWatch.ElapsedInUs;
            response.routeStartTimeInTicks = e2eWatch.StartTime.Ticks;

            request.Dispose(); // we can dispose the request now that we're done with it
            if (Program.EnableObjectCache)
            {
                RegisterForDispose(response, context.ServerCallContext);
            }

            return new ValueTask<ForwardResponse>(response);
        }

        private static void RegisterForDispose(ForwardResponse response, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            httpContext.Response.RegisterForDispose(response);
        }
    }
}
