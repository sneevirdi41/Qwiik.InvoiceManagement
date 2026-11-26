using FluentValidation;
using Qwiik.InvoiceManagement.Application.DTOs.Invoices;

namespace Qwiik.InvoiceManagement.Application.Validators;

public sealed class CreateInvoiceItemRequestValidator : AbstractValidator<CreateInvoiceItemRequest>
{
    public CreateInvoiceItemRequestValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Item description is required.")
            .MaximumLength(500).WithMessage("Item description must not exceed 500 characters.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0m).WithMessage("Item quantity must be greater than zero.");

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0m).WithMessage("Item unit price cannot be negative.");
    }
}

public sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.InvoiceNumber)
            .NotEmpty().WithMessage("Invoice number is required.")
            .MaximumLength(50).WithMessage("Invoice number must not exceed 50 characters.");

        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Customer name is required.")
            .MaximumLength(200).WithMessage("Customer name must not exceed 200 characters.");

        RuleFor(x => x.InvoiceDate)
            .NotEmpty().WithMessage("Invoice date is required.");

        RuleFor(x => x.DueDate)
            .NotEmpty().WithMessage("Due date is required.")
            .Must((request, dueDate) => dueDate.Date >= request.InvoiceDate.Date)
            .WithMessage("Due date cannot precede invoice date.");

        RuleFor(x => x.TaxPercentage)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Tax percentage must be between 0 and 100.");

        RuleFor(x => x.Items)
            .NotNull().WithMessage("At least one invoice item is required.")
            .Must(items => items is { Count: > 0 })
            .WithMessage("At least one invoice item is required.");

        RuleForEach(x => x.Items)
            .SetValidator(new CreateInvoiceItemRequestValidator());
    }
}
