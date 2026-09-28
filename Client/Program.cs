using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorApp.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
{
    var apiPrefix = builder.Configuration["API_Prefix"];
    var baseAddress = !string.IsNullOrWhiteSpace(apiPrefix)
        ? apiPrefix
        : builder.HostEnvironment.BaseAddress;

    return new HttpClient
    {
        BaseAddress = new Uri(baseAddress, UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(30)
    };
});

await builder.Build().RunAsync();
