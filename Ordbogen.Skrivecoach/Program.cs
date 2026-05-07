using System.Net.Http.Headers;
using Ordbogen.Skrivecoach.Components;
using Ordbogen.Skrivecoach.Models;
using Ordbogen.Skrivecoach.Services;

namespace Ordbogen.Skrivecoach;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services
            .AddOptions<OrdbogenOptions>()
            .Bind(builder.Configuration.GetSection(OrdbogenOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Ordbogen:BaseUrl skal være sat.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Model), "Ordbogen:Model skal være sat.");

        builder.Services.AddHttpClient<OrdbogenClient>((sp, http) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrdbogenOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(60);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.ApiKey);
            }
        });

        builder.Services.AddScoped<SkrivecoachService>();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
