namespace Hypercube.Core;

/// <summary>Disk spill format used when cardinality exceeds memory limits.</summary>
public enum SpillBackendKind
{
    /// <summary>Embedded LiteDB document store (default).</summary>
    LiteDb,

    /// <summary>Columnar Parquet files optimized for metric scans.</summary>
    Parquet
}
