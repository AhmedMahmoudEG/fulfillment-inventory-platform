namespace Fulfillment.Application.Auth.DTOs;

public record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);
