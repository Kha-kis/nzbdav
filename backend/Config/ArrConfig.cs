using NzbWebDAV.Clients.RadarrSonarr;

namespace NzbWebDAV.Config;

public class ArrConfig
{
    public List<ConnectionDetails> RadarrInstances { get; set; } = [];
    public List<ConnectionDetails> SonarrInstances { get; set; } = [];
    public List<QueueRule> QueueRules { get; set; } = [];

    // Skip instances with a blank Host. ArrClient builds its request URI as $"{Host}{path}", so a
    // blank Host produces an empty URI string and `new Uri("")` throws "Invalid URI: The URI is
    // empty." That surfaced as health-check repairs failing with RepairStatus=ActionNeeded rather
    // than as a config error, which is a confusing place to learn an instance was misconfigured.
    // ReSharper disable once InvokeAsExtensionMethod
    public IEnumerable<ArrClient> GetArrClients() => Enumerable.Concat(
        RadarrInstances.Where(IsUsable).Select(ArrClient (x) => new RadarrClient(x.Host, x.ApiKey)),
        SonarrInstances.Where(IsUsable).Select(ArrClient (x) => new SonarrClient(x.Host, x.ApiKey))
    );

    private static bool IsUsable(ConnectionDetails x) => !string.IsNullOrWhiteSpace(x.Host);

    public int GetInstanceCount() =>
        RadarrInstances.Count + SonarrInstances.Count;

    public class ConnectionDetails
    {
        public required string Host { get; set; }
        public required string ApiKey { get; set; }
    }

    public class QueueRule
    {
        public string Message { get; set; } = null!;
        public QueueAction Action { get; set; }
    }

    public enum QueueAction
    {
        DoNothing = 0,
        Remove = 1,
        RemoveAndBlocklist = 2,
        RemoveAndBlocklistAndSearch = 3
    }
}