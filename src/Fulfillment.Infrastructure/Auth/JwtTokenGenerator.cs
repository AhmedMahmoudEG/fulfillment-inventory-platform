using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fulfillment.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fulfillment.Infrastructure.Auth;

public class JwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings;

    public JwtTokenGenerator(IOptions<JwtSettings> jwtSettingsOptions)
    {
        _jwtSettings = jwtSettingsOptions.Value;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(ApplicationUser user, string role)
    {
        if (string.IsNullOrWhiteSpace(_jwtSettings.SigningKey))
        {
            throw new InvalidOperationException("JWT SigningKey configuration is missing or empty.");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes > 0 ? _jwtSettings.ExpirationMinutes : 60);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = !string.IsNullOrWhiteSpace(_jwtSettings.Issuer) ? _jwtSettings.Issuer : "FulfillmentApi",
            Audience = !string.IsNullOrWhiteSpace(_jwtSettings.Audience) ? _jwtSettings.Audience : "FulfillmentClients",
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return (tokenHandler.WriteToken(token), expiresAt);
    }
}
