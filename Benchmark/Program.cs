#define BDN

using System.Runtime.CompilerServices;

[module: SkipLocalsInit]

namespace GrpcTestService; // for shared namespace just for code simplicity

static class Program
{
    public const bool EnableObjectCache = false;

    static void Main(string[] args)
    {
#if RELEASE && BDN

        BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
#else
        var obj = new DeserializeBenchmarks();
        obj.Setup();
        for (int i = 0; i < 50000; i++)
        {
            if ((i % 100) == 0) System.Console.Write(".");
            //obj.DeserializeRequestPBN_ROM();
            //obj.DeserializeResponsePBN_ROM();

            //obj.DeserializeRequestGoogle_BA();
            //obj.DeserializeResponseGoogle_BA();
            //obj.MeasureSerializeRequestPBN_BW();
            obj.DeserializeRequestGoogle_MS_H();
            obj.DeserializeResponseGoogle_MS_H();
        }
#endif
    }
}
