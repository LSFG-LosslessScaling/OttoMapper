using System;
using System.Collections.Generic;
using System.Linq;

namespace OttoMapper.Mapping
{
    public class MapperConfiguration : IMapperConfiguration
    {
        internal readonly List<TypeMap> TypeMaps = new List<TypeMap>();

        public void CreateMap<TSource, TDestination>(Action<IMappingExpression<TSource, TDestination>> mapping = null)
        {
            var typeMap = new TypeMap(typeof(TSource), typeof(TDestination));
            var expr = new MappingExpression<TSource, TDestination>(typeMap);
            mapping?.Invoke(expr);
            TypeMaps.Add(typeMap);
        }

        public IMapper BuildMapper(bool warmUp = true)
        {
            var mapper = new Mapper(this);
            if (warmUp)
            {
                foreach (var tm in TypeMaps.ToArray())
                {
                    // trigger map func creation
                    mapper.PrepareMap(tm.SourceType, tm.DestinationType);
                }
            }

            return mapper;
        }

        internal TypeMap GetTypeMap(Type source, Type destination)
        {
            return TypeMaps.FirstOrDefault(tm => tm.SourceType == source && tm.DestinationType == destination);
        }
    }
}
