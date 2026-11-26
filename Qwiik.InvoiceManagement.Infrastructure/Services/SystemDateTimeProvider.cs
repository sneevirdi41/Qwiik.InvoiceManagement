using Qwiik.InvoiceManagement.Application.Abstractions;

namespace Qwiik.InvoiceManagement.Infrastructure.Services;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
