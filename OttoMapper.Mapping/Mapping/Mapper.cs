using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OttoMapper.Mapping
{
    public class Mapper : IMapper
    {
        private readonly MapperConfiguration _config;
        private readonly ConcurrentDictionary<(Type, Type), Func<object, object>> _mapFuncs = new ConcurrentDictionary<(Type, Type), Func<object, object>>();
        private readonly TypedMapCache _typedCache = new TypedMapCache();

        public Mapper(MapperConfiguration config)
        {
            _config = config;
        }

        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (source == null) return default;

            if (_typedCache.TryGet<TSource, TDestination>(out var typed))
            {
                return typed(source);
            }

            var func = GetMapFunc(typeof(TSource), typeof(TDestination));
            return (TDestination)func(source);
        }

        public object Map(object source, Type sourceType, Type destinationType)
        {
            if (source == null) return null;
            var func = GetMapFunc(sourceType, destinationType);
            return func(source);
        }

        internal void PrepareMap(Type sourceType, Type destinationType)
        {
            // ensure compiled map exists
            GetMapFunc(sourceType, destinationType);
        }

        private Func<object, object> GetMapFunc(Type sourceType, Type destinationType)
        {
            return _mapFuncs.GetOrAdd((sourceType, destinationType), _ => CreateMapFunc(sourceType, destinationType));
        }

        private Func<object, object> CreateMapFunc(Type sourceType, Type destinationType)
        {
            var typeMap = _config.GetTypeMap(sourceType, destinationType);

            // Respect typed custom converter if present
            if (typeMap?.TypedCustomConverter != null)
            {
                TryRegisterTypedFromDelegate(sourceType, destinationType, typeMap.TypedCustomConverter);
                // build non-DynamicInvoke wrapper using MappingHelpers.InvokeTypedDelegate<TSource,TDestination>
                try
                {
                    var srcParamObj = Expression.Parameter(typeof(object), "srcObj");
                    var typedConvType = typeMap.TypedCustomConverter.GetType();
                    var typedConst = Expression.Constant(typeMap.TypedCustomConverter, typedConvType);
                    var helper = typeof(MappingHelpers).GetMethod("InvokeTypedDelegate", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(sourceType, destinationType);
                    var call = Expression.Call(helper, Expression.Convert(typedConst, typeof(Func<,>).MakeGenericType(sourceType, destinationType)), srcParamObj);
                    var lambda = Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), srcParamObj);
                    return lambda.Compile();
                }
                catch
                {
                    return src => typeMap.TypedCustomConverter.DynamicInvoke(src)!;
                }
            }

            // Respect CustomConverter: entirely custom mapping (untyped)
            if (typeMap?.CustomConverter != null)
            {
                TryRegisterTypedFromObjectConverter(sourceType, destinationType, typeMap.CustomConverter);
                return typeMap.CustomConverter;
            }

            // collection mapping
            if (MappingHelpers.IsEnumerable(sourceType) && MappingHelpers.IsEnumerable(destinationType))
            {
                var srcElem = MappingHelpers.GetEnumerableElementType(sourceType) ?? typeof(object);
                var dstElem = MappingHelpers.GetEnumerableElementType(destinationType) ?? typeof(object);

                // if we have typed element mapper, use generic MapCollectionGeneric call
                if (_typedCache.TryGet(srcElem, dstElem, out var typedElem))
                {
                    // Build expression-based wrapper Func<object, object> to avoid DynamicInvoke/Activator
                    var srcParamObj = Expression.Parameter(typeof(object), "srcObj");
                    var enumerableType = typeof(IEnumerable<>).MakeGenericType(srcElem);
                    var srcConverted = Expression.Convert(srcParamObj, enumerableType);
                    var typedDelegateType = typeof(Func<,>).MakeGenericType(srcElem, dstElem);
                    var typedConst = Expression.Constant(typedElem, typedDelegateType);

                    Expression finalCollectionExpr;
                    if (destinationType.IsArray)
                    {
                        var mapToArrayMethod = typeof(MappingHelpers).GetMethod("MapToArrayGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                        var call = Expression.Call(mapToArrayMethod, srcConverted, typedConst); // TDestination[]
                        finalCollectionExpr = Expression.Convert(call, typeof(object));
                    }
                    else
                    {
                        var listType = typeof(List<>).MakeGenericType(dstElem);
                        var listCtor = listType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                        if (destinationType.IsAssignableFrom(listType) && listCtor != null)
                        {
                            var mapToListMethod = typeof(MappingHelpers).GetMethod("MapToListGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                            var call = Expression.Call(mapToListMethod, srcConverted, typedConst); // List<dstElem>
                            finalCollectionExpr = Expression.Convert(call, typeof(object));
                        }
                        else
                        {
                            var destCtor = destinationType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                            if (destCtor != null)
                            {
                                // use MapCollectionGeneric to produce IEnumerable<dstElem> then call destination ctor
                                var mapGenericMethod = typeof(MappingHelpers).GetMethod("MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                                var collectionExpr = Expression.Call(mapGenericMethod, srcConverted, typedConst);
                                var newDest = Expression.New(destCtor, collectionExpr);
                                finalCollectionExpr = Expression.Convert(newDest, typeof(object));
                            }
                            else
                            {
                                // fallback: cast IEnumerable<dstElem> to object (e.g., property expects IEnumerable<T>)
                                var mapGenericMethod = typeof(MappingHelpers).GetMethod("MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                                var collectionExpr = Expression.Call(mapGenericMethod, srcConverted, typedConst);
                                finalCollectionExpr = Expression.Convert(collectionExpr, typeof(object));
                            }
                        }
                    }

                    var collWrapperLambda = Expression.Lambda<Func<object, object>>(finalCollectionExpr, srcParamObj);
                    var collWrapperCompiled = collWrapperLambda.Compile();
                    return collWrapperCompiled;
                }

                var elemMapper = GetMapFunc(srcElem, dstElem);
                Func<object, object> wrapper = src => MappingHelpers.MapCollection(src, srcElem, dstElem, elemMapper, destinationType);
                TryRegisterTypedForCollection(sourceType, destinationType, srcElem, dstElem, elemMapper, wrapper);
                return wrapper;
            }

            // Build typed lambda: Func<sourceType, destinationType>
            var srcParamTyped = Expression.Parameter(sourceType, "src");
            var destVar = Expression.Variable(destinationType, "dest");

            // destination creation: use TypedConstructUsing if available else ConstructUsing or default
            Expression assignDestTyped;
            if (typeMap?.TypedConstructUsing != null)
            {
                var typedCtor = typeMap.TypedConstructUsing; // delegate
                var typedConst = Expression.Constant(typedCtor, typedCtor.GetType());
                var invoked = Expression.Invoke(typedConst, srcParamTyped);
                assignDestTyped = Expression.Assign(destVar, Expression.Convert(invoked, destinationType));
            }
            else if (typeMap?.ConstructUsing != null)
            {
                var constructConst = Expression.Constant(typeMap.ConstructUsing, typeof(Func<object, object>));
                var invoked = Expression.Invoke(constructConst, Expression.Convert(srcParamTyped, typeof(object)));
                assignDestTyped = Expression.Assign(destVar, Expression.Convert(invoked, destinationType));
            }
            else
            {
                assignDestTyped = Expression.Assign(destVar, Expression.New(destinationType));
            }

            var blockExpressionsTyped = new List<Expression> { assignDestTyped };

            // For each writable property on destination, try to map from source property of same name
            foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                var srcProp = sourceType.GetProperty(destProp.Name, BindingFlags.Public | BindingFlags.Instance);
                Expression valueExpr = null;

                // try to use typed member resolver
                if (typeMap != null && typeMap.TypedMemberResolvers.TryGetValue(destProp.Name, out var typedResolver))
                {
                    var (srcType, memberType, del) = typedResolver;
                    var delType = del.GetType();
                    var delConst = Expression.Constant(del, delType);
                    // pass the whole source object to the resolver (not the source property)
                    var invoke = Expression.Invoke(delConst, srcParamTyped);
                    valueExpr = Expression.Convert(invoke, destProp.PropertyType);
                }
                else if (typeMap != null && typeMap.MemberResolvers.TryGetValue(destProp.Name, out var resolver))
                {
                    var resolverConst = Expression.Constant(resolver, typeof(Func<object, object>));
                    var invoke = Expression.Invoke(resolverConst, Expression.Convert(srcParamTyped, typeof(object)));
                    valueExpr = Expression.Convert(invoke, destProp.PropertyType);
                }
                else if (srcProp != null && srcProp.CanRead)
                {
                    // nested mapping for complex types
                    if (MappingHelpers.IsEnumerable(srcProp.PropertyType) && MappingHelpers.IsEnumerable(destProp.PropertyType))
                    {
                        var srcElem = MappingHelpers.GetEnumerableElementType(srcProp.PropertyType) ?? typeof(object);
                        var dstElem = MappingHelpers.GetEnumerableElementType(destProp.PropertyType) ?? typeof(object);

                        if (_typedCache.TryGet(srcElem, dstElem, out var typedElem))
                        {
                            // call generic MapCollection using typed element mapper (fast path)
                            var srcAccess = Expression.Property(srcParamTyped, srcProp);
                            var enumerableType = typeof(IEnumerable<>).MakeGenericType(srcElem);
                            var srcConverted = Expression.Convert(srcAccess, enumerableType);
                            var typedDelegateType = typeof(Func<,>).MakeGenericType(srcElem, dstElem);
                            var typedConst = Expression.Constant(typedElem, typedDelegateType);

                            Expression collectionExpr;
                            if (destProp.PropertyType.IsArray)
                            {
                                var mapToArrayMethod = typeof(MappingHelpers).GetMethod("MapToArrayGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                                collectionExpr = Expression.Call(mapToArrayMethod, srcConverted, typedConst);
                                valueExpr = Expression.Convert(collectionExpr, destProp.PropertyType);
                            }
                            else
                            {
                                var listType = typeof(List<>).MakeGenericType(dstElem);
                                var listCtor = listType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                                if (destProp.PropertyType.IsAssignableFrom(listType) && listCtor != null)
                                {
                                    var mapToListMethod = typeof(MappingHelpers).GetMethod("MapToListGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                                    collectionExpr = Expression.Call(mapToListMethod, srcConverted, typedConst);
                                    valueExpr = Expression.Convert(collectionExpr, destProp.PropertyType);
                                }
                                else
                                {
                                    var destCtor = destProp.PropertyType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                                    if (destCtor != null)
                                    {
                                        var mapGenericMethod = typeof(MappingHelpers).GetMethod("MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                                        collectionExpr = Expression.Call(mapGenericMethod, srcConverted, typedConst);
                                        var newDest = Expression.New(destCtor, collectionExpr);
                                        valueExpr = Expression.Convert(newDest, destProp.PropertyType);
                                    }
                                    else
                                    {
                                        var mapGenericMethod = typeof(MappingHelpers).GetMethod("MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(srcElem, dstElem);
                                        collectionExpr = Expression.Call(mapGenericMethod, srcConverted, typedConst);
                                        valueExpr = Expression.Convert(collectionExpr, destProp.PropertyType);
                                    }
                                }
                            }
                        }
                        else
                        {
                            var srcAccess = Expression.Property(srcParamTyped, srcProp);
                            var mapCollectionMethod = typeof(MappingHelpers).GetMethod("MapCollection", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            var mappedCall = Expression.Call(mapCollectionMethod!, Expression.Convert(srcAccess, typeof(object)), Expression.Constant(srcElem), Expression.Constant(dstElem), Expression.Constant(GetMapFunc(srcElem, dstElem)), Expression.Constant(destProp.PropertyType));
                            valueExpr = Expression.Convert(mappedCall, destProp.PropertyType);
                        }
                    }
                    else if (!MappingHelpers.IsEnumerable(srcProp.PropertyType) && !MappingHelpers.IsEnumerable(destProp.PropertyType))
                    {
                        if (srcProp.PropertyType == destProp.PropertyType)
                        {
                            var srcAccess = Expression.Property(srcParamTyped, srcProp);
                            valueExpr = srcAccess;
                        }
                        else
                        {
                            // nested complex object mapping
                            PrepareMap(srcProp.PropertyType, destProp.PropertyType);

                            if (_typedCache.TryGet(srcProp.PropertyType, destProp.PropertyType, out var nestedTypedObj))
                            {
                                var nestedConst = Expression.Constant(nestedTypedObj, nestedTypedObj.GetType());
                                var srcAccess = Expression.Property(srcParamTyped, srcProp);
                                var invoke = Expression.Invoke(nestedConst, srcAccess);
                                valueExpr = Expression.Convert(invoke, destProp.PropertyType);
                            }
                            else
                            {
                                var nestedMapper = GetMapFunc(srcProp.PropertyType, destProp.PropertyType);
                                var srcAccess = Expression.Property(srcParamTyped, srcProp);
                                var nestedConst = Expression.Constant(nestedMapper, typeof(Func<object, object>));
                                var invoke = Expression.Invoke(nestedConst, Expression.Convert(srcAccess, typeof(object)));
                                valueExpr = Expression.Convert(invoke, destProp.PropertyType);
                            }
                        }
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                var bind = Expression.Assign(Expression.Property(destVar, destProp), valueExpr);
                blockExpressionsTyped.Add(bind);
            }

            blockExpressionsTyped.Add(destVar);

            var bodyTyped = Expression.Block(new[] { destVar }, blockExpressionsTyped);

            // compile typed delegate Func<sourceType, destinationType>
            var funcType = typeof(Func<,>).MakeGenericType(sourceType, destinationType);

            Delegate? typedDel = null;
            try
            {
                var typedLambda = Expression.Lambda(funcType, bodyTyped, srcParamTyped);
                typedDel = typedLambda.Compile();

                // register typed delegate into cache
                RegisterTypedDelegate(sourceType, destinationType, typedDel);
            }
            catch
            {
                typedDel = null;
            }

            // create object wrapper
            var sourceParamObj = Expression.Parameter(typeof(object), "srcObj");
            Expression invokeExpr;
            if (typedDel != null)
            {
                var helper = typeof(MappingHelpers).GetMethod("InvokeTypedDelegate", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(sourceType, destinationType);
                var typedConst = Expression.Constant(typedDel, funcType);
                var call = Expression.Call(helper, Expression.Convert(typedConst, typeof(Func<,>).MakeGenericType(sourceType, destinationType)), sourceParamObj);
                invokeExpr = Expression.Convert(call, typeof(object));
            }
            else
            {
                var objSrcCast = Expression.Convert(sourceParamObj, sourceType);
                var destVarObj = Expression.Variable(destinationType, "destObj");

                Expression assignDestObj;
                if (typeMap?.ConstructUsing != null)
                {
                    var constructConst = Expression.Constant(typeMap.ConstructUsing, typeof(Func<object, object>));
                    var invoked = Expression.Invoke(constructConst, sourceParamObj);
                    assignDestObj = Expression.Assign(destVarObj, Expression.Convert(invoked, destinationType));
                }
                else
                {
                    assignDestObj = Expression.Assign(destVarObj, Expression.New(destinationType));
                }

                var blockExprsObj = new List<Expression> { assignDestObj };

                foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!destProp.CanWrite) continue;
                    var srcProp = sourceType.GetProperty(destProp.Name, BindingFlags.Public | BindingFlags.Instance);
                    Expression valueExprObj = null;

                    if (typeMap != null && typeMap.MemberResolvers.TryGetValue(destProp.Name, out var resolver))
                    {
                        var resolverConst = Expression.Constant(resolver, typeof(Func<object, object>));
                        var invoke = Expression.Invoke(resolverConst, sourceParamObj);
                        valueExprObj = Expression.Convert(invoke, destProp.PropertyType);
                    }
                    else if (srcProp != null && srcProp.CanRead)
                    {
                        if (MappingHelpers.IsEnumerable(srcProp.PropertyType) && MappingHelpers.IsEnumerable(destProp.PropertyType))
                        {
                            var srcElem = MappingHelpers.GetEnumerableElementType(srcProp.PropertyType) ?? typeof(object);
                            var dstElem = MappingHelpers.GetEnumerableElementType(destProp.PropertyType) ?? typeof(object);
                            var elemMapper = GetMapFunc(srcElem, dstElem);
                            var srcAccess = Expression.Property(Expression.Convert(sourceParamObj, sourceType), srcProp);
                            var mapCollectionMethod = typeof(MappingHelpers).GetMethod("MapCollection", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            var mappedCall = Expression.Call(mapCollectionMethod!, Expression.Convert(srcAccess, typeof(object)), Expression.Constant(srcElem), Expression.Constant(dstElem), Expression.Constant(elemMapper), Expression.Constant(destProp.PropertyType));
                            valueExprObj = Expression.Convert(mappedCall, destProp.PropertyType);
                        }
                        else if (!MappingHelpers.IsEnumerable(srcProp.PropertyType) && !MappingHelpers.IsEnumerable(destProp.PropertyType))
                        {
                            if (srcProp.PropertyType == destProp.PropertyType)
                            {
                                var srcAccess = Expression.Property(Expression.Convert(sourceParamObj, sourceType), srcProp);
                                valueExprObj = srcAccess;
                            }
                            else
                            {
                                var nestedMapper = GetMapFunc(srcProp.PropertyType, destProp.PropertyType);
                                var srcAccess = Expression.Property(Expression.Convert(sourceParamObj, sourceType), srcProp);
                                var nestedConst = Expression.Constant(nestedMapper, typeof(Func<object, object>));
                                var invoke = Expression.Invoke(nestedConst, Expression.Convert(srcAccess, typeof(object)));
                                valueExprObj = Expression.Convert(invoke, destProp.PropertyType);
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    var bind = Expression.Assign(Expression.Property(destVarObj, destProp), valueExprObj);
                    blockExprsObj.Add(bind);
                }

                blockExprsObj.Add(destVarObj);
                var bodyObj = Expression.Block(new[] { destVarObj }, blockExprsObj);
                invokeExpr = Expression.Convert(bodyObj, typeof(object));
            }

            var wrapperLambda = Expression.Lambda<Func<object, object>>(invokeExpr, sourceParamObj);
            var wrapperCompiled = wrapperLambda.Compile();

            return wrapperCompiled;
        }

        private void RegisterTypedDelegate(Type sourceType, Type destinationType, Delegate typedDel)
        {
            try
            {
                var setMethod = typeof(TypedMapCache).GetMethod("Set")!.MakeGenericMethod(sourceType, destinationType);
                setMethod.Invoke(_typedCache, new object[] { typedDel });
            }
            catch { }
        }

        private void TryRegisterTypedFromDelegate(Type sourceType, Type destinationType, Delegate del)
        {
            try
            {
                var setMethod = typeof(TypedMapCache).GetMethod("Set")!.MakeGenericMethod(sourceType, destinationType);
                setMethod.Invoke(_typedCache, new object[] { del });
            }
            catch { }
        }

        private void TryRegisterTypedFromObjectConverter(Type sourceType, Type destinationType, Func<object, object> converter)
        {
            // wrap converter into typed func if possible
            try
            {
                var funcType = typeof(Func<,>).MakeGenericType(sourceType, destinationType);
                // build delegate: (TSource s) => (TDestination)converter((object)s)
                var srcParam = Expression.Parameter(sourceType, "s");
                var convConst = Expression.Constant(converter, typeof(Func<object, object>));
                var call = Expression.Invoke(convConst, Expression.Convert(srcParam, typeof(object)));
                var body = Expression.Convert(call, destinationType);
                var lambda = Expression.Lambda(funcType, body, srcParam);
                var del = lambda.Compile();
                RegisterTypedDelegate(sourceType, destinationType, del);
            }
            catch { }
        }

        private void TryRegisterTypedForCollection(Type sourceType, Type destinationType, Type srcElem, Type dstElem, Func<object, object> elemMapper, Func<object, object> wrapper)
        {
            try
            {
                var method = typeof(Mapper).GetMethod(nameof(WrapTypedCollection), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(sourceType, destinationType, srcElem, dstElem);
                method.Invoke(this, new object[] { elemMapper, wrapper });
            }
            catch { }
        }

        private void WrapTypedCollection<TSource, TDestination, TSrcElem, TDstElem>(Func<object, object> elemMapper, Func<object, object> wrapper)
        {
            Func<TSource, TDestination> typed = src => (TDestination)wrapper(src!);
            _typedCache.Set(typed);
        }
    }
}