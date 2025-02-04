using System;
using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion.Interception;
using Stl.Serialization;
using Stl.Text;

namespace Stl.Fusion.Swapping
{
    public abstract class SwapServiceBase : ISwapService
    {
        protected Func<IUtf16Serializer<object>> SerializerFactory { get; set; } = null!;

        protected SwapServiceBase() { }
        protected SwapServiceBase(Func<IUtf16Serializer<object>> serializerFactory)
            => SerializerFactory = serializerFactory;

        public async ValueTask<IResult?> Load(
            (ComputeMethodInput Input, LTag Version) key,
            CancellationToken cancellationToken = default)
        {
            var serializedKey = SerializeKey(key.Input, key.Version);
            var data = await Load(serializedKey, cancellationToken).ConfigureAwait(false);
            if (data == null)
                return null;
            return SerializerFactory.Invoke().Reader.Read(data) as IResult;
        }

        public async ValueTask Store(
            (ComputeMethodInput Input, LTag Version) key, IResult value,
            CancellationToken cancellationToken = default)
        {
            var serializedKey = SerializeKey(key.Input, key.Version);
            if (await Renew(serializedKey, cancellationToken).ConfigureAwait(false))
                return;
            var data = SerializerFactory.Invoke().Writer.Write(value);
            await Store(serializedKey, data, cancellationToken).ConfigureAwait(false);
        }

        // Protected methods

        protected abstract ValueTask<string?> Load(string key, CancellationToken cancellationToken);
        protected abstract ValueTask<bool> Renew(string key, CancellationToken cancellationToken);
        protected abstract ValueTask Store(string key, string value, CancellationToken cancellationToken);

        protected virtual string SerializeKey(ComputeMethodInput input, LTag version)
        {
            using var f = ListFormat.Default.CreateFormatter();
            var method = input.Method;
            f.Append(method.InvocationTargetHandler.ToStringFunc.Invoke(input.Target));
            f.Append(version.ToString());
            var arguments = input.Arguments;
            for (var i = 0; i < method.ArgumentHandlers.Length; i++) {
                var handler = method.ArgumentHandlers[i];
                f.Append(handler.ToStringFunc.Invoke(arguments[i]));
            }
            f.AppendEnd();
            return f.Output;
        }
    }
}
