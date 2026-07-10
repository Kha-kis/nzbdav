using NzbWebDAV.Config;

namespace backend.Tests.Config;

/// <summary>
/// ArrClient builds its request URI as $"{Host}{path}". A blank Host yields an empty URI string and
/// `new Uri("")` throws "Invalid URI: The URI is empty." That surfaced as health-check repairs
/// failing with RepairStatus=ActionNeeded instead of as a configuration error.
/// </summary>
public class ArrConfigTests
{
    private static ArrConfig.ConnectionDetails Details(string host) =>
        new() { Host = host, ApiKey = "key" };

    [Fact]
    public void Instances_with_a_usable_host_become_clients()
    {
        var config = new ArrConfig
        {
            RadarrInstances = [Details("http://radarr:7878")],
            SonarrInstances = [Details("http://sonarr:8989")],
        };

        Assert.Equal(2, config.GetArrClients().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Instances_with_a_blank_host_are_skipped(string host)
    {
        var config = new ArrConfig
        {
            RadarrInstances = [Details(host)],
            SonarrInstances = [Details(host)],
        };

        Assert.Empty(config.GetArrClients());
    }

    [Fact]
    public void A_blank_instance_does_not_suppress_a_usable_one()
    {
        var config = new ArrConfig
        {
            SonarrInstances = [Details(""), Details("http://sonarr:8989")],
        };

        var clients = config.GetArrClients().ToList();
        Assert.Single(clients);
        Assert.Equal("http://sonarr:8989", clients[0].Host);
    }
}
