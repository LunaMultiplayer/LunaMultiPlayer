using ByteSizeLib;
using Server.Log;
using Server.System;
using Server.System.Waypoint;
using System;
using System.IO;
using System.Linq;

namespace Server.Context
{
    public class Universe
    {
        /// <summary>
        /// Gets the universe folder size. Caution! This method is not thread safe!
        /// </summary>
        private static double GetUniverseSize()
        {
            if (!Directory.Exists(ServerContext.UniverseDirectory))
                return 0;

            var size = ByteSize.FromBytes(Directory.GetFiles(ServerContext.UniverseDirectory, "*.*")
                .Select(f => new FileInfo(f)).Select(i => i.Length).Sum()).KiloBytes;

            return Math.Round(size, 3);
        }

        /// <summary>
        /// Create universe directories
        /// </summary>
        public static void CheckUniverse()
        {
            LunaLog.Debug($"Loading universe... {GetUniverseSize()}{ByteSize.KiloByteSymbol}");

            if (!FileHandler.FolderExists(ServerContext.ConfigDirectory))
                FileHandler.FolderCreate(ServerContext.ConfigDirectory);
            if (!FileHandler.FileExists(ServerContext.ModFilePath))
                if (FileHandler.FileExists(ServerContext.OldModFilePath))
                    FileHandler.MoveFile(ServerContext.OldModFilePath, ServerContext.ModFilePath);
                else
                    ModFileSystem.GenerateNewModFile();
            else
                // Cleanup
                FileHandler.FileDelete(ServerContext.OldModFilePath);
            if (!FileHandler.FolderExists(ServerContext.UniverseDirectory))
                FileHandler.FolderCreate(ServerContext.UniverseDirectory);

            if (!FileHandler.FolderExists(CraftLibrarySystem.CraftPath))
                FileHandler.FolderCreate(CraftLibrarySystem.CraftPath);
            if (!FileHandler.FolderExists(FlagSystem.FlagPath))
                FileHandler.FolderCreate(FlagSystem.FlagPath);
            if (!FileHandler.FolderExists(GroupSystem.GroupsPath))
                FileHandler.FolderCreate(GroupSystem.GroupsPath);
            if (!FileHandler.FolderExists(ScreenshotSystem.ScreenshotPath))
                FileHandler.FolderCreate(ScreenshotSystem.ScreenshotPath);
            if (!FileHandler.FolderExists(KerbalSystem.KerbalsPath))
            {
                FileHandler.FolderCreate(KerbalSystem.KerbalsPath);
                KerbalSystem.GenerateDefaultKerbals();
            }
            if (!FileHandler.FolderExists(ScenarioSystem.ScenariosPath))
                FileHandler.FolderCreate(ScenarioSystem.ScenariosPath);
            if (!FileHandler.FolderExists(VesselStoreSystem.VesselsPath))
                FileHandler.FolderCreate(VesselStoreSystem.VesselsPath);
            if (!FileHandler.FolderExists(WaypointSystem.WaypointsPath))
                FileHandler.FolderCreate(WaypointSystem.WaypointsPath);
        }
    }
}
