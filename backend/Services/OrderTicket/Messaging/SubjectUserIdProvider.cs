using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        GetUserId(connection.User);

    internal static string? GetUserId(ClaimsPrincipal? user) =>
        user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
