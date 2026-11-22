using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.Validation;

/// <summary>
/// Fails for <see cref="Guid.Empty"/> (the value an unselected dropdown binds to). Unlike a check
/// inside IValidatableObject.Validate, this runs together with the other attributes, so a form
/// reports every missing selection in one pass instead of in a second round.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RequiredGuidAttribute : ValidationAttribute
{
    public RequiredGuidAttribute() : base("A selection is required.")
    {
    }

    public override bool IsValid(object? value) => value switch
    {
        null => false,
        Guid guid => guid != Guid.Empty,
        _ => false
    };
}
