using System;
using Microsoft.Extensions.DependencyInjection;
using OttoMapper.Mapping;

namespace OttoMapper.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOttoMapper(this IServiceCollection services, Action<MapperConfiguration>? configure = null)
        {
            var config = new MapperConfiguration();
            configure?.Invoke(config);
            // register config as singleton
            services.AddSingleton(config);

            // register mapper as singleton
            services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().BuildMapper());

            return services;
        }

        public static IServiceCollection AddOttoMapper(this IServiceCollection services, MapperConfiguration config)
        {
            services.AddSingleton(config);
            services.AddSingleton<IMapper>(sp => config.BuildMapper());
            return services;
        }
    }
}
