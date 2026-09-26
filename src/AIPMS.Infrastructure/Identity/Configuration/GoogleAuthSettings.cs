namespace AIPMS.Infrastructure.Identity.Configuration;

public sealed class GoogleAuthSettings
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "";
    public string[] AllowedOrigins { get; set; } = [];
    internal string DriveClientId { get; set; } = "";

    public bool IsValid() => !Enabled ||
        (ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) && ClientId != DriveClientId
         && AllowedOrigins.Length > 0 && AllowedOrigins.All(IsOrigin));

    private static bool IsOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)
        && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.UserInfo.Length == 0 && value == uri.GetLeftPart(UriPartial.Authority);
}
