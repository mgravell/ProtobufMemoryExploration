Investigating various things:

- using a struct instead of a class as the nested sub-items in a gRPC hive (one root, thousands of items, each sub-item as a byte buffer)
- using a recyclable byte buffer instead of a per-deserialize byte[] (which is what Google.Protobuf and protobuf-net do, historically)
  - for that buffer: try a wrapper type with recycle callback, vs a custom memory manager; turns out the latter works better
  - using a slab allocator for the byte buffers, because the array-pool was being exhausted very rapidly

Current results - key:

- `PBN` - protobuf-net
- `GPB` / `Google` - Google.Protobuf
  - `_H` the hacked up buffer version of the above
- `MS`: `MemoryStream`
- `BA`: `byte[]`
- `ROM`: `ReadOnlyMemory<byte>`
- `BW`: `BufferWriter<byte>`

Note: the `_H` examples use hacks in Google.Protobuf; this code will not compile/work against the real repo!

Aside: the pattern I'm using here for memory reduction is one I've seen arise multiple times; I've also started a discussion around
normalizing this pattern; no idea where that will go - assume "nowhere" for now.

Serialize:

|                          Method |       Mean |    Error |    StdDev |     Median |  Gen 0 | Allocated |
|-------------------------------- |-----------:|---------:|----------:|-----------:|-------:|----------:|
|       SerializeRequestGoogle_MS |   356.5 us | 13.83 us |  40.34 us |   355.8 us | 0.4883 |   4,248 B |
|     SerializeRequestGoogle_MS_H |   344.1 us |  8.43 us |  24.60 us |   342.4 us | 0.4883 |   4,248 B |
|          SerializeRequestPBN_MS |   530.4 us | 19.05 us |  55.88 us |   531.1 us |      - |       1 B |
|      SerializeResponseGoogle_MS |   192.9 us |  4.79 us |  14.05 us |   191.7 us | 0.4883 |   4,184 B |
|    SerializeResponseGoogle_MS_H |   189.7 us |  5.20 us |  15.17 us |   190.8 us | 0.4883 |   4,184 B |
|         SerializeResponsePBN_MS |   413.6 us | 12.09 us |  35.09 us |   410.7 us |      - |         - |
|   MeasureSerializeRequestGPB_BW |   427.0 us | 13.71 us |  39.78 us |   426.1 us |      - |         - |
| MeasureSerializeRequestGPB_BW_H |   421.0 us | 15.38 us |  44.39 us |   416.9 us |      - |         - |
|   MeasureSerializeRequestPBN_BW | 1,111.5 us | 44.64 us | 130.93 us | 1,073.9 us |      - |       1 B |

Deserialize:

|                         Method |     Mean |    Error |    StdDev |   Median |    Gen 0 |   Gen 1 | Allocated |
|------------------------------- |---------:|---------:|----------:|---------:|---------:|--------:|----------:|
|    DeserializeRequestGoogle_BA | 680.3 us | 43.86 us | 128.64 us | 650.0 us | 121.0938 | 39.0625 | 770,802 B |
|    DeserializeRequestGoogle_MS | 654.5 us | 22.25 us |  64.54 us | 651.2 us | 127.9297 | 63.4766 | 808,305 B |
|  DeserializeRequestGoogle_BA_H | 638.1 us | 27.72 us |  80.86 us | 627.5 us |  54.6875 |  1.9531 | 346,041 B |
|  DeserializeRequestGoogle_MS_H | 688.7 us | 24.31 us |  71.68 us | 668.7 us |  55.6641 | 21.4844 | 350,779 B |
|      DeserializeRequestPBN_ROM | 691.8 us | 15.62 us |  45.30 us | 686.7 us |        - |       - |     114 B |
|       DeserializeRequestPBN_MS | 707.8 us | 15.82 us |  46.15 us | 700.8 us |        - |       - |     138 B |
|   DeserializeResponseGoogle_BA | 428.8 us | 13.52 us |  39.42 us | 428.6 us |  81.5430 | 23.4375 | 513,960 B |
|   DeserializeResponseGoogle_MS | 392.3 us | 13.52 us |  39.21 us | 384.2 us |  82.5195 | 10.2539 | 518,080 B |
| DeserializeResponseGoogle_BA_H | 567.8 us | 28.15 us |  83.01 us | 570.9 us |  37.1094 |  1.9531 | 234,011 B |
| DeserializeResponseGoogle_MS_H | 462.7 us | 18.88 us |  55.37 us | 460.0 us |  37.1094 |  3.9063 | 238,131 B |
|     DeserializeResponsePBN_ROM | 510.0 us | 15.64 us |  44.11 us | 499.3 us |        - |       - |      11 B |
|      DeserializeResponsePBN_MS | 567.5 us | 13.94 us |  40.67 us | 568.1 us |        - |       - |      35 B |

protobuf-net can be made basically zero alloc, but the performance vs Google.Protobuf is not as good - can be double or worse;

The Google code deserialize is allocating nearly a MiB per deserialize; this can be halved by using the hacks to enable buffer management, with
a similar degredation in performance. This suggests that some more attention to the allocator in use may help reduce this? or maybe unavoidable?

In the above Google.Protobuf `_H` tests, we're still allocating objects for the sub values; changing this to `struct` would probably be non-trivial,
but may be worth a second look.

The gRPC performance (as seen via the test client/server) is worse than the [de]serialization benchmarks would suggest on the surface, so it may
be that the [de]serialization performance is not even key!

Using the gRPC client/server implementation provided, a hand-written gRPC stub using the protobuf-net hooks (but not protobuf-net.Grpc) has been
investigated to see if this is relevant; no major difference between that and regular code-first protobuf-net.Grpc

Next steps:

1. investigate the impact of the "bag" object recycler (gRPC root object) - this may be impacting the gRPC performance?
2. investigate improving the custom memory allocator
3. investigate hacking the value-type usage into Google.Protobuf (some T : class nuances making this a little awkward; investigating)

----


gRPC results:

Google protobuf and gRPC (baseline)

Ready to send simulated requests
successful rate 1 = 10000/10000
qps 246.16416959743367 = 10000/(2022-05-23 16:29:16 - 2022-05-23 16:28:35)
min   latency in Us: 2401
max   latency in Us: 164126
50%   latency in Us: 3676
90%   latency in Us: 4720
95%   latency in Us: 5480
99%   latency in Us: 14568
99.9% latency in Us: 27101
successful rate 1 = 10000/10000
qps 264.95911609299725 = 10000/(2022-05-23 16:29:54 - 2022-05-23 16:29:16)
min   latency in Us: 2374
max   latency in Us: 97882
50%   latency in Us: 3500
90%   latency in Us: 4270
95%   latency in Us: 4784
99%   latency in Us: 11976
99.9% latency in Us: 28588

protobuf-net with memory hacks using protobuf-net.Grpc vanilla code-first

Ready to send simulated requests
successful rate 1 = 10000/10000
qps 146.22636179758976 = 10000/(2022-05-23 16:38:21 - 2022-05-23 16:37:12)
min   latency in Us: 3570
max   latency in Us: 63220
50%   latency in Us: 5498
90%   latency in Us: 8899
95%   latency in Us: 18364
99%   latency in Us: 27274
99.9% latency in Us: 37840
successful rate 1 = 10000/10000
qps 158.93389558635883 = 10000/(2022-05-23 16:39:23 - 2022-05-23 16:38:21)
min   latency in Us: 3195
max   latency in Us: 48949
50%   latency in Us: 5086
90%   latency in Us: 7153
95%   latency in Us: 16972
99%   latency in Us: 26875
99.9% latency in Us: 32893

protobuf-net with memory hacks using Grpc.Core.Api and hacked-up client/server stubs (i.e. protobuf-net but not protobuf-net.Grpc)

Ready to send simulated requests
successful rate 1 = 10000/10000
qps 144.15500475622858 = 10000/(2022-05-23 16:51:34 - 2022-05-23 16:50:25)
min   latency in Us: 3294
max   latency in Us: 184347
50%   latency in Us: 5461
90%   latency in Us: 9214
95%   latency in Us: 17590
99%   latency in Us: 27866
99.9% latency in Us: 84714
successful rate 1 = 10000/10000
qps 151.83497878494111 = 10000/(2022-05-23 16:52:40 - 2022-05-23 16:51:34)
min   latency in Us: 3461
max   latency in Us: 59561
50%   latency in Us: 5309
90%   latency in Us: 7808
95%   latency in Us: 17538
99%   latency in Us: 26875
99.9% latency in Us: 35853


---------------------------------------------

Idea: use `Memory<T>` collections (branch: `collections`), growing via array-pool doubling (and copy), and
disabling the object-pool

|                         Method |     Mean |    Error |   StdDev |    Gen 0 |   Gen 1 | Allocated |
|------------------------------- |---------:|---------:|---------:|---------:|--------:|----------:|
|    DeserializeRequestGoogle_BA | 695.6 us | 20.25 us | 59.70 us | 122.0703 | 37.1094 | 770,801 B |
|    DeserializeRequestGoogle_MS | 770.4 us | 18.45 us | 54.12 us | 128.4180 | 63.9648 | 808,305 B |
|  DeserializeRequestGoogle_BA_H | 791.0 us | 23.91 us | 70.13 us |  54.6875 |  1.9531 | 346,041 B |
|  DeserializeRequestGoogle_MS_H | 825.6 us | 25.64 us | 75.61 us |  55.6641 | 21.4844 | 350,780 B |
|      DeserializeRequestPBN_ROM | 837.8 us | 25.61 us | 75.50 us |        - |       - |     178 B |
|       DeserializeRequestPBN_MS | 862.8 us | 24.25 us | 71.50 us |        - |       - |     202 B |
|   DeserializeResponseGoogle_BA | 451.6 us | 11.07 us | 32.12 us |  81.5430 | 23.4375 | 513,961 B |
|   DeserializeResponseGoogle_MS | 481.1 us | 13.80 us | 40.70 us |  82.5195 | 11.7188 | 518,081 B |
| DeserializeResponseGoogle_BA_H | 547.9 us | 10.91 us | 28.93 us |  37.1094 |  0.9766 | 234,011 B |
| DeserializeResponseGoogle_MS_H | 507.7 us | 10.15 us | 27.27 us |  37.5977 |  2.9297 | 238,131 B |
|     DeserializeResponsePBN_ROM | 549.2 us | 10.98 us | 28.92 us |        - |       - |      67 B |
|      DeserializeResponsePBN_MS | 530.3 us | 10.60 us | 27.37 us |        - |       - |      91 B |

Using a minimal object-pool implementation (just a `[ThreadStatic]`)

|                         Method |     Mean |    Error |   StdDev |    Gen 0 |   Gen 1 | Allocated |
|------------------------------- |---------:|---------:|---------:|---------:|--------:|----------:|
|    DeserializeRequestGoogle_BA | 718.8 us | 21.11 us | 61.57 us | 122.0703 | 37.1094 | 770,801 B |
|    DeserializeRequestGoogle_MS | 751.0 us | 16.11 us | 47.26 us | 127.9297 | 63.4766 | 808,305 B |
|  DeserializeRequestGoogle_BA_H | 783.5 us | 25.38 us | 73.23 us |  54.6875 |  1.9531 | 346,041 B |
|  DeserializeRequestGoogle_MS_H | 780.3 us | 17.38 us | 49.31 us |  55.6641 | 21.4844 | 350,779 B |
|      DeserializeRequestPBN_ROM | 997.6 us | 19.43 us | 49.80 us |        - |       - |     114 B |
|       DeserializeRequestPBN_MS | 908.0 us | 27.70 us | 81.68 us |        - |       - |     138 B |
|   DeserializeResponseGoogle_BA | 467.3 us |  9.29 us | 26.94 us |  81.5430 | 23.4375 | 513,960 B |
|   DeserializeResponseGoogle_MS | 450.0 us |  8.67 us | 19.93 us |  82.5195 | 11.7188 | 518,080 B |
| DeserializeResponseGoogle_BA_H | 578.9 us | 13.97 us | 39.63 us |  37.1094 |  1.9531 | 234,011 B |
| DeserializeResponseGoogle_MS_H | 565.5 us | 11.07 us | 29.35 us |  37.5977 |  1.9531 | 238,131 B |
|     DeserializeResponsePBN_ROM | 596.9 us | 13.86 us | 40.86 us |        - |       - |      11 B |
|      DeserializeResponsePBN_MS | 566.5 us | 12.16 us | 35.27 us |        - |       - |      35 B |


