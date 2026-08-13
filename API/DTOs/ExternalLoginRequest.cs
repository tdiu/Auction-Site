namespace API.DTOs;

public record ExternalLoginRequest(string Provider, string ProviderKey, string? Email, string? Name);
