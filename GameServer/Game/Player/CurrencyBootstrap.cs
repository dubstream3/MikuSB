using MikuSB.Data;

namespace MikuSB.GameServer.Game.Player;

/// <summary>
/// Initial currency balances handed to an account the first time it logs in after the economy
/// rework. The old bootstrap pinned every money id in 1..200 to 999_999_999 and merged it with a
/// "raise only" rule on every snapshot (PlayerInstance.BuildPlayerAttr), which made every currency
/// effectively infinite and silently undid any charge. The values below are meant to be spent:
/// the primary free currencies are comfortable, tokens and event coins are pocket money, and the
/// client can now actually run out.
///
/// The seed runs once per account (<see cref="Database.Player.PlayerGameData.CurrencySeeded"/>) so
/// later logins keep whatever the player spent or earned.
/// </summary>
public static class CurrencyBootstrap
{
    public const uint DefaultAmount = 1_000_000;

    /// <summary>moneyType -> starting amount.</summary>
    private static readonly Dictionary<uint, uint> InitialAmounts = new()
    {
        // Primary currencies (cash.json: 1 MONEY, 2 GOLD, 3 SILVER, 4 VIGOR, 5 TOKEN, 6 COIN, 7 FRAGMENT)
        [1] = 60_000,
        [2] = 120_000,
        [3] = 30_000_000,
        [5] = 20_000,
        [6] = 500_000,
        [7] = 50_000,
        [8] = 120_000,

        // DLC / mode tokens
        [9] = 5_000,
        [10] = 5_000,
        [11] = 5_000,
        [12] = 5_000,
        [13] = 5_000,
        [14] = 5_000,
        [23] = 5_000,
        [24] = 5_000,
        [25] = 20_000,
        [26] = 20_000,
        [27] = 5_000,
        [28] = 5_000,
        [29] = 5_000,
        [30] = 5_000,
        [41] = 5_000,
        [42] = 5_000,
        [51] = 5_000,
        [52] = 5_000,
        [53] = 5_000,
        [54] = 5_000,
        [55] = 5_000,
        [56] = 5_000,
        [57] = 5_000,
        [58] = 5_000,
        [61] = 5_000,
        [62] = 5_000,
        [63] = 5_000,
    };

    /// <summary>31..50 are the Rogue3D season keys (cash.json ROGUE3SEASONKEY1..9).</summary>
    private static uint AmountFor(uint moneyType)
    {
        if (InitialAmounts.TryGetValue(moneyType, out var amount))
            return amount;

        return moneyType is >= 31 and <= 50 ? 2_000 : DefaultAmount;
    }

    /// <summary>
    /// Writes the starting balance for every money id the client can read, unless the attribute
    /// already exists (a returning player keeps their balance) or the account was already seeded.
    /// </summary>
    public static int Seed(PlayerInstance player)
    {
        if (player.Data.CurrencySeeded)
            return 0;

        var seeded = 0;
        for (uint moneyId = 1; moneyId <= 200; moneyId++)
        {
            if (moneyId == AttrIds.Currency.Vigor)
                continue; // Vigor is a player field (PlayerGameData.Vigor), not a group 1 attribute.

            var sid = AttrIds.Currency.GetSid(moneyId);
            if (player.Attributes.Get(AttrIds.Currency.GroupId, sid) != null)
                continue;

            player.Attributes.Set(AttrIds.Currency.GroupId, sid, AmountFor(moneyId));
            seeded++;
        }

        player.Data.CurrencySeeded = true;
        return seeded;
    }
}
