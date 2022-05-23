using Google.Protobuf;
using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrpcTestService
{
    internal class TestProxyService : TestProxy.TestProxy.TestProxyBase
    {
        private static int gen0 = 0;
        private static int gen1 = 0;
        private static int gen2 = 0;

        private const int ExtraResultSize = 32;
        private static ByteString extraResult = ByteString.CopyFrom(new string('b', ExtraResultSize), System.Text.Encoding.ASCII);
        public override Task<TestProxy.ForwardResponse> Forward(TestProxy.ForwardRequest request, ServerCallContext context)
        {
            var gen0After = GC.CollectionCount(0);
            var gen1After = GC.CollectionCount(1);
            var gen2After = GC.CollectionCount(2);

            /*
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
            var response = new TestProxy.ForwardResponse();
            foreach (var itemRequest in request.ItemRequests)
            {
                var itemResponse = new TestProxy.ForwardPerItemResponse();
                itemResponse.Result = 100;
                itemResponse.ExtraResult = extraResult;
                response.ItemResponses.Add(itemResponse);
            }
            e2eWatch.Stop();
            response.RouteLatencyInUs = e2eWatch.ElapsedInUs;
            response.RouteStartTimeInTicks = e2eWatch.StartTime.Ticks;
            return Task.FromResult(response);
        }
    }
}
