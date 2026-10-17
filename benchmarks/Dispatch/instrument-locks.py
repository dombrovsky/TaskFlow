"""Intrusive coordinator-lock timing for disposable worktrees only; never use these binaries for throughput claims."""
from pathlib import Path
import sys
root = Path(sys.argv[1])
p = root / 'TaskFlow/Internal/KeyedCoordinator.cs'
s = p.read_text()
assert 'ReviewLockSummary' not in s
s = s.replace('lock (_sync)', 'using (MeasureLock())')
needle = '    private readonly object _sync = new object();'
s = s.replace(needle, needle + '\n    private readonly Dictionary<string, LockMetric> _lockMetrics = new Dictionary<string, LockMetric>(StringComparer.Ordinal);')
needle = '    public override bool RequiresKey => true;'
s = s.replace(needle, '''    internal string ReviewLockSummary
    {
        get
        {
            lock (_sync)
                return "frequency=" + System.Diagnostics.Stopwatch.Frequency + ";" + string.Join(";",
                    System.Linq.Enumerable.Select(_lockMetrics, pair => System.FormattableString.Invariant(
                        $"{pair.Key},{pair.Value.Count},{pair.Value.Wait},{pair.Value.Held}")));
        }
    }
    private LockScope MeasureLock([System.Runtime.CompilerServices.CallerMemberName] string member = "") => new LockScope(this, member);
    private sealed class LockMetric
    {
        public long Count;
        public long Wait;
        public long Held;
    }
    private readonly struct LockScope : IDisposable
    {
        private readonly KeyedCoordinator<TKey> _owner;
        private readonly LockMetric _metric;
        private readonly long _wait;
        private readonly long _start;
        public LockScope(KeyedCoordinator<TKey> owner, string member)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Monitor.Enter(owner._sync);
            _wait = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            _owner = owner;
            if (!owner._lockMetrics.TryGetValue(member, out var metric))
            {
                metric = new LockMetric();
                owner._lockMetrics.Add(member, metric);
            }
            _metric = metric;
            _start = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            var held = System.Diagnostics.Stopwatch.GetTimestamp() - _start;
            _metric.Count++;
            _metric.Wait += _wait;
            _metric.Held += held;
            Monitor.Exit(_owner._sync);
        }
    }
''' + needle)
p.write_text(s)
p = root / 'TaskFlow/Internal/TaskSchedulerPipeline.cs'
s = p.read_text()
s = s.replace('    internal TaskSchedulerMiddlewareLifetime? Lifetime { get; }', '''    internal TaskSchedulerMiddlewareLifetime? Lifetime { get; }
    internal string? ReviewLockSummary => (Coordinator as KeyedCoordinator<int>)?.ReviewLockSummary;''')
p.write_text(s)