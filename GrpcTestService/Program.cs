#define HACKUP

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Server;
using ProtoBuf.Meta;
using System.Net;
using System.Reflection;
using TestProxyPBN;

namespace GrpcTestService
{
    internal class Program
    {
        public const bool EnableObjectCache = true;

        internal class Startup
        {
            private const int GrpcMaxReceiveMessageSizeInMB = 1024 * 1024;

            public void ConfigureServices(IServiceCollection services)
            {
#if HACKUP
                services.AddGrpc(options =>
                {
                    options.MaxReceiveMessageSize = GrpcMaxReceiveMessageSizeInMB;
                });
#else

                services.AddCodeFirstGrpc(options =>
                {
                    options.MaxReceiveMessageSize = GrpcMaxReceiveMessageSizeInMB;
                });
#endif

                // Filter out Asp Net Core's default Info logging and just display warnings
                services.AddLogging(
                builder =>
                {
                    builder.AddFilter("Microsoft", Microsoft.Extensions.Logging.LogLevel.Warning)
                            .AddConsole();
                });
            }

            // This code configures Web API. The Startup class is specified as a type
            // parameter in the WebAppBuilder Start method.
            public virtual void Configure(IApplicationBuilder appBuilder)
            {
                appBuilder.UseRouting();
                appBuilder.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<TestProxyService>();
                });
            }
        }

        private static void Main(string[] args)
        {
            ServicePointManager.DefaultConnectionLimit = 10000;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.MaxServicePointIdleTime = 3600 * 1000;

            // Disable server certificate validation
            ServicePointManager.ServerCertificateValidationCallback += (o, c, ch, er) => true;

            if (EnableObjectCache)
            {
                RuntimeTypeModel.Default.Add<ForwardRequest>().SetFactory(typeof(ObjectCache).GetMethod(nameof(ObjectCache.GetForwardRequest), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                // RuntimeTypeModel.Default.Add<ForwardPerItemRequest>().SetFactory(typeof(ObjectCache).GetMethod(nameof(ObjectCache.GetForwardPerItemRequest), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            }

            var webHost = WebHost.CreateDefaultBuilder()
                       .UseStartup<Startup>()
                       .UseKestrel(options =>
                       {
                           options.Listen(
                               IPAddress.Any,
                               81,
                               listenOptions =>
                               {
                                   listenOptions.Protocols = HttpProtocols.Http2;
                                   listenOptions.KestrelServerOptions.Limits.Http2.InitialConnectionWindowSize = 8 * 1024 * 1024;
                                   listenOptions.KestrelServerOptions.Limits.Http2.InitialStreamWindowSize = 1 * 1024 * 1024;
                               });
                       })
                       .Build();

            webHost.Run();
        }
    }
}
