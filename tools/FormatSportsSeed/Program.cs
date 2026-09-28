using System;
using System.IO;
using System.Threading.Tasks;
using BlazorApp.Shared;

internal static class Program
{
    private static async Task Main()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, "Api", "data", "sports-schedules.json");
        var json = await File.ReadAllTextAsync(path);
        var data = SportsSchedules.ParseJson(json);
        SportsSchedules.EnsureEventIds(data);
        await File.WriteAllTextAsync(path, SportsSchedules.ToJson(data));
        Console.WriteLine($"Formatted {data.Events.Count} events at {path}");
    }
}
