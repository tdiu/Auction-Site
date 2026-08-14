namespace API.Validation;

/// <summary>
/// Constrains a caller-supplied <c>returnUrl</c> to a path on the client origin. The client's own
/// guard does not apply once the API is the redirector, so an unchecked value would make the
/// external-login callback a server-side open redirect issued by our own domain.
/// </summary>
public static class ReturnUrlPolicy
{
    public static string Safe(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw[0] != '/')
            return "/";
        if (raw.StartsWith("//") || raw.StartsWith("/\\"))
            return "/";
        return raw;
    }
}
