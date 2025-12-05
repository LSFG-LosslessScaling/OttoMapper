using System;
using System.Collections.Generic;
using System.Diagnostics;
using OttoMapper.Mapping;

class Program
{
    static void Main()
    {
        // prepare config
        var config = new MapperConfiguration();
        config.CreateMap<SourceDto, DestinationDto>(cfg => cfg.ForMember(d => d.Name, s => s.Name.ToUpper()));
        config.CreateMap<AddressDto, AddressModel>();
        var mapper = config.BuildMapper(warmUp: true);

        var src = new SourceDto("Alice", 30, new AddressDto("Berlin", "10115"), new List<AddressDto> { new AddressDto("Munich", "80331") });

        Console.WriteLine("Running allocation micro-profiles (per-op)");

        RunAllocationTest("Otto_Map_Typed", () => mapper.Map<SourceDto, DestinationDto>(src), iterations: 1_000_000);
        RunAllocationTest("Otto_Map_ObjectAPI", () => mapper.Map((object)src, typeof(SourceDto), typeof(DestinationDto)), iterations: 1_000_000);

        // collection-only test
        var list = new List<AddressDto>();
        for (int i = 0; i < 100; i++) list.Add(new AddressDto("City", "00000"));
        RunAllocationTest("Otto_Map_Collection_Typed", () => mapper.Map<List<AddressDto>, AddressModel[]>(list), iterations: 200_000);

        Console.WriteLine("Done");
    }

    static void RunAllocationTest(string name, Action action, int iterations)
    {
        // warmup
        for (int i = 0; i < 1000; i++) action();

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var sw = Stopwatch.StartNew();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) action();
        long after = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();

        double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
        long allocated = after - before;
        double allocPerOp = (double)allocated / iterations;

        Console.WriteLine($"{name}: iterations={iterations}, time={sw.Elapsed.TotalMilliseconds}ms, ops/sec={opsPerSec:N0}, allocated={allocated} bytes, alloc/op={allocPerOp:N6} bytes");
    }
}

public record AddressDto(string City, string Zip);
public record SourceDto(string Name, int Age, AddressDto Home, List<AddressDto> PreviousAddresses);
public class AddressModel { public string City { get; set; } public string Zip { get; set; } }
public class DestinationDto { public string Name { get; set; } public int Age { get; set; } public AddressModel Home { get; set; } public AddressModel[] PreviousAddresses { get; set; } }
