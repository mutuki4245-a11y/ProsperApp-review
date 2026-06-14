namespace ProsperApp.Options;

public class GoogleAuthOptions
{
    public string[] AllowedEmails { get; set; } = [];

    public string[] AllowedDomains { get; set; } = [];

    public bool HasAccessRestriction =>
        AllowedEmails.Any(value => !string.IsNullOrWhiteSpace(value)) ||
        AllowedDomains.Any(value => !string.IsNullOrWhiteSpace(value));

    public bool IsAllowed(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !HasAccessRestriction)
        {
            return false;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (AllowedEmails.Any(value => string.Equals(value.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var atIndex = normalizedEmail.LastIndexOf('@');
        if (atIndex < 0 || atIndex == normalizedEmail.Length - 1)
        {
            return false;
        }

        var domain = normalizedEmail[(atIndex + 1)..];
        return AllowedDomains
            .Select(value => value.Trim().TrimStart('@'))
            .Any(value => string.Equals(value, domain, StringComparison.OrdinalIgnoreCase));
    }
}
