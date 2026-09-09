using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common;
using ShowtimeBackend.DTOs.UserPermission;
using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Tests.UserPermission;

public sealed class UserRealNamesApiTests
{
    [Fact]
    public async Task Crud_EncryptsStoredValueAndNeverReturnsPlaintextOrCiphertext()
    {
        using var factory = new AuthTestFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);
        const string idCardNo = "31010119900101123X";

        var create = await client.PostAsJsonAsync(
            "/api/users/me/real-names",
            new CreateUserRealNameRequest
            {
                RealName = "Alice",
                IdCardNo = idCardNo,
            });
        var createBody = await create.Content.ReadAsStringAsync();
        var createResponse = await create.Content.ReadFromJsonAsync<ApiResponse<UserRealNameResponse>>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(createResponse?.Data);
        Assert.DoesNotContain(idCardNo, createBody, StringComparison.Ordinal);
        Assert.DoesNotContain("v1.", createBody, StringComparison.Ordinal);
        Assert.Equal("310***********123X", createResponse.Data.MaskedIdCardNo);

        var stored = await factory.ExecuteDbContextAsync(async db =>
            await db.Set<UserRealName>().SingleAsync());
        Assert.StartsWith("v1.", stored.IdCardNo);
        Assert.DoesNotContain(idCardNo, stored.IdCardNo, StringComparison.Ordinal);

        var list = await client.GetAsync("/api/users/me/real-names");
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(idCardNo, listBody, StringComparison.Ordinal);
        Assert.DoesNotContain("v1.", listBody, StringComparison.Ordinal);

        var changed = await client.PutAsJsonAsync(
            $"/api/users/me/real-names/{createResponse.Data.RealNameId}",
            new UpdateUserRealNameRequest
            {
                RealName = "Alice Changed",
                IdCardNo = idCardNo,
            });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        var deleted = await client.DeleteAsync(
            $"/api/users/me/real-names/{createResponse.Data.RealNameId}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Fact]
    public async Task List_RejectsAnonymousRequest()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/users/me/real-names");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(AuthTestFactory factory)
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedRoleAsync();
        var client = factory.CreateApiClient();
        var registration = await client.PostAsJsonAsync(
            "/api/auth/register",
            TestRequests.ValidRegistration());
        registration.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            TestRequests.Login("alice"));
        login.EnsureSuccessStatusCode();
        var response = await AuthTestFactory.ReadResponseAsync<LoginResponse>(login);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", response.Data!.AccessToken);
        return client;
    }
}
