using ShowtimeBackend.Entities.UserPermission;

namespace ShowtimeBackend.Common.Jwt;

public interface IJwtTokenService
{
    JwtTokenResult CreateToken(
        SysUser user,
        IReadOnlyCollection<string> roleCodes,
        long sessionId);
}
