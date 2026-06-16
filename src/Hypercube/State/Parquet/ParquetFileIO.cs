namespace Hypercube.State.Parquet;

internal static class ParquetFileIO
{
    public static FileStream OpenReadShared(string filePath) =>
        File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    public static FileStream CreateExclusive(string filePath) =>
        File.Create(filePath);
}
