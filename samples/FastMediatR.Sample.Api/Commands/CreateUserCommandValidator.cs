using FastMediatR.Sample.Api.Commands;
using FluentValidation;

namespace FastMediatR.Sample.Api.Commands;

/// <summary>
/// FluentValidation validator for <see cref="CreateUserCommand"/>.
/// Demonstrates <see cref="FastMediatR.ValidationBehavior{TRequest,TResponse}"/> in Phase 4.
/// Registered automatically via <c>builder.Services.AddValidatorsFromAssemblyContaining&lt;Program&gt;()</c>.
/// </summary>
public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    /// <summary>Configures validation rules for <see cref="CreateUserCommand"/>.</summary>
    public CreateUserCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty().WithMessage("Email must not be empty.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(c => c.DisplayName)
            .NotEmpty().WithMessage("Display name must not be empty.")
            .MaximumLength(100).WithMessage("Display name must not exceed 100 characters.");
    }
}
