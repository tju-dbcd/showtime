namespace ShowtimeBackend.DTOs.UserPermission;

public sealed record UserRealNameResponse(
    long RealNameId,
    string RealName,
    string MaskedIdCardNo,
    bool IsDefault,
    bool IsVerified,
    DateTime CreateTime,
    DateTime UpdateTime);
