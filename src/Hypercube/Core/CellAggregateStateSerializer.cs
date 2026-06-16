using Hypercube.Core.Sketches;
using LiteDB;

namespace Hypercube.Core;

/// <summary>
/// Thread-safe BSON conversion for <see cref="CellAggregateState"/> used by <see cref="State.LiteDbBackend{TValue}"/>.
/// </summary>
internal static class CellAggregateStateSerializer
{
    public static void Register(BsonMapper mapper)
    {
        mapper.RegisterType<CellAggregateState>(
            serialize: state =>
            {
                lock (state.Sync)
                {
                    return new BsonDocument
                    {
                        ["MetricValues"] = new BsonArray(state.MetricValues.Select(static v => new BsonValue(v))),
                        ["SketchStates"] = new BsonArray(MaterializeSketchStates(state).Select(static bytes => new BsonValue(bytes)))
                    };
                }
            },
            deserialize: bson =>
            {
                var document = bson.AsDocument;
                var metricValues = document["MetricValues"].AsArray.Select(static value => value.AsDouble).ToArray();
                var sketchStates = document["SketchStates"].AsArray.Select(static value => value.AsBinary).ToArray();
                return new CellAggregateState
                {
                    MetricValues = metricValues,
                    SketchStates = sketchStates,
                    ActiveSketches = []
                };
            });
    }

    public static CellAggregateState Snapshot(CellAggregateState state)
    {
        lock (state.Sync)
        {
            return new CellAggregateState
            {
                MetricValues = (double[])state.MetricValues.Clone(),
                SketchStates = MaterializeSketchStates(state),
                ActiveSketches = []
            };
        }
    }

    private static byte[][] MaterializeSketchStates(CellAggregateState state)
    {
        var length = Math.Max(state.ActiveSketches.Length, state.SketchStates.Length);
        var sketchStates = new byte[length][];
        for (var i = 0; i < length; i++)
        {
            if (state.ActiveSketches.Length > i && state.ActiveSketches[i] is TDigestState digest)
            {
                sketchStates[i] = digest.Serialize();
                continue;
            }

            if (state.ActiveSketches.Length > i && state.ActiveSketches[i] is HyperLogLogState hll)
            {
                sketchStates[i] = hll.Serialize();
                continue;
            }

            sketchStates[i] = state.SketchStates.Length > i ? state.SketchStates[i] ?? [] : [];
        }

        return sketchStates;
    }
}
