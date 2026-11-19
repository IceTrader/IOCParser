// Core/Extensions/ServiceCollectionExtensions.cs
using Core.Extractors;
using Core.Factories;
using Core.Interfaces;
using Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IIndicatorExtractor, IndicatorExtractor>();
        services.AddSingleton<ParserFactory>();
        services.AddScoped<IScanService, ScanService>();
        return services;
    }
}