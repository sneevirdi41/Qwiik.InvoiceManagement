using Qwiik.InvoiceManagement.Application.DTOs.Invoices;

namespace Qwiik.InvoiceManagement.Application.Services;

public interface IInvoiceService
{
    Task<InvoiceResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<InvoiceSummaryResponse>> GetPagedAsync(GetInvoicesQuery query, CancellationToken cancellationToken = default);
    Task<InvoiceResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<InvoiceResponse> UpdateStatusAsync(Guid id, UpdateInvoiceStatusRequest request, CancellationToken cancellationToken = default);
    Task<DashboardSummaryResponse> GetDashboardAsync(CancellationToken cancellationToken = default);
}
