﻿// <auto-generated>
//   This file was generated by a tool; you should avoid making direct changes.
//   Consider using 'partial classes' to extend these types
//   Input: test_proxy.proto
// </auto-generated>


#region Designer generated code
#pragma warning disable CS0612, CS0618, CS1591, CS3021, IDE0079, IDE1006, RCS1036, RCS1057, RCS1085, RCS1192

using System;

namespace TestProxyPBN
{
    // desirable: [global::System.Runtime.CompilerServices.SkipLocalsInit]
    internal sealed class CustomTypeModel : global::ProtoBuf.Meta.TypeModel
    {
        private CustomTypeModel() { }
        internal static global::ProtoBuf.Meta.TypeModel Instance { get; } = new CustomTypeModel();

        protected override global::ProtoBuf.Serializers.ISerializer<T> GetSerializer<T>()
            => global::ProtoBuf.Serializers.SerializerCache.Get<SomeSerializer, T>();
    }
    // desirable: [global::System.Runtime.CompilerServices.SkipLocalsInit]
    internal sealed partial class SomeSerializer :
        global::ProtoBuf.Serializers.ISerializer<ForwardPerItemRequest>, global::ProtoBuf.Serializers.ISerializer<ForwardPerItemResponse>,
        global::ProtoBuf.Serializers.ISerializer<ForwardRequest>, global::ProtoBuf.Serializers.ISerializer<ForwardResponse>,
        global::ProtoBuf.Serializers.IMeasuringSerializer<ForwardPerItemRequest>
    {
        // handles all message types; if we need a more specific override: can use explicit implementation
        public global::ProtoBuf.Serializers.SerializerFeatures Features => global::ProtoBuf.Serializers.SerializerFeatures.CategoryMessage | global::ProtoBuf.Serializers.SerializerFeatures.WireTypeString;

        ForwardPerItemResponse global::ProtoBuf.Serializers.ISerializer<ForwardPerItemResponse>.Read(ref global::ProtoBuf.ProtoReader.State state, ForwardPerItemResponse value)
        {
            Merge(ref state, ref value);
            return value;
        }
        static SomeSerializer()
        {
            s_memoryConverter = global::ProtoBuf.Serializers.DefaultMemoryConverter<byte>.Instance;
            GetMemoryConverter(ref s_memoryConverter); // invite customization from partial
            s_memoryConverter ??= global::ProtoBuf.Serializers.DefaultMemoryConverter<byte>.Instance; // ensure not null
        }

        private static readonly global::ProtoBuf.Serializers.IMemoryConverter<global::System.Memory<byte>, byte> s_memoryConverter;
        static partial void GetMemoryConverter(ref global::ProtoBuf.Serializers.IMemoryConverter<global::System.Memory<byte>, byte> value);
        private static void Merge(ref global::ProtoBuf.ProtoReader.State state, ref ForwardPerItemResponse value)
        {
            int field;
            var (_1, _2) = (value.Result, value.extraResult);
            while ((field = state.ReadFieldHeader()) > 0)
            {
                switch (field)
                {
                    case 1:
                        _1 = state.ReadSingle();
                        break;
                    case 2:
                        _2 = state.AppendBytes(_2, s_memoryConverter);
                        break;
                    default:
                        state.SkipField();
                        break;
                }
            }
            value = new ForwardPerItemResponse(_1, _2);
        }

        ForwardPerItemRequest global::ProtoBuf.Serializers.ISerializer<ForwardPerItemRequest>.Read(ref global::ProtoBuf.ProtoReader.State state, ForwardPerItemRequest value)
        {
            Merge(ref state, ref value);
            return value;
        }

        private static void Merge(ref global::ProtoBuf.ProtoReader.State state, ref ForwardPerItemRequest value)
        {
            int field;
            var (_1, _2) = (value.itemId, value.itemContext);
            while ((field = state.ReadFieldHeader()) > 0)
            {
                switch (field)
                {
                    case 1:
                        _1 = state.AppendBytes(_1, s_memoryConverter);
                        break;
                    case 2:
                        _2 = state.AppendBytes(_2, s_memoryConverter);
                        break;
                    default:
                        state.SkipField();
                        break;
                }
            }
            value = new ForwardPerItemRequest(_1, _2);
        }

        void global::ProtoBuf.Serializers.ISerializer<ForwardPerItemResponse>.Write(ref global::ProtoBuf.ProtoWriter.State state, ForwardPerItemResponse value)
        {
            if (value.Result != 0)
            {
                state.WriteFieldHeader(1, global::ProtoBuf.WireType.Fixed32);
                state.WriteSingle(value.Result);
            }
            if (!value.extraResult.IsEmpty)
            {
                state.WriteFieldHeader(2, global::ProtoBuf.WireType.String);
                state.WriteBytes(value.extraResult, s_memoryConverter);
            }
        }

        int global::ProtoBuf.Serializers.IMeasuringSerializer<ForwardPerItemRequest>.Measure(global::ProtoBuf.ISerializationContext context, global::ProtoBuf.WireType wireType, ForwardPerItemRequest value)
        {
            throw new NotImplementedException();
        }

        void global::ProtoBuf.Serializers.ISerializer<ForwardPerItemRequest>.Write(ref global::ProtoBuf.ProtoWriter.State state, ForwardPerItemRequest value)
        {
            if (!value.itemId.IsEmpty)
            {
                state.WriteFieldHeader(1, global::ProtoBuf.WireType.String);
                state.WriteBytes(value.itemId, s_memoryConverter);
            }
            if (!value.itemContext.IsEmpty)
            {
                state.WriteFieldHeader(2, global::ProtoBuf.WireType.String);
                state.WriteBytes(value.itemContext, s_memoryConverter);
            }
        }

        ForwardRequest global::ProtoBuf.Serializers.ISerializer<ForwardRequest>.Read(ref global::ProtoBuf.ProtoReader.State state, ForwardRequest value)
        {
            value ??= global::GrpcTestService.Program.EnableObjectCache ? GrpcTestService.ObjectCache.GetForwardRequest() : new();
            int field;
            while ((field = state.ReadFieldHeader()) > 0)
            {
                switch (field)
                {
                    case 1:
                        value.traceId = state.ReadString();
                        continue;
                    case 2:
                        // global::ProtoBuf.Serializers.RepeatedSerializer.CreateList<ForwardPerItemRequest>().ReadRepeated(ref state, global::ProtoBuf.Serializers.SerializerFeatures.OptionPackedDisabled | global::ProtoBuf.Serializers.SerializerFeatures.WireTypeString, value.itemRequests, this);
                        // manual unroll of ReadRepeated logic, optimizing for this case
                        var list = value.itemRequests;
                        do
                        {
                            var tok = state.StartSubItem();
                            ForwardPerItemRequest subItem = default;
                            Merge(ref state, ref subItem);
                            list.Add(subItem);
                            state.EndSubItem(tok);
                        }
                        while (state.TryReadFieldHeader(2));
                        continue;
                    case 3:
                        value.requestContextInfo = state.AppendBytes(value.requestContextInfo, s_memoryConverter);
                        continue;
                    default:
                        state.AppendExtensionData(value);
                        continue;
                }
            }
            return value;
        }

        void global::ProtoBuf.Serializers.ISerializer<ForwardRequest>.Write(ref global::ProtoBuf.ProtoWriter.State state, ForwardRequest value)
        {
            state.WriteString(1, value.traceId);
            global::ProtoBuf.Serializers.RepeatedSerializer.CreateList<ForwardPerItemRequest>().WriteRepeated(ref state, 2, global::ProtoBuf.Serializers.SerializerFeatures.OptionPackedDisabled | global::ProtoBuf.Serializers.SerializerFeatures.WireTypeString, value.itemRequests, this);
            if (!value.requestContextInfo.IsEmpty)
            {
                state.WriteFieldHeader(3, global::ProtoBuf.WireType.String);
                state.WriteBytes(value.requestContextInfo, s_memoryConverter);
            }
            state.AppendExtensionData(value);
        }

        ForwardResponse global::ProtoBuf.Serializers.ISerializer<ForwardResponse>.Read(ref global::ProtoBuf.ProtoReader.State state, ForwardResponse value)
        {
            value ??= global::GrpcTestService.Program.EnableObjectCache ? GrpcTestService.ObjectCache.GetForwardResponse() : new();
            int field;
            while ((field = state.ReadFieldHeader()) > 0)
            {
                switch (field)
                {
                    case 1:
                        // global::ProtoBuf.Serializers.RepeatedSerializer.CreateList<ForwardPerItemResponse>().ReadRepeated(ref state, global::ProtoBuf.Serializers.SerializerFeatures.OptionPackedDisabled | global::ProtoBuf.Serializers.SerializerFeatures.WireTypeString, value.itemResponses, this);
                        // manual unroll of ReadRepeated logic, optimizing for this case
                        var list = value.itemResponses;
                        do
                        {
                            var tok = state.StartSubItem();
                            ForwardPerItemResponse subItem = default;
                            Merge(ref state, ref subItem);
                            list.Add(subItem);
                            state.EndSubItem(tok);
                        }
                        while (state.TryReadFieldHeader(1));
                        continue;
                    case 2:
                        value.routeLatencyInUs = state.ReadInt64();
                        continue;
                    case 3:
                        value.routeStartTimeInTicks = state.ReadInt64();
                        continue;
                    default:
                        state.AppendExtensionData(value);
                        continue;
                }
            }
            return value;
        }

        void global::ProtoBuf.Serializers.ISerializer<ForwardResponse>.Write(ref global::ProtoBuf.ProtoWriter.State state, ForwardResponse value)
        {
            global::ProtoBuf.Serializers.RepeatedSerializer.CreateList<ForwardPerItemResponse>().WriteRepeated(ref state, 1, global::ProtoBuf.Serializers.SerializerFeatures.OptionPackedDisabled | global::ProtoBuf.Serializers.SerializerFeatures.WireTypeString, value.itemResponses, this);
            if (value.routeLatencyInUs != 0)
            {
                state.WriteFieldHeader(2, ProtoBuf.WireType.Varint);
                state.WriteInt64(value.routeLatencyInUs);
            }
            if (value.routeStartTimeInTicks != 0)
            {
                state.WriteFieldHeader(3, ProtoBuf.WireType.Varint);
                state.WriteInt64(value.routeStartTimeInTicks);
            }
            state.AppendExtensionData(value);
        }
    }


    // ----

    [global::ProtoBuf.ProtoContract(Serializer = typeof(SomeSerializer))]
    public readonly partial struct ForwardPerItemRequest /* : global::ProtoBuf.IExtensible, */
    {
        /*
        private global::ProtoBuf.IExtension __pbn__extensionData;
        global::ProtoBuf.IExtension global::ProtoBuf.IExtensible.GetExtensionObject(bool createIfMissing)
            => global::ProtoBuf.Extensible.GetExtensionObject(ref __pbn__extensionData, createIfMissing);
        */

        public ForwardPerItemRequest(global::System.Memory<byte> itemId, global::System.Memory<byte> itemContext)
        {
            this.itemId = itemId;
            this.itemContext = itemContext;
        }

        [global::ProtoBuf.ProtoMember(1)]
        public global::System.Memory<byte> itemId { get; }

        [global::ProtoBuf.ProtoMember(2)]
        public global::System.Memory<byte> itemContext { get; }
    }

    [global::ProtoBuf.ProtoContract(Serializer = typeof(SomeSerializer))]
    public partial class ForwardRequest : global::ProtoBuf.IExtensible
    {
        private global::ProtoBuf.IExtension __pbn__extensionData;
        global::ProtoBuf.IExtension global::ProtoBuf.IExtensible.GetExtensionObject(bool createIfMissing)
            => global::ProtoBuf.Extensible.GetExtensionObject(ref __pbn__extensionData, createIfMissing);

        [global::ProtoBuf.ProtoMember(1)]
        [global::System.ComponentModel.DefaultValue("")]
        public string traceId { get; set; } = "";

        [global::ProtoBuf.ProtoMember(2)]
        public global::System.Collections.Generic.List<ForwardPerItemRequest> itemRequests { get; } = new global::System.Collections.Generic.List<ForwardPerItemRequest>();

        [global::ProtoBuf.ProtoMember(3)]
        public global::System.Memory<byte> requestContextInfo { get; set; }
    }

    [global::ProtoBuf.ProtoContract(Serializer = typeof(SomeSerializer))]
    public readonly partial struct ForwardPerItemResponse /* : global::ProtoBuf.IExtensible, */
    {
        /*
        private global::ProtoBuf.IExtension __pbn__extensionData;
        global::ProtoBuf.IExtension global::ProtoBuf.IExtensible.GetExtensionObject(bool createIfMissing)
            => global::ProtoBuf.Extensible.GetExtensionObject(ref __pbn__extensionData, createIfMissing);
        */

        public ForwardPerItemResponse(float result, global::System.Memory<byte> extraResult)
        {
            this.Result = result;
            this.extraResult = extraResult;
        }

        [global::ProtoBuf.ProtoMember(1, Name = @"result")]
        public float Result { get; }

        [global::ProtoBuf.ProtoMember(2)]
        public global::System.Memory<byte> extraResult { get; }
    }

    [global::ProtoBuf.ProtoContract(Serializer = typeof(SomeSerializer))]
    public partial class ForwardResponse : global::ProtoBuf.IExtensible
    {
        private global::ProtoBuf.IExtension __pbn__extensionData;
        global::ProtoBuf.IExtension global::ProtoBuf.IExtensible.GetExtensionObject(bool createIfMissing)
            => global::ProtoBuf.Extensible.GetExtensionObject(ref __pbn__extensionData, createIfMissing);

        [global::ProtoBuf.ProtoMember(1)]
        public global::System.Collections.Generic.List<ForwardPerItemResponse> itemResponses { get; } = new global::System.Collections.Generic.List<ForwardPerItemResponse>();

        [global::ProtoBuf.ProtoMember(2)]
        public long routeLatencyInUs { get; set; }

        [global::ProtoBuf.ProtoMember(3)]
        public long routeStartTimeInTicks { get; set; }

    }

#if !NOGRPC
    [global::ProtoBuf.Grpc.Configuration.Service(@"TestProxyPkg.TestProxy")]
    public partial interface ITestProxy
    {
        global::System.Threading.Tasks.ValueTask<ForwardResponse> ForwardAsync(ForwardRequest value, global::ProtoBuf.Grpc.CallContext context = default);
    }
#endif
}

#pragma warning restore CS0612, CS0618, CS1591, CS3021, IDE0079, IDE1006, RCS1036, RCS1057, RCS1085, RCS1192
#endregion