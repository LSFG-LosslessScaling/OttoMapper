using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Otto = OttoMapper.Mapping;
using AM = AutoMapper;

public record AddressDto(string City, string Zip);
public record SourceDto(string Name, int Age, AddressDto Home, List<AddressDto> PreviousAddresses);
public class AddressModel
{
    public string? City { get; set; }
    public string? Zip { get; set; }
}

public class DestinationDto
{
    public string? Name { get; set; }
    public int Age { get; set; }
    public AddressModel? Home { get; set; }
    public AddressModel[]? PreviousAddresses { get; set; }
}

[MemoryDiagnoser]
public class MapBenchmarks
{
    private Otto.IMapper _ottoMapper;
    private AM.IMapper _autoMapper;
    private SourceDto _src;

    public MapBenchmarks()
    {
        var config = new Otto.MapperConfiguration();
        config.CreateMap<SourceDto, DestinationDto>(cfg => cfg.ForMember(d => d.Name, s => s.Name.ToUpper()));
        config.CreateMap<AddressDto, AddressModel>();

        try
        {
            // enable warm-up to precompile typed delegates
            _ottoMapper = config.BuildMapper(warmUp: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Otto mapper warm-up failed: " + ex);
            // fallback to non-warmup mapper so benchmark can run
            _ottoMapper = config.BuildMapper(warmUp: false);
        }

        var automapperConfig = new AM.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<SourceDto, DestinationDto>().ForMember(d => d.Name, opt => opt.MapFrom(s => s.Name.ToUpper()));
            cfg.CreateMap<AddressDto, AddressModel>();
        });
        _autoMapper = new AM.Mapper(automapperConfig);

        _src = new SourceDto("Alice", 30, new AddressDto("Berlin","10115"), new List<AddressDto>{ new AddressDto("Munich","80331") });
    }

    [Benchmark]
    public DestinationDto Otto_Map() => _ottoMapper.Map<SourceDto, DestinationDto>(_src);

    [Benchmark]
    public DestinationDto AutoMapper_Map() => _autoMapper.Map<SourceDto, DestinationDto>(_src);

    public static void Main()
    {
        var summary = BenchmarkRunner.Run<MapBenchmarks>();
    }
}
