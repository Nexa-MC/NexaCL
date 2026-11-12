using System.Collections.Immutable;
using System.Globalization;
using Nexa.Plugin.Bootstrap.V1;
using Nexa.Plugin.Hosting;
using Nexa.Plugin.Sdk.Abi100.Data;
using Nexa.Services.Minecraft.Management;

namespace Nexa.Services.Composition.Plugins;

public sealed partial class PluginBusinessRoutes
{
    private IEnumerable<HostCapabilityRoute> ContentGraphRoutes => [
        HostRoutes.Typed<EntityRequest, ContentDependencyGraph>("Minecraft.Content", "ReadDependencyGraph", async (c, v) =>
        {
            var snapshot = await Management(c, v.Id, c.Options.Cancellation, graph: true).ConfigureAwait(false);
            var graph = snapshot.ContentGraph;
            if (graph is null || !graph.Complete) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
            if (graph.Nodes.Count > 512 || graph.Nodes.Sum(n => n.Dependencies.Sum(d => Math.Max(1, d.Providers.Count))) > 2048)
                throw new AbiHostException(AbiErrors.MESSAGE_TOO_LARGE);
            EntityId Id(int key) => new(Token(c, "graph:" + v.Id.Value + ":" + key.ToString(CultureInfo.InvariantCulture)));
            var nodes = graph.Nodes.Select(n => new ContentItem(Id(n.Key), v.Id, new(n.NestedCandidate ? "nexa:dependency/nested" : "nexa:dependency/mod"), n.Id,
                new(new(new("nexa:mod-metadata"), n.Id), n.Version), n.Enabled, new(Token(c, "graph-node:" + n.Id + ":" + n.Version + ":" + n.Enabled)))).ToList();
            var edges = ImmutableArray.CreateBuilder<ContentDependency>();
            var synthetic = new Dictionary<string, EntityId>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
                foreach (var dependency in node.Dependencies)
                {
                    if (dependency.Providers.Count > 0)
                    {
                        foreach (int provider in dependency.Providers)
                        {
                            if (!graph.Nodes.Any(n => n.Key == provider)) throw new AbiHostException(AbiErrors.CAPABILITY_UNAVAILABLE);
                            edges.Add(new(Id(node.Key), Id(provider), true, false));
                        }
                    }
                    else
                    {
                        string key = dependency.State + ":" + dependency.Id;
                        if (!synthetic.TryGetValue(key, out var id))
                        {
                            id = new(Token(c, "dependency:" + v.Id.Value + ":" + key)); synthetic.Add(key, id);
                            nodes.Add(new(id, v.Id, new("nexa:dependency/" + dependency.State.ToString().ToLowerInvariant()), dependency.Id, null,
                                dependency.State == InstanceDependencyState.Runtime, new(Token(c, "dependency:" + key))));
                        }
                        edges.Add(new(Id(node.Key), id, true, false));
                    }
                }
            var result = new ContentDependencyGraph(v.Id, nodes.ToImmutableArray(), edges.ToImmutable(), new("pending"));
            return result with { Revision = ProjectionRevision(result) };
        })
    ];
}
