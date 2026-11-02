namespace MikuSB.Util;

/// <summary>
///     Keeps a second copy of the server from starting next to the first one. Two instances fight over the same
///     seven HTTPS ports and the admin panel, and the loser used to surface as "the SDK server failed to start"
///     instead of "it is already running".
/// </summary>
/// <remarks>
///     The mutex is deliberately never released or disposed: the OS frees it when the process exits, and a
///     <see cref="System.Threading.Mutex" /> released by the GC would silently unlock while the server still runs.
///     The updater restarts the exe only after the old process is gone (<c>--restart</c> waits on the pid), so the
///     hand-over does not trip this check; the short acquire timeout covers a slow teardown.
/// </remarks>
public static class SingleInstance
{
    private const string GlobalMutexName = @"Global\MikuSB.Server";
    private const string LocalMutexName = @"Local\MikuSB.Server";

    // Held for the process lifetime on purpose - see the remarks above.
    private static Mutex? _held;

    /// <summary>
    ///     Try to become the one running server. Returns <c>false</c> only when another instance demonstrably holds
    ///     the mutex; an environment where the mutex cannot be created at all still returns <c>true</c> so that a
    ///     sandbox or a locked-down session is never mistaken for a duplicate.
    /// </summary>
    public static bool TryAcquire(TimeSpan timeout)
    {
        // Global scope catches instances started from another session (RDP, a second logged-in user); the local
        // scope is the fallback for accounts without SeCreateGlobalPrivilege.
        foreach (var name in new[] { GlobalMutexName, LocalMutexName })
        {
            Mutex mutex;
            try
            {
                mutex = new Mutex(false, name);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died without releasing it: we own it now.
                acquired = true;
            }
            catch (Exception)
            {
                acquired = false;
            }

            if (acquired)
            {
                _held = mutex;
                return true;
            }

            mutex.Dispose();
            return false;
        }

        return true;
    }
}
