# Polly documentation validation

This test project compiles the examples in [the Polly integration guide](../docs/integrations/polly.md).
The guide's seven marked snippets and shared imports must match `PollyExamples.cs` verbatim;
`GuideSnippets_MatchCompiledSource` checks this in CI. Edit the source and guide together.

Run with the .NET 10 SDK and .NET 8/10 runtimes installed:

```shell
dotnet test TaskFlow.Documentation.Tests -c Release
```

The fixture exercises every public example, retry filtering/counts, FIFO versus resubmission,
keyed admission and concurrency, caller/shutdown cancellation during backoff, both timeout
scopes, cancellation-ignoring callbacks, and dedicated-thread capture. Behavioral delays use
fake time; gates and timer-registration signals establish ordering. Real-time limits only
guard against hung tests. Polly dependencies belong to this non-packable test project.
