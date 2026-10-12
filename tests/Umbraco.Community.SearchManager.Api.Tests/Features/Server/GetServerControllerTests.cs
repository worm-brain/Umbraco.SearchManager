using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Community.SearchManager.Features.Server;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Server;

public class GetServerControllerTests
{
    private readonly IServerRoleAccessor _roleAccessor = Substitute.For<IServerRoleAccessor>();
    private readonly IServerRegistrationService _registrations =
        Substitute.For<IServerRegistrationService>();

    [Theory]
    [InlineData(ServerRole.Single, 0, false)]
    [InlineData(ServerRole.SchedulingPublisher, 1, false)]
    [InlineData(ServerRole.SchedulingPublisher, 2, true)]
    [InlineData(ServerRole.Subscriber, 1, true)]
    [InlineData(ServerRole.Unknown, 3, true)]
    public void DetectLoadBalancing_RoleAndActiveServers_TellsWhetherOtherServersExist(
        ServerRole role,
        int activeServers,
        bool expected
    )
    {
        // Act
        bool loadBalanced = ServerInfo.DetectLoadBalancing(role, activeServers);

        // Assert
        Assert.Equal(expected, loadBalanced);
    }

    [Fact]
    public void GetServer_TwoActiveRegistrations_ReportsThisServerAsLoadBalanced()
    {
        // Arrange
        _roleAccessor.CurrentServerRole.Returns(ServerRole.SchedulingPublisher);
        _registrations
            .GetActiveServers()
            .Returns([
                Substitute.For<IServerRegistration>(),
                Substitute.For<IServerRegistration>(),
            ]);

        // Act
        ServerInfo server = Get();

        // Assert
        Assert.Equal(
            new ServerInfo(Environment.MachineName, ServerRole.SchedulingPublisher, 2, true),
            server
        );
    }

    [Fact]
    public void GetServer_RegistrationsCannotBeRead_CountsOnlyThisServer()
    {
        // Arrange
        _roleAccessor.CurrentServerRole.Returns(ServerRole.Single);
        _registrations.GetActiveServers().Throws(new InvalidOperationException("No database"));

        // Act
        ServerInfo server = Get();

        // Assert
        Assert.Equal((1, false), (server.ActiveServers, server.IsLoadBalanced));
    }

    private ServerInfo Get() =>
        Assert.IsType<ServerInfo>(
            Assert
                .IsType<OkObjectResult>(
                    new GetServerController(_roleAccessor, _registrations).GetServer()
                )
                .Value
        );
}
