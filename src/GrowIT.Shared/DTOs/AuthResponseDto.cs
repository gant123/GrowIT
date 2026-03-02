namespace GrowIT.Shared.DTOs;

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
}

public class RegisterResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public bool RequiresEmailConfirmation { get; set; }
    public string Email { get; set; } = string.Empty;
}

public class ConfirmEmailResultDto
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ResendConfirmationEmailRequest
{
    public string Email { get; set; } = string.Empty;
}
