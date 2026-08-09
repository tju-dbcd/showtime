using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShowtimeBackend.Common;
using ShowtimeBackend.Controllers.SeatZone;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Services.SeatZone;

namespace ShowtimeBackend.Tests.SeatZone;

public sealed class SeatLocksControllerTests
{
    [Fact]
    public async Task Lock_ReturnsCreatedForAuthenticatedUser()
    {
        var service = new StubSeatLockService();
        var controller = CreateController(service);

        var action = await controller.Lock(
            10,
            new SeatLockBatchRequest([50]),
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        var response = Assert.IsType<ApiResponse<SeatLockBatchResponse>>(result.Value);
        Assert.True(response.Success);
        Assert.Equal(7, service.LastUserId);
        Assert.Equal("alice", service.LastActor);
    }

    [Fact]
    public async Task Release_ReturnsOkForAuthenticatedUser()
    {
        var service = new StubSeatLockService();
        var controller = CreateController(service);

        var action = await controller.Release(
            10,
            new SeatLockReleaseRequest(["token-50"]),
            CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<SeatLockReleaseResponse>>(result.Value);
        Assert.True(response.Success);
        Assert.Equal(7, service.LastUserId);
    }

    private static SeatLocksController CreateController(ISeatLockService service)
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", "7"), new Claim(ClaimTypes.Name, "alice")],
            "Test");
        return new SeatLocksController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private sealed class StubSeatLockService : ISeatLockService
    {
        public long LastUserId { get; private set; }
        public string? LastActor { get; private set; }

        public Task<SeatZoneResult<SeatLockBatchResponse>> LockAsync(
            long userId,
            string actor,
            long sessionId,
            SeatLockBatchRequest request,
            CancellationToken cancellationToken)
        {
            LastUserId = userId;
            LastActor = actor;
            var expireTime = new DateTime(2026, 8, 9, 12, 10, 0);
            return Task.FromResult(SeatZoneResult<SeatLockBatchResponse>.Success(
                new SeatLockBatchResponse(
                    sessionId,
                    expireTime,
                    [new SeatLockItemResponse(50, "token-50", expireTime)])));
        }

        public Task<SeatZoneResult<SeatLockReleaseResponse>> ReleaseAsync(
            long userId,
            string actor,
            long sessionId,
            SeatLockReleaseRequest request,
            CancellationToken cancellationToken)
        {
            LastUserId = userId;
            LastActor = actor;
            return Task.FromResult(SeatZoneResult<SeatLockReleaseResponse>.Success(
                new SeatLockReleaseResponse(sessionId, request.LockTokens.Count)));
        }
    }
}
