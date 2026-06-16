namespace Hypercube.State.Parquet;

internal static class ParquetCellColumns
{
    public const string KeyColumn = "key";
    public const string LastAccessColumn = "last_access_ticks";
    public const string ScalarColumnPrefix = "v_";
    public const string SketchColumnPrefix = "sk_";
}
