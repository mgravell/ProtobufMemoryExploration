#define BDN

using System;
using System.Runtime.CompilerServices;

[module: SkipLocalsInit]

namespace GrpcTestService; // for shared namespace just for code simplicity

static class Program
{
    public const bool EnableObjectCache = true;

    static void Main(string[] args)
    {
#if RELEASE && BDN

        BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
#else
        var obj = new SerializeBenchmarks();
        obj.Setup();
        for (int i = 0; i < 1; i++)
        {
            if ((i % 100) == 0) System.Console.Write(".");
            //obj.DeserializeRequestPBN_ROM();
            //obj.DeserializeResponsePBN_ROM();

            //obj.DeserializeRequestGoogle_BA();
            //obj.DeserializeResponseGoogle_BA();
            //obj.MeasureSerializeRequestPBN_BW();
            //obj.DeserializeRequestGoogle_MS_H();
            //obj.DeserializeResponseGoogle_MS_H();
            //obj.DeserializeHandCrankedRequest_BA();
            //obj.DeserializeHandCrankedResponse_BA();

            Console.WriteLine("a");
            System.Console.WriteLine(obj.MeasureRequestGoogle());
            System.Console.WriteLine(obj.MeasureRequestHandCranked());

            System.Console.WriteLine(obj.MeasureResponseGoogle());
            System.Console.WriteLine(obj.MeasureResponseHandCranked());
        }
#endif
    }
}
