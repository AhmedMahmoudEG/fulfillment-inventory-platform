namespace Fulfillment.Application.Users.DTOs;

public record CreateUserRequest(string Email, string Password, string Role);
