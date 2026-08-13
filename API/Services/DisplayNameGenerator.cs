using System.Globalization;
using System.Text;

namespace API.Services;

public static class DisplayNameGenerator
{
    private const int MaxBaseLength = 11;

    // Based on minimum length rule in registerDto
    private const int MinLength = 3;

    public static string Derive(string? providerName, string email)
    {
        var slug = Slugify(providerName);
        if (slug.Length < MinLength)
            slug = Slugify(email.Split('@')[0]);
        if (slug.Length < MinLength)
            slug = "user";

        return slug.Length > MaxBaseLength ? slug[..MaxBaseLength] : slug;
    }

    // Must exceed the last numbered arm below, so at least one attempt reaches the GUID branch.
    // Lower it and a signup can fail for want of a username
    public const int MaxAttempts = 6;

    public static string Candidate(string baseName, int attempt)
    {
        // Attempt 0 gets clean name. Later attempts append random digits
        return attempt switch
        {
            0 => baseName,
            1 or 2 => baseName + Random.Shared.Next(100, 1000),
            3 or 4 => baseName + Random.Shared.Next(1000, 10000),
            _ => "user" + Guid.NewGuid().ToString("N")[..10]
        };
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            // Drop accents left behind by FormD instead of accented letter themselves
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            // ASCII, alphanumeric only
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
                slug.Append(char.ToLowerInvariant(c));
        }

        return slug.ToString();
    }
}
