using FluentValidation;
using Qwiik.InvoiceManagement.Application.DTOs.Invoices;

namespace Qwiik.InvoiceManagement.Application.Validators;

public sealed class GetInvoicesQueryValidator : AbstractValidator<GetInvoicesQuery>
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "invoicedate",
        "duedate",
        "totalamount",
        "invoicenumber",
        "customername",
        "status",
        "createdat"
    };

    public GetInvoicesQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Status)
            .IsInEnum().When(x => x.Status.HasValue)
            .WithMessage("Invalid invoice status filter.");

        RuleFor(x => x.ToDate)
            .Must((query, toDate) => !query.FromDate.HasValue || !toDate.HasValue || toDate.Value.Date >= query.FromDate.Value.Date)
            .WithMessage("ToDate cannot precede FromDate.");

        RuleFor(x => x.SortBy)
            .Must(sortBy => string.IsNullOrWhiteSpace(sortBy) || AllowedSortFields.Contains(sortBy.Trim()))
            .WithMessage("SortBy must be one of: invoiceDate, dueDate, totalAmount, invoiceNumber, customerName, status, createdAt.");

        RuleFor(x => x.SortDirection)
            .Must(dir => string.IsNullOrWhiteSpace(dir) || dir.Equals("asc", StringComparison.OrdinalIgnoreCase) || dir.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("SortDirection must be 'asc' or 'desc'.");
    }
}
