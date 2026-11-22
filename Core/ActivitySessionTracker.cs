namespace ActivityLedger.Core;

/// <summary>
/// Partitions foreground observations into non-overlapping sessions. Observations should arrive once a second.
/// A gap longer than ten seconds ends the previous session at its last observation, excluding suspend time.
/// Checkpoints update the same database row, so a crash loses at most five observed seconds.
/// </summary>
public sealed class ActivitySessionTracker(Action<ActivityRecord> save)
{
    private static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(5);
    private ActivityRecord? current;
    private ActivitySample? identity;
    private DateTimeOffset? lastObservation;
    private DateTimeOffset lastCheckpoint;

    public ActivityRecord? Current => current;

    public void Observe(ActivitySample? sample, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        if (lastObservation is { } previous && (now - previous > MaximumGap || now < previous))
        {
            Commit();
            current = null;
            identity = null;
        }

        if (current is not null && now >= current.EndUtc) current = current with { EndUtc = now };
        if (sample != identity)
        {
            Commit();
            current = null;
            identity = sample;
            if (sample is not null)
            {
                current = new(Guid.NewGuid().ToString("N"), now, now,
                    sample.Kind, sample.ProcessName, sample.Title, sample.Url, sample.Domain);
                lastCheckpoint = now;
                // Make newly observed activity immediately visible in the dashboard.
                save(current);
            }
        }
        else if (current is not null && now - lastCheckpoint >= CheckpointInterval)
        {
            Commit();
            lastCheckpoint = now;
        }
        lastObservation = now;
    }

    /// <summary>Saves observed time only; callers should Observe before Flush if they have a fresh sample.</summary>
    public void Flush(DateTimeOffset now)
    {
        Commit();
    }

    public void Stop(DateTimeOffset now)
    {
        Observe(null, now);
        lastObservation = null;
    }

    private void Commit()
    {
        if (current is not null) save(current);
    }
}
