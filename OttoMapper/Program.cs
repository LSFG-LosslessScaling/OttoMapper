using System;
using System.Collections.Generic;
using OttoMapper.Mapping;

namespace OttoMapper
{
    public record AddressDto(string City, string Zip);
    public record SourceDto(string Name, int Age, AddressDto Home, List<AddressDto> PreviousAddresses);
    public class AddressModel
    {
        public string? City { get; set; }
        public string? Zip { get; set; }
    }

    public class DestinationDto
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public AddressModel Home { get; set; }
        public AddressModel[] PreviousAddresses { get; set; }
    }

    public static class Program
    {
        public static void Main()
        {
            Console.WriteLine("OttoMapper demo");

            var config = new MapperConfiguration();
            config.CreateMap<SourceDto, DestinationDto>(cfg =>
            {
                cfg.ForMember(d => d.Name, s => s.Name.ToUpper());
            });

            config.CreateMap<AddressDto, AddressModel>();

            var mapper = config.BuildMapper();

            var src = new SourceDto("Alice", 30, new AddressDto("Berlin","10115"), new List<AddressDto>{ new AddressDto("Munich","80331") });
            var dst = mapper.Map<SourceDto, DestinationDto>(src);
            Console.WriteLine($"Mapped: {dst.Name} ({dst.Age}) Home={dst.Home.City} PrevCount={dst.PreviousAddresses?.Length}");
        }
    }
}
