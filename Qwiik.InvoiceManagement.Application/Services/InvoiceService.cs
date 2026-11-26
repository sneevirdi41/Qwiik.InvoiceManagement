using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwiik.InvoiceManagement.Application.Abstractions;
using Qwiik.InvoiceManagement.Application.DTOs.Invoices;
using Qwiik.InvoiceManagement.Application.Exceptions;
using Qwiik.InvoiceManagement.Domain.Entities;
using Qwiik.InvoiceManagement.Domain.Enums;

namespace Qwiik.InvoiceManagement.Application.Services;

public sealed class InvoiceService : IInvoiceService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IValidator<CreateInvoiceRequest> _createValidator;
    private readonly IValidator<UpdateInvoiceStatusRequest> _updateStatusValidator;
    private readonly IValidator<GetInvoicesQuery> _getInvoicesValidator;

    public InvoiceService(
        IApplicationDbContext dbContext,
        ICurrentTenantContext tenantContext,
        IDateTimeProvider dateTimeProvider,
        IValidator<CreateInvoiceRequest> createValidator,
        IValidator<UpdateInvoiceStatusRequest> updateStatusValidator,
        IValidator<GetInvoicesQuery> getInvoicesValidator)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _dateTimeProvider = dateTimeProvider;
        _createValidator = createValidator;
        _updateStatusValidator = updateStatusValidator;
        _getInvoicesValidator = getInvoicesValidator;
    }

    public async Task<InvoiceResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireTenantOrganizationId();
        await ValidateAsync(_createValidator, request, cancellationToken);

        var organizationExists = await _dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(o => o.Id == organizationId, cancellationToken);

        if (!organizationExists)
        {
            throw new ForbiddenAccessException("The authenticated organization is not registered or active.");
        }

        var normalizedInvoiceNumber = request.InvoiceNumber.Trim();
        var duplicateExists = await _dbContext.Invoices
            .AsNoTracking()
            .AnyAsync(i => i.OrganizationId == organizationId && i.InvoiceNumber == normalizedInvoiceNumber, cancellationToken);

        if (duplicateExists)
        {
            throw new ConflictException($"Invoice number '{normalizedInvoiceNumber}' already exists for this organization.");
        }

        var invoice = Invoice.Create(
            organizationId,
            normalizedInvoiceNumber,
            request.CustomerName,
            request.InvoiceDate,
            request.DueDate,
            request.TaxPercentage,
            request.Items.Select(item => (item.Description, item.Quantity, item.UnitPrice)),
            _dateTimeProvider.UtcNow);

        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(invoice);
    }

    public async Task<PagedResponse<InvoiceSummaryResponse>> GetPagedAsync(GetInvoicesQuery query, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireTenantOrganizationId();
        await ValidateAsync(_getInvoicesValidator, query, cancellationToken);

        IQueryable<Invoice> invoicesQuery = _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.OrganizationId == organizationId);

        if (query.Status.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.Status == query.Status.Value);
        }

        if (query.FromDate.HasValue)
        {
            var fromDate = query.FromDate.Value.Date;
            invoicesQuery = invoicesQuery.Where(i => i.InvoiceDate >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDate = query.ToDate.Value.Date;
            invoicesQuery = invoicesQuery.Where(i => i.InvoiceDate <= toDate);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            invoicesQuery = invoicesQuery.Where(i =>
                i.InvoiceNumber.Contains(search) ||
                i.CustomerName.Contains(search));
        }

        invoicesQuery = ApplySorting(invoicesQuery, query.SortBy, query.SortDirection);

        var totalCount = await invoicesQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var items = await invoicesQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(i => new InvoiceSummaryResponse(
                i.Id,
                i.OrganizationId,
                i.InvoiceNumber,
                i.CustomerName,
                i.InvoiceDate,
                i.DueDate,
                i.Subtotal,
                i.TaxAmount,
                i.TotalAmount,
                i.Status,
                i.CreatedAt,
                i.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResponse<InvoiceSummaryResponse>(
            items,
            query.Page,
            query.PageSize,
            totalCount,
            totalPages,
            query.Page > 1,
            query.Page < totalPages);
    }

    public async Task<InvoiceResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireTenantOrganizationId();

        var invoice = await _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.Id == id && i.OrganizationId == organizationId)
            .Select(i => new InvoiceResponse(
                i.Id,
                i.OrganizationId,
                i.InvoiceNumber,
                i.CustomerName,
                i.InvoiceDate,
                i.DueDate,
                i.Subtotal,
                i.TaxAmount,
                i.TotalAmount,
                i.Status,
                i.CreatedAt,
                i.UpdatedAt,
                i.Items
                    .OrderBy(item => item.Description)
                    .Select(item => new InvoiceItemResponse(
                        item.Id,
                        item.Description,
                        item.Quantity,
                        item.UnitPrice,
                        item.LineTotal))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            throw new NotFoundException($"Invoice '{id}' was not found.");
        }

        return invoice;
    }

    public async Task<InvoiceResponse> UpdateStatusAsync(Guid id, UpdateInvoiceStatusRequest request, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireTenantOrganizationId();
        await ValidateAsync(_updateStatusValidator, request, cancellationToken);

        var invoice = await _dbContext.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id && i.OrganizationId == organizationId, cancellationToken);

        if (invoice is null)
        {
            throw new NotFoundException($"Invoice '{id}' was not found.");
        }

        invoice.TransitionStatus(request.Status, _dateTimeProvider.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToResponse(invoice);
    }

    public async Task<DashboardSummaryResponse> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = RequireTenantOrganizationId();
        var todayUtc = _dateTimeProvider.UtcNow.Date;

        var summary = await _dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.OrganizationId == organizationId)
            .GroupBy(_ => 1)
            .Select(g => new DashboardSummaryResponse(
                g.Count(),
                g.Sum(i => i.Status == InvoiceStatus.Paid ? 1 : 0),
                g.Sum(i => i.Status == InvoiceStatus.Issued ? 1 : 0),
                g.Sum(i => i.Status == InvoiceStatus.Issued && i.DueDate < todayUtc ? 1 : 0),
                g.Sum(i => i.Status != InvoiceStatus.Cancelled ? i.TotalAmount : 0m),
                g.Sum(i => i.Status == InvoiceStatus.Paid ? i.TotalAmount : 0m),
                g.Sum(i => i.Status == InvoiceStatus.Issued ? i.TotalAmount : 0m)))
            .FirstOrDefaultAsync(cancellationToken);

        return summary ?? new DashboardSummaryResponse(0, 0, 0, 0, 0m, 0m, 0m);
    }

    private Guid RequireTenantOrganizationId()
    {
        if (!_tenantContext.IsAuthenticated || _tenantContext.OrganizationId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A valid authenticated organization context is required.");
        }

        return _tenantContext.OrganizationId;
    }

    private static async Task ValidateAsync<T>(IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

            throw new AppValidationException(errors);
        }
    }

    private static IQueryable<Invoice> ApplySorting(IQueryable<Invoice> query, string? sortBy, string? sortDirection)
    {
        var descending = string.IsNullOrWhiteSpace(sortDirection) ||
                         sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);

        var field = (sortBy ?? "invoicedate").Trim().ToLowerInvariant();

        return (field, descending) switch
        {
            ("duedate", true) => query.OrderByDescending(i => i.DueDate).ThenByDescending(i => i.CreatedAt),
            ("duedate", false) => query.OrderBy(i => i.DueDate).ThenBy(i => i.CreatedAt),
            ("totalamount", true) => query.OrderByDescending(i => i.TotalAmount).ThenByDescending(i => i.CreatedAt),
            ("totalamount", false) => query.OrderBy(i => i.TotalAmount).ThenBy(i => i.CreatedAt),
            ("invoicenumber", true) => query.OrderByDescending(i => i.InvoiceNumber),
            ("invoicenumber", false) => query.OrderBy(i => i.InvoiceNumber),
            ("customername", true) => query.OrderByDescending(i => i.CustomerName),
            ("customername", false) => query.OrderBy(i => i.CustomerName),
            ("status", true) => query.OrderByDescending(i => i.Status).ThenByDescending(i => i.CreatedAt),
            ("status", false) => query.OrderBy(i => i.Status).ThenBy(i => i.CreatedAt),
            ("createdat", true) => query.OrderByDescending(i => i.CreatedAt),
            ("createdat", false) => query.OrderBy(i => i.CreatedAt),
            (_, true) => query.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.CreatedAt),
            (_, false) => query.OrderBy(i => i.InvoiceDate).ThenBy(i => i.CreatedAt)
        };
    }

    private static InvoiceResponse MapToResponse(Invoice invoice)
    {
        return new InvoiceResponse(
            invoice.Id,
            invoice.OrganizationId,
            invoice.InvoiceNumber,
            invoice.CustomerName,
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.Subtotal,
            invoice.TaxAmount,
            invoice.TotalAmount,
            invoice.Status,
            invoice.CreatedAt,
            invoice.UpdatedAt,
            invoice.Items
                .OrderBy(item => item.Description)
                .Select(item => new InvoiceItemResponse(
                    item.Id,
                    item.Description,
                    item.Quantity,
                    item.UnitPrice,
                    item.LineTotal))
                .ToList());
    }
}
