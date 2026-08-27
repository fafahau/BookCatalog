using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using BookCatalog;
using BookCatalog.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// WebAssembly's IJSRuntime is always in-process, so synchronous JS interop
// (needed by LocalStorageSessionPersistence) is safe here.
builder.Services.AddSingleton(sp => (IJSInProcessRuntime)sp.GetRequiredService<IJSRuntime>());
builder.Services.AddSingleton<LocalStorageSessionPersistence>();
builder.Services.AddSingleton<SupabaseService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ImageUploadService>();
builder.Services.AddSingleton<BookService>();
builder.Services.AddSingleton<CollectionService>();
builder.Services.AddScoped<IsbnLookupService>();
builder.Services.AddSingleton<UserService>();

builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CustomAuthStateProvider>());
builder.Services.AddAuthorizationCore();

var host = builder.Build();

var supabaseService = host.Services.GetRequiredService<SupabaseService>();
await supabaseService.InitializeAsync();
await host.Services.GetRequiredService<AuthService>().RestoreSessionAsync();

await host.RunAsync();
