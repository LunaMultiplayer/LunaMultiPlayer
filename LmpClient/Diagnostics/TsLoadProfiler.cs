using System.Diagnostics;
using System.Text;
using System.Threading;

namespace LmpClient.Diagnostics
{
    /// <summary>
    /// Diagnostic-only profiler for the Tracking Station entry path. Counts
    /// invocations and accumulates wall-clock time spent inside the suspect
    /// hot spots (lock-acquire flood, <c>buildVesselsList</c> rebuild storm,
    /// KSC marker refreshes, etc.) and emits one consolidated
    /// <c>[LMP][TS-PROFILE]</c> line per second when any bucket has activity.
    ///
    /// Why this exists: the heartbeat in <see cref="MainSystem"/> proved the
    /// Unity main thread really does stall for tens of seconds when entering
    /// TRACKSTATION on a server with ~100+ vessels. We need a per-call-site
    /// breakdown to attribute the stall to a specific event handler before we
    /// pick a fix. A real Unity profiler is overkill (and unavailable in
    /// users' shipping installs), and ad-hoc <c>LunaLog.Log</c> calls per
    /// invocation would themselves dominate the timing for an event that
    /// fires N×N times. This profiler aggregates first, logs once per
    /// second, and skips the line entirely on quiet seconds.
    ///
    /// Design constraints:
    /// <list type="bullet">
    ///   <item>Cheap on the hot path: one <see cref="Stopwatch.GetTimestamp"/>
    ///         pair and two interlocked adds per call. No allocations, no
    ///         dictionary lookups, no managed locks.</item>
    ///   <item>Thread-safe: lock callbacks reach us on the Unity thread today
    ///         but the LMP message pipeline routes some events off the Unity
    ///         thread, so all accumulators are written via
    ///         <see cref="Interlocked"/> and read by snapshot+swap so a flush
    ///         can never race a concurrent <see cref="Record"/>.</item>
    ///   <item>Scoped: this is purely diagnostic. No production code path
    ///         depends on it. Removing the profiler is a matter of deleting
    ///         this file, the csproj entry, and the four call sites.</item>
    /// </list>
    /// </summary>
    public static class TsLoadProfiler
    {
        //One bucket per instrumented call site. Adding a new bucket is two
        //lines (a pair of static fields) plus one entry in AppendBucket. Using
        //named fields rather than a Dictionary<string, ...> keeps Record
        //allocation-free and lock-free on the call-count critical path.
        private static long _onLockAcquireCount;
        private static long _onLockAcquireTicks;

        private static long _onLockReleaseCount;
        private static long _onLockReleaseTicks;

        private static long _refreshTrackingStationVesselsCount;
        private static long _refreshTrackingStationVesselsTicks;

        private static long _refreshMarkersCount;
        private static long _refreshMarkersTicks;

        private static long _onVesselCreatedCount;
        private static long _onVesselCreatedTicks;

        private static long _vesselInitializedCount;
        private static long _vesselInitializedTicks;

        //Counts the actual coalesced rebuilds executed by KscSceneSystem's
        //LateUpdate flush. Without this bucket the per-event RefreshTS bucket
        //above would show ~0ms × N (just the dirty-flag write) and the real
        //post-debounce cost — typically 1 rebuild per frame regardless of how
        //many handlers fired into it — would be invisible.
        private static long _tsRebuildFlushCount;
        private static long _tsRebuildFlushTicks;

        public enum Bucket
        {
            OnLockAcquire,
            OnLockRelease,
            RefreshTrackingStationVessels,
            RefreshMarkers,
            OnVesselCreated,
            VesselInitialized,
            TsRebuildFlush,
        }

        /// <summary>
        /// Marks a single invocation of an instrumented call site. The caller
        /// captures <c>var t0 = <see cref="Stopwatch.GetTimestamp"/>();</c>
        /// before the work and passes the elapsed-tick delta in. We use raw
        /// stopwatch ticks (not milliseconds) so the call site avoids a
        /// floating-point divide — the conversion happens once per second
        /// inside the flush.
        /// </summary>
        public static void Record(Bucket bucket, long elapsedTicks)
        {
            switch (bucket)
            {
                case Bucket.OnLockAcquire:
                    Interlocked.Increment(ref _onLockAcquireCount);
                    Interlocked.Add(ref _onLockAcquireTicks, elapsedTicks);
                    break;
                case Bucket.OnLockRelease:
                    Interlocked.Increment(ref _onLockReleaseCount);
                    Interlocked.Add(ref _onLockReleaseTicks, elapsedTicks);
                    break;
                case Bucket.RefreshTrackingStationVessels:
                    Interlocked.Increment(ref _refreshTrackingStationVesselsCount);
                    Interlocked.Add(ref _refreshTrackingStationVesselsTicks, elapsedTicks);
                    break;
                case Bucket.RefreshMarkers:
                    Interlocked.Increment(ref _refreshMarkersCount);
                    Interlocked.Add(ref _refreshMarkersTicks, elapsedTicks);
                    break;
                case Bucket.OnVesselCreated:
                    Interlocked.Increment(ref _onVesselCreatedCount);
                    Interlocked.Add(ref _onVesselCreatedTicks, elapsedTicks);
                    break;
                case Bucket.VesselInitialized:
                    Interlocked.Increment(ref _vesselInitializedCount);
                    Interlocked.Add(ref _vesselInitializedTicks, elapsedTicks);
                    break;
                case Bucket.TsRebuildFlush:
                    Interlocked.Increment(ref _tsRebuildFlushCount);
                    Interlocked.Add(ref _tsRebuildFlushTicks, elapsedTicks);
                    break;
            }
        }

        /// <summary>
        /// Snapshots and resets every bucket atomically (per-bucket; the
        /// snapshot of the whole struct is not transactional but each bucket
        /// is, which is good enough for diagnostics) and returns a pre-built
        /// log line, or <c>null</c> if every bucket was idle in the snapshot
        /// window. Returning <c>null</c> on quiet seconds keeps the heartbeat
        /// log readable when nothing TS-related is happening.
        ///
        /// Returns the pre-built string rather than calling
        /// <see cref="LunaLog"/> directly so the heartbeat stays the single
        /// owner of all diagnostic emission and there's no risk of the
        /// profiler ever logging from a non-Unity thread by accident.
        /// </summary>
        public static string FlushSnapshotOrNull()
        {
            var onLockAcquireCount = Interlocked.Exchange(ref _onLockAcquireCount, 0);
            var onLockAcquireTicks = Interlocked.Exchange(ref _onLockAcquireTicks, 0);
            var onLockReleaseCount = Interlocked.Exchange(ref _onLockReleaseCount, 0);
            var onLockReleaseTicks = Interlocked.Exchange(ref _onLockReleaseTicks, 0);
            var refreshTsCount = Interlocked.Exchange(ref _refreshTrackingStationVesselsCount, 0);
            var refreshTsTicks = Interlocked.Exchange(ref _refreshTrackingStationVesselsTicks, 0);
            var refreshMarkersCount = Interlocked.Exchange(ref _refreshMarkersCount, 0);
            var refreshMarkersTicks = Interlocked.Exchange(ref _refreshMarkersTicks, 0);
            var onVesselCreatedCount = Interlocked.Exchange(ref _onVesselCreatedCount, 0);
            var onVesselCreatedTicks = Interlocked.Exchange(ref _onVesselCreatedTicks, 0);
            var vesselInitializedCount = Interlocked.Exchange(ref _vesselInitializedCount, 0);
            var vesselInitializedTicks = Interlocked.Exchange(ref _vesselInitializedTicks, 0);
            var tsRebuildFlushCount = Interlocked.Exchange(ref _tsRebuildFlushCount, 0);
            var tsRebuildFlushTicks = Interlocked.Exchange(ref _tsRebuildFlushTicks, 0);

            var totalCount = onLockAcquireCount + onLockReleaseCount + refreshTsCount +
                             refreshMarkersCount + onVesselCreatedCount + vesselInitializedCount +
                             tsRebuildFlushCount;
            if (totalCount == 0)
                return null;

            var sb = new StringBuilder(256);
            sb.Append("[LMP][TS-PROFILE]");
            AppendBucket(sb, "OnLockAcquire", onLockAcquireCount, onLockAcquireTicks);
            AppendBucket(sb, "OnLockRelease", onLockReleaseCount, onLockReleaseTicks);
            AppendBucket(sb, "RefreshTS", refreshTsCount, refreshTsTicks);
            AppendBucket(sb, "RefreshMarkers", refreshMarkersCount, refreshMarkersTicks);
            AppendBucket(sb, "OnVesselCreated", onVesselCreatedCount, onVesselCreatedTicks);
            AppendBucket(sb, "VesselInitialized", vesselInitializedCount, vesselInitializedTicks);
            AppendBucket(sb, "TsRebuildFlush", tsRebuildFlushCount, tsRebuildFlushTicks);
            return sb.ToString();
        }

        /// <summary>
        /// Appends <c>" Name=N (Xms)"</c> when the bucket fired at least once
        /// in the snapshot window, otherwise nothing. Keeps the per-second
        /// line short and focused on the buckets that actually contributed
        /// to the latency.
        /// </summary>
        private static void AppendBucket(StringBuilder sb, string name, long count, long ticks)
        {
            if (count == 0) return;
            //Stopwatch ticks → ms via Stopwatch.Frequency. The (double) cast
            //happens once per active bucket per second, not per call.
            var ms = ticks * 1000.0 / Stopwatch.Frequency;
            sb.Append(' ');
            sb.Append(name);
            sb.Append('=');
            sb.Append(count);
            sb.Append(" (");
            sb.Append(ms.ToString("F1"));
            sb.Append("ms)");
        }
    }
}
