using System.Text.Json.Serialization;
using MikuSB.GameServer.Game.Quest;
using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Chapter;

[CallGSApi("Chapter_GetStarAward")]
public sealed class Chapter_GetStarAward : CallGSHandler<ChapterStarAwardParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ChapterStarAwardParam request)
    {
        if (request.ChapterId == 0 || request.Difficult == 0)
            return CallGSResult.Error("error.BadParam");

        var result = await context.Player.QuestManager.ClaimChapterStarAwardsAsync(
            request.IsMain,
            request.Difficult,
            request.ChapterId,
            request.AwardIndex);
        if (result == null)
            return CallGSResult.Error("error.BadParam");

        return CallGSResult.Ok(result.Value.Response, result.Value.Sync);
    }
}

public sealed class ChapterStarAwardParam
{
    [JsonPropertyName("bMain")]
    public bool IsMain { get; set; }

    [JsonPropertyName("nDifficult")]
    public uint Difficult { get; set; }

    [JsonPropertyName("nChapterID")]
    public uint ChapterId { get; set; }

    [JsonPropertyName("nIndex")]
    public int AwardIndex { get; set; }
}
