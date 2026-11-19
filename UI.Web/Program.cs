using Core.Data;
using Core.Data.Repositories;
using Core.Data.Services;
using Core.Extensions;
using Core.Interfaces;
using Core.Models;
using Core.Utils;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// 1. Логирование в консоль
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

// Оставляем подробное логирование для наших категорий
builder.Logging.AddFilter("Core", LogLevel.Debug);
builder.Logging.AddFilter("UI.Web", LogLevel.Debug);

// 2. Регистрация сервисов Core (ScanService, ParserFactory, IndicatorExtractor и т.д.)
builder.Services.AddCoreServices();

// 3. MVC
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient<UI.Web.Services.AbuseIpdbService>();
builder.Services.AddHttpClient<UI.Web.Services.ProxycheckService>();
builder.Services.AddMemoryCache();
builder.Services.AddHostedService<UI.Web.Services.ResultsCleanupService>();
builder.Services.AddSingleton<UI.Web.Services.WhoisStatusService>();
builder.Services.AddHttpClient<UI.Web.Services.NextcloudService>();
builder.Services.AddSingleton<UI.Web.Services.SyncOrchestrator>();
builder.Services.AddSingleton<IStructuredIocExtractor, Core.Extractors.FinCertExtractor>();

// 4. Предзагрузка Cloudflare диапазонов (лениво, но с логом)
var tempLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
var cloudflareLogger = tempLoggerFactory.CreateLogger("Cloudflare");
WhoisCloudflareRanges.LoadRanges(cloudflareLogger);

builder.Services.AddDbContext<IocDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<Core.Data.Repositories.IocRepository>();
builder.Services.AddScoped<Core.Data.Services.IocIngestionService>();



var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();