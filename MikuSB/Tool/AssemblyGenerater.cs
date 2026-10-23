using MikuSB.Util;
using System.Reflection;

namespace MikuSB.MikuSB.Tool;

public class AssemblyGenerater
{
    private static readonly string SourceSpace = "MikuSB.MikuSB.Source.";

    public static async ValueTask LoadCustomData(Assembly assembly)
    {
        string[] embededRes = assembly.GetManifestResourceNames();
        foreach (var res in embededRes)
        {
            var stream = assembly.GetManifestResourceStream(res);
            if (stream != null && res.Contains(ConfigManager.Config.Path.DataPath.Split("/").Last()))
                await WriteOutputFiles(stream, res);
        }
    }

    private async static ValueTask WriteOutputFiles(Stream stream, string resSpace)
    {
        if (stream == null) return;

        string relativePath = resSpace.Replace(SourceSpace, "");
        int lastDotIndex = relativePath.LastIndexOf('.');
        string outputPath = string.Concat(
            ConfigManager.Config.Path.ConfigPath, "/",
            relativePath[..lastDotIndex].Replace('.', '/'),
            relativePath.AsSpan(lastDotIndex));

        if (File.Exists(outputPath)) return; // Check if file exist

        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        stream.Position = 0;
        await stream.CopyToAsync(fileStream);
    }
}