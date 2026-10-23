using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Activity;

/// <summary>
/// 心愿寄语 (the DLC35 "Dorm" entry, DlcFuncDorm.lua:1 - "心愿寄语") server-side state. The whole
/// module is plain attributes on gid 72 (Activity/RoleFestivalMessage.lua:9-27):
///
///   (72, 1)              act id, seeded at login; GetIsInActTime() is literally "~= 0" (:41)
///   (72, 1000 + NpcID)   1 = the letter is listed (GetMessageOpenId, :271-286)
///   (72, 3000 + NpcID)   1 = the letter has been read (GetBeReadCount, :200-219)
///   (72, 100 + MessageNum) 1 = the reward tier has been claimed (:90-100, umg_role_festival_reward)
///
/// Only (72, 3000 + NpcID) is written by a client request (SetMessageReadState, :222-227). Nothing
/// on the client ever opens a letter, so a fresh account sees an empty board: the open flags have to
/// come from the server. The shipped UnlockNum arrays (ActivityTime.json: [3,3,3,3,4,5] for the
/// current act) describe a staged release - three letters, then three more per read milestone - but
/// the milestones are exactly the cumulative MessageNum of the reward tiers (3/6/9/12/16/21), i.e.
/// the staging exists to pace the same six rewards. A private server has no reason to gate them, so
/// every letter of the act is opened at once and the whole reward chain stays reachable.
/// </summary>
public static class RoleFestivalService
{
    public const uint GroupId = AttrIds.Activity.RoleFestivalGroupId;             // 72
    public const uint ActSid = AttrIds.Activity.RoleFestivalActSid;               // 1
    private const uint ContentStartSid = AttrIds.Activity.RoleFestivalContentStartSid;   // 1000
    private const uint ReadStateStartSid = AttrIds.Activity.RoleFestivalReadStateStartSid; // 3000
    private const uint RewardStartSid = AttrIds.Activity.RoleFestivalLevelRewardStartSid;  // 100

    public static uint GetActId(PlayerInstance player) => player.Attributes.GetValue(GroupId, ActSid);

    /// <summary>Attribute that lists a letter once it is 1 (ContentStartId + NpcID).</summary>
    public static uint ContentSid(uint npcId) => ContentStartSid + npcId;

    /// <summary>Attribute that marks a letter as read (BeReadStateStartId + NpcID).</summary>
    public static uint ReadSid(uint npcId) => ReadStateStartSid + npcId;

    /// <summary>Attribute that marks a reward tier as claimed (LevelRewardGetState + MessageNum).</summary>
    public static uint RewardSid(uint messageNum) => RewardStartSid + messageNum;

    public static List<RoleFestivalContentExcel> MessagesOf(uint actId)
    {
        if (actId == 0)
            return [];

        return GameData.RoleFestivalContentData.Values
            .Where(row => row.ActId == actId)
            .OrderBy(row => row.Npc)
            .ToList();
    }

    /// <summary>
    /// Opens every normal letter of the act (彩蛋 letters are listed by the UI without a flag,
    /// umg_role_festival_message.lua:196-198). Returns how many flags were flipped.
    /// </summary>
    public static int OpenMessages(PlayerInstance player, uint actId, NtfSyncPlayer? sync = null)
    {
        if (actId == 0)
            return 0;

        var opened = 0;
        foreach (var row in MessagesOf(actId))
        {
            if (row.IsEasterEgg != 0)
                continue;

            if (player.Attributes.GetValue(GroupId, ContentSid(row.Npc)) == 1)
                continue;

            var attr = player.Attributes.Set(GroupId, ContentSid(row.Npc), 1);
            if (sync != null)
                player.Attributes.SyncTo(sync, attr);

            opened++;
        }

        return opened;
    }

    /// <summary>
    /// Number of letters of the act marked as read. The client counts the same set
    /// (RoleFestivalMessage.lua:200-219, EasterEgg rows included) and refuses a claim while the count
    /// is below the tier's MessageNum, so the server check has to match it.
    /// </summary>
    public static int CountRead(PlayerInstance player, uint actId)
    {
        if (actId == 0)
            return 0;

        var count = 0;
        foreach (var row in MessagesOf(actId))
        {
            if (player.Attributes.GetValue(GroupId, ReadSid(row.Npc)) == 1)
                count++;
        }

        return count;
    }

    /// <summary>Letters of the act that the player actually has access to (opened ones).</summary>
    public static bool IsMessageOfAct(uint actId, uint npcId)
    {
        return actId != 0
               && GameData.RoleFestivalContentData.TryGetValue(actId * 1000 + npcId, out _);
    }

    public static RoleFestivalRewardExcel? FindReward(uint actId, uint messageNum)
    {
        return GameData.RoleFestivalRewardData.TryGetValue(actId * 1000 + messageNum, out var row)
            ? row
            : null;
    }
}
