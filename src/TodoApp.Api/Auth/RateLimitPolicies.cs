namespace TodoApp.Api.Auth;

public static class RateLimitPolicies
{
    /// <summary>Authenticated traffic, partitioned by the token subject.</summary>
    public const string PerUser = "per-user";

    /// <summary>Anonymous traffic, partitioned by remote address.</summary>
    public const string PerAddress = "per-address";
}
