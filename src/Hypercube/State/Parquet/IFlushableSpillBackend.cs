namespace Hypercube.State.Parquet;

/// <summary>Spill backend that supports a synchronous durability flush.</summary>
public interface IFlushableSpillBackend
{
    /// <summary>Writes pending in-memory state to disk synchronously.</summary>
    void FlushNow();
}
