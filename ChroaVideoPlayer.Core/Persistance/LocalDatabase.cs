using System;
using System.IO;

namespace ChroaVideoPlayer.Core.Persistance;

public static class LocalDatabase
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChroaVideoPlayer",
        "chroa.db");
}
