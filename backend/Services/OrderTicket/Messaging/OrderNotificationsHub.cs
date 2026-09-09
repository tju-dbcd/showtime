using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

[Authorize]
public sealed class OrderNotificationsHub : Hub;
