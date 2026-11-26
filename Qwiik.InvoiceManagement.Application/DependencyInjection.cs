using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Qwiik.InvoiceManagement.Application.Services;

namespace Qwiik.InvoiceManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IInvoiceService, InvoiceService>();

        return services;
    }
}
