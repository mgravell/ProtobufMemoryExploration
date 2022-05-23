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

Next steps:

1. investigate using a hand-written gRPC stub rather than the protobuf-net.Grpc hooks - see whether we can make things better there
2. investigate hacking the value-type usage into Google.Protobuf (some T : class nuances making this a little awkward; investigating)
3. investigate improving the custom memory allocator