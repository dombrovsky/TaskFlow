"""Count original Task.Run(Pump) calls in a disposable worktree, without changing scheduling."""
from pathlib import Path
import sys
root = Path(sys.argv[1])
for name in ('BoundedAdmissionMiddleware.cs', 'KeyedCoordinator.cs'):
    path = root / 'TaskFlow/Internal' / name
    text = path.read_text()
    assert '_scheduledDrains' not in text
    needle = '    private bool _pumping;'
    text = text.replace(needle, '    private int _scheduledDrains;\n    internal int ScheduledDrainCount => Volatile.Read(ref _scheduledDrains);\n' + needle)
    needle = '            _ = Task.Run(Pump, CancellationToken.None);'
    assert needle in text
    text = text.replace(needle, '        {\n            Interlocked.Increment(ref _scheduledDrains);\n' + needle + '\n        }')
    path.write_text(text)
path = root / 'TaskFlow/Internal/TaskSchedulerPipeline.cs'
text = path.read_text()
needle = '    internal TaskSchedulerMiddlewareLifetime? Lifetime { get; }'
assert needle in text
text = text.replace(needle, needle + '''
    internal int ScheduledDrainCount
    {
        get
        {
            var count = Coordinator is KeyedCoordinator<int> keyed ? keyed.ScheduledDrainCount : 0;
            foreach (var registration in _registrations)
                if (registration.Middleware is BoundedAdmissionMiddleware bounded)
                    count += bounded.ScheduledDrainCount;
            return count;
        }
    }
''')
path.write_text(text)