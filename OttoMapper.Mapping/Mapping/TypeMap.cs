using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace OttoMapper.Mapping
{
    public class TypeMap
    {
        public Type SourceType { get; }
        public Type DestinationType { get; }
        public Dictionary<string, Func<object, object>> MemberResolvers { get; } = new Dictionary<string, Func<object, object>>();

        // optional untyped converters
        public Func<object, object>? CustomConverter { get; set; }
        public Func<object, object>? ConstructUsing { get; set; }

        // typed resolvers and converters (best-effort)
        public Dictionary<string, (Type srcType, Type memberType, Delegate resolver)> TypedMemberResolvers { get; } = new Dictionary<string, (Type, Type, Delegate)>();
        public Delegate? TypedCustomConverter { get; set; }
        public Delegate? TypedConstructUsing { get; set; }

        public TypeMap(Type source, Type destination)
        {
            SourceType = source;
            DestinationType = destination;
        }
    }

    public class MappingExpression<TSource, TDestination> : IMappingExpression<TSource, TDestination>
    {
        private readonly TypeMap _typeMap;

        public MappingExpression(TypeMap typeMap)
        {
            _typeMap = typeMap;
        }

        public IMappingExpression<TSource, TDestination> ForMember<TMember>(System.Linq.Expressions.Expression<Func<TDestination, TMember>> destinationMember, Func<TSource, TMember> resolver)
        {
            if (destinationMember.Body is MemberExpression mexp)
            {
                var name = mexp.Member.Name;
                // wrap resolver to object-based Func<object, object>
                Func<object, object> boxed = src => resolver((TSource)src)!;
                _typeMap.MemberResolvers[name] = boxed;

                // store typed resolver as well
                _typeMap.TypedMemberResolvers[name] = (typeof(TSource), typeof(TMember), resolver);
            }
            else
            {
                throw new ArgumentException("Destination member must be a property access", nameof(destinationMember));
            }

            return this;
        }

        public IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter)
        {
            Func<object, object> boxed = src => converter((TSource)src)!;
            _typeMap.CustomConverter = boxed;
            _typeMap.TypedCustomConverter = converter;
            return this;
        }

        public IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> constructor)
        {
            Func<object, object> boxed = src => constructor((TSource)src)!;
            _typeMap.ConstructUsing = boxed;
            _typeMap.TypedConstructUsing = constructor;
            return this;
        }
    }
}
