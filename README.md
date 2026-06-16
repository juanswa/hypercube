# Local AI-Enhanced Rollup Engine

Version: 1.0

## Purpose

Build a high-performance, streaming rollup engine for .NET that:
- Aggregates large volumes of data in memory
- Automatically spills to disk (LiteDB) when cardinality exceeds thresholds
- Provides advanced statistical analysis
- Integrates a local AI module for anomaly detection, trend analysis, and narrative generation
- Runs fully offline with no cloud dependencies

## Project Structure

- `requirements/` - specs and requirement documents
- `src/` - implementation code
- `tests/` - automated tests

## Core Components

- `RollupEngine` for streaming ingestion, rollups, spill management, and snapshot derivation
- `DimensionStore<T>` for backend-agnostic state storage with automatic spillover
- `IStateBackend<T>` with `InMemoryBackend<T>` and `LiteDbBackend<T>` implementations
- Streaming aggregators: Count, Sum, Min/Max, Histograms, Percentiles, HyperLogLog, Count-Min Sketch, EWMA, and Z-score anomaly detection
- Derivers and sanitizers for generating summary rows and normalizing raw inputs

## Local AI Engine

The local AI module runs fully offline (ONNX or GGML) to:
- Detect anomalies
- Identify trends
- Score unusual patterns
- Generate narrative summaries

### API

```csharp
interface ILocalAiEngine
{
    AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot);
    string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis);
}
```

## Deliverables

- RollupEngine library (NuGet)
- LiteDB backend
- Local AI engine with sample ONNX models
- CLI demo tool
- Documentation and examples
