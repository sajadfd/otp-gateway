using System.ComponentModel.DataAnnotations;

namespace OtpGateway.Api.Dtos;

public record SendOtpRequest
{
    [Required, Phone, MaxLength(32)]
    public string Phone { get; init; } = "";

    [EmailAddress, MaxLength(320)]
    public string? Email { get; init; }

    public string? PreferredChannel { get; init; }

    [MaxLength(128)]
    public string? Ref { get; init; }
}

public record SendOtpResponse(bool Success, Guid? OtpId, string? Channel, string? Error);

public record VerifyOtpRequest
{
    [Required]
    public Guid OtpId { get; init; }

    [Required, MaxLength(8)]
    public string Code { get; init; } = "";
}

public record VerifyOtpResponse(bool Verified, string? Error, string? Phone, string? Channel);
