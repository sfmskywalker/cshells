using CShells;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.SampleApp.Features;

/// <summary>
/// Admin service interface for administrative operations.
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Gets admin dashboard information.
    /// </summary>
    AdminInfo GetAdminInfo();
}

/// <summary>
/// Admin information record.
/// </summary>
public record AdminInfo(string Status, DateTime ServerTime, int ActiveShells);

/// <summary>
/// Implementation of the admin service.
/// </summary>
public class AdminService : IAdminService
{
    private readonly ITimeService _timeService;

    public AdminService(ITimeService timeService)
    {
        _timeService = timeService;
    }

    /// <inheritdoc />
    public AdminInfo GetAdminInfo()
    {
        return new AdminInfo(
            Status: "Running",
            ServerTime: _timeService.GetCurrentTime(),
            ActiveShells: 2 // Default and Admin
        );
    }
}

/// <summary>
/// Admin feature startup that registers administrative services.
/// </summary>
[ShellFeature("Admin", DependsOn = ["Core"], DisplayName = "Admin Feature")]
public class AdminFeatureStartup : IShellFeature
{
    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAdminService, AdminService>();
    }
}
