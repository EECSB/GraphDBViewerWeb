using Blazored.LocalStorage;
using GraphDBViewerWeb;
using GraphDBViewerWeb.Code;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<IAppStorage, LocalAppStorage>();
builder.Services.AddScoped<WorkspaceStore>();
builder.Services.AddScoped<LlmConnectionStore>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
