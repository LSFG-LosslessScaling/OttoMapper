using System;
using System.Collections.Concurrent;

namespace OttoMapper.Mapping
{
    internal class TypedMapCache
    {
        private readonly ConcurrentDictionary<(Type, Type), object> _typedFuncs = new ConcurrentDictionary<(Type, Type), object>();

        public void Set<TSource, TDestination>(Func<TSource, TDestination> func)
        {
            _typedFuncs[(typeof(TSource), typeof(TDestination))] = func;
        }

        public bool TryGet<TSource, TDestination>(out Func<TSource, TDestination> func)
        {
            if (_typedFuncs.TryGetValue((typeof(TSource), typeof(TDestination)), out var obj) && obj is Func<TSource, TDestination> f)
            {
                func = f;
                return true;
            }

            func = default!;
            return false;
        }

        public bool TryGet(Type source, Type destination, out object func)
        {
            return _typedFuncs.TryGetValue((source, destination), out func);
        }
    }
}
