using Hangfire.Dashboard;

namespace FollowUp.Api.Auth;

/// <summary>
/// Restricts the Hangfire dashboard to local requests (background-job authorization, architect security).
/// A production deployment should replace this with a privilege-gated filter (e.g. ManageUsers).
/// </summary>
public sealed class LocalRequestsOnlyDashboardAuthorization : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var remote = http.Connection.RemoteIpAddress;
        var local = http.Connection.LocalIpAddress;
        if (remote is null) return true;                       // in-process
        if (remote.Equals(local)) return true;                 // same host
        return System.Net.IPAddress.IsLoopback(remote);
    }
}
