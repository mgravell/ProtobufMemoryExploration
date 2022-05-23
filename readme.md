Investigating various things:

- using a struct instead of a class as the nested sub-items in a gRPC hive (one root, thousands of items, each sub-item as a byte buffer)
- using a recyclable byte buffer instead of a per-deserialize byte[] (which is what Google.Protobuf and protobuf-net do, historically)
  - for that buffer: try a wrapper type with recycle callback, vs a custom memory manager; turns out the latter works better
  - using a slab allocator for the byte buffers, because the array-pool was being exhausted very rapidly

Findings from ^^^

protobuf-net can be made basically zero alloc, but the performance vs Google.Protobuf is not quite as good; this is not new to this investigation;
there are some design choices and features that make it hard to tweak this further without an API redesign (sadface); the Google code here
is allocating nearly a MiB per deserialize, so: non-trivial

The gRPC performance (as seen via the test client/server) is worse than the [de]serialization benchmarks would suggest on the surface, so it may
be that the [de]serialization performance is not even key!

Next steps:

1. investigate hacking the byte-buffer recycling hacks into Google.Protobuf (tests running now)
2. investigate hacking the value-type usage into Google.Protobuf (some T : class nuances making this a little awkward; investigating)
3. investigate using a hand-written gRPC stub rather than the protobuf-net.Grpc hooks - see whether we can make things better there