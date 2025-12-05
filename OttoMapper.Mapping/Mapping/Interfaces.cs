using System;
using System.Linq.Expressions;

namespace OttoMapper.Mapping
{
    public interface IMapper
    {
        TDestination Map<TSource, TDestination>(TSource source);
        object Map(object source, Type sourceType, Type destinationType);
    }

    public interface IMapperConfiguration
    {
        void CreateMap<TSource, TDestination>(Action<IMappingExpression<TSource, TDestination>> mapping = null);
        IMapper BuildMapper(bool warmUp = true);
    }

    public interface IMappingExpression<TSource, TDestination>
    {
        IMappingExpression<TSource, TDestination> ForMember<TMember>(Expression<Func<TDestination, TMember>> destinationMember, Func<TSource, TMember> resolver);
        IMappingExpression<TSource, TDestination> ConvertUsing(Func<TSource, TDestination> converter);
        IMappingExpression<TSource, TDestination> ConstructUsing(Func<TSource, TDestination> constructor);
    }
}
