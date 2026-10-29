namespace MikuSB.Data;

/// <summary>
/// DLC eras the server refuses to serve.
///
/// Eras 1-4 shipped with the caps/UI assets of the 1.x client, and those assets are not in the
/// resource packs the server is built against any more (no key art, no gallery entries, no trial
/// activities: <c>activities.json</c> has no rows for them and <c>UI/Picture/Localization</c> only
/// keeps 324-byte placeholders). Serving one of them seeds the era attributes and pushes an era
/// window while the client has nothing to draw, so the DLC page comes up empty and every entry in
/// it errors out. They are therefore withdrawn: the command rejects them, the admin panel keeps the
/// cards as a record of what used to exist but cannot switch to them.
/// </summary>
public static class DlcEraPolicy
{
    private static readonly uint[] retired = [1u, 2u, 3u, 4u];

    /// <summary>Ids that are no longer served, in ascending order.</summary>
    public static IReadOnlyList<uint> Retired => retired;

    /// <summary>True when <paramref name="id"/> must not be served (0, the "no era" id, is never retired).</summary>
    public static bool IsRetired(uint id)
    {
        foreach (var retiredId in retired)
        {
            if (retiredId == id)
                return true;
        }

        return false;
    }

    /// <summary>"1, 2, 3, 4" - for command replies and log lines.</summary>
    public static string RetiredList => string.Join(", ", retired);
}
