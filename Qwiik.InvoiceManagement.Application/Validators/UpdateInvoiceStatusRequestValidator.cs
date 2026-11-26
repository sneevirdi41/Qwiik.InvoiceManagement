using FluentValidation;
using Qwiik.InvoiceManagement.Application.DTOs.Invoices;

namespace Qwiik.InvoiceManagement.Application.Validators;

public sealed class UpdateInvoiceStatusRequestValidator : AbstractValidator<UpdateInvoiceStatusRequest>
{
    public UpdateInvoiceStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("A valid invoice status is required.");
    }
}
