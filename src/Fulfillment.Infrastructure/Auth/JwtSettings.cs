namespace Fulfillment.Infrastructure.Auth;

public class JwtSettings
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "FulfillmentApi";
    public string Audience { get; set; } = "FulfillmentClients";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
}
