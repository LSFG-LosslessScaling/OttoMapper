using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OttoMapper.Mapping
{
    internal static class MappingHelpers
    {
        public static bool IsEnumerable(Type type)
        {
            if (type == typeof(string)) return false;
            return typeof(IEnumerable).IsAssignableFrom(type);
        }

        public static Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition()))
            {
                return type.GetGenericArguments()[0];
            }

            var intf = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return intf?.GetGenericArguments()[0];
        }

        public static object MapCollection(object sourceCollection, Type sourceElementType, Type destElementType, Func<object, object> elementMapper, Type destCollectionType)
        {
            if (sourceCollection == null) return null!;

            var sourceEnum = (IEnumerable)sourceCollection;

            // try to get count
            int count = -1;
            if (sourceCollection is ICollection coll)
            {
                count = coll.Count;
            }

            var listType = typeof(List<>).MakeGenericType(destElementType);
            IList list;

            if (count > 0)
            {
                try
                {
                    var ctor = listType.GetConstructor(new[] { typeof(int) });
                    if (ctor != null)
                    {
                        list = (IList)ctor.Invoke(new object[] { count });
                    }
                    else
                    {
                        list = (IList)Activator.CreateInstance(listType);
                    }
                }
                catch
                {
                    list = (IList)Activator.CreateInstance(listType);
                }
            }
            else
            {
                list = (IList)Activator.CreateInstance(listType);
            }

            foreach (var item in sourceEnum)
            {
                var mapped = elementMapper(item);
                list.Add(mapped);
            }

            if (destCollectionType.IsArray)
            {
                var arr = Array.CreateInstance(destElementType, list.Count);
                list.CopyTo(arr, 0);
                return arr;
            }

            if (destCollectionType.IsAssignableFrom(listType)) return list;

            try
            {
                var constructed = Activator.CreateInstance(destCollectionType, list);
                if (constructed != null) return constructed;
            }
            catch { }

            return list;
        }

        public static IEnumerable<TDestination> MapCollectionGeneric<TSource, TDestination>(IEnumerable<TSource> source, Func<TSource, TDestination> mapper)
        {
            if (source == null) yield break;
            foreach (var s in source)
            {
                yield return mapper(s);
            }
        }

        public static TDestination[] MapToArrayGeneric<TSource, TDestination>(IEnumerable<TSource> source, Func<TSource, TDestination> mapper)
        {
            if (source == null) return Array.Empty<TDestination>();
            if (source is ICollection<TSource> coll)
            {
                if (coll.Count == 0) return Array.Empty<TDestination>();
                var arr = new TDestination[coll.Count];
                int i = 0;
                foreach (var s in coll)
                {
                    arr[i++] = mapper(s);
                }
                return arr;
            }
            var list = new List<TDestination>();
            foreach (var s in source)
            {
                list.Add(mapper(s));
            }
            return list.ToArray();
        }

        public static List<TDestination> MapToListGeneric<TSource, TDestination>(IEnumerable<TSource> source, Func<TSource, TDestination> mapper)
        {
            if (source == null) return new List<TDestination>();
            if (source is ICollection<TSource> coll)
            {
                var list = new List<TDestination>(coll.Count);
                foreach (var s in coll)
                {
                    list.Add(mapper(s));
                }
                return list;
            }
            var res = new List<TDestination>();
            foreach (var s in source)
            {
                res.Add(mapper(s));
            }
            return res;
        }

        public static object InvokeTypedDelegate<TSource, TDestination>(Func<TSource, TDestination> del, object src)
        {
            return del((TSource)src)!;
        }
    }
}
