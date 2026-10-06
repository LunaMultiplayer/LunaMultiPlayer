using LmpClient.Localization.Structures;
using LmpClient.Utilities;
using LmpCommon.Xml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LmpClient.Localization
{
    public static class LocalizationContainer
    {
        public static readonly List<string> Languages = new List<string>();
        public static string CurrentLanguage { get; set; } = "English";

        public static AdminWindowText AdminWindowText = new AdminWindowText();
        public static BannedPartsResourcesWindowText BannedPartsResourcesWindowText = new BannedPartsResourcesWindowText();
        public static ChatWindowText ChatWindowText = new ChatWindowText();
        public static ConnectionWindowText ConnectionWindowText = new ConnectionWindowText();
        public static ModWindowText ModWindowText = new ModWindowText();
        public static OptionsWindowText OptionsWindowText = new OptionsWindowText();
        public static ServerListWindowText ServerListWindowText = new ServerListWindowText();
        public static StatusWindowText StatusWindowText = new StatusWindowText();
        public static DisclaimerDialogText DisclaimerDialogText = new DisclaimerDialogText();
        public static InstallDialogText InstallDialogText = new InstallDialogText();
        public static ScreenshotWindowText ScreenshotWindowText = new ScreenshotWindowText();
        public static ScreenText ScreenText = new ScreenText();
        public static ButtonTooltips ButtonTooltips = new ButtonTooltips();
        public static UpdateWindowText UpdateWindowText = new UpdateWindowText();
        public static CompatibleDialogText CompatibleDialogText = new CompatibleDialogText();
        public static RevertDialogText RevertDialogText = new RevertDialogText();
        public static ServerListFiltersText ServerListFiltersText = new ServerListFiltersText();

        private static readonly string LocalizationFolder = CommonUtil.CombinePaths(MainSystem.KspPath, "GameData", "LunaMultiplayer", "Localization");

        public static string GetCurrentLanguageAsText()
        {
            return CurrentLanguage.Replace("_", " ");
        }

        #region Loading

        public static void LoadLanguages()
        {
            Languages.Clear();
            Languages.AddRange(Directory.GetDirectories(LocalizationFolder).Select(d => new DirectoryInfo(d).Name));
        }

        public static string GetNextLanguage()
        {
            for (var i = 0; i < Languages.Count; i++)
            {
                if (CurrentLanguage == Languages[i])
                {
                    return i + 1 == Languages.Count ? Languages[0] : Languages[i + 1];
                }
            }

            return Languages[0];
        }

        public static void LoadLanguage(string language)
        {
            CurrentLanguage = language;

            if (!Directory.Exists(LocalizationFolder))
            {
                Directory.CreateDirectory(LocalizationFolder);
            }

            if (!Directory.Exists(CommonUtil.CombinePaths(LocalizationFolder, language)))
            {
                Directory.CreateDirectory(CommonUtil.CombinePaths(LocalizationFolder, language));
            }

            LoadWindowTexts(language, ref AdminWindowText);
            LoadWindowTexts(language, ref BannedPartsResourcesWindowText);
            LoadWindowTexts(language, ref ChatWindowText);
            LoadWindowTexts(language, ref ConnectionWindowText);
            LoadWindowTexts(language, ref ModWindowText);
            LoadWindowTexts(language, ref OptionsWindowText);
            LoadWindowTexts(language, ref ServerListWindowText);
            LoadWindowTexts(language, ref StatusWindowText);
            LoadWindowTexts(language, ref DisclaimerDialogText);
            LoadWindowTexts(language, ref InstallDialogText);
            LoadWindowTexts(language, ref ScreenshotWindowText);
            LoadWindowTexts(language, ref ScreenText);
            LoadWindowTexts(language, ref ButtonTooltips);
            LoadWindowTexts(language, ref UpdateWindowText);
            LoadWindowTexts(language, ref CompatibleDialogText);
            LoadWindowTexts(language, ref RevertDialogText);
            LoadWindowTexts(language, ref ServerListFiltersText);
        }

        /// <summary>
        /// Regenerates all the translation xml files. 
        /// Usefull when you add a new string to a new text class as you don't need to edit all the xml files in every language
        /// </summary>
        public static void RegenerateTranslations()
        {
            var currentLanguage = CurrentLanguage.Clone() as string;

            foreach (var language in Languages)
            {
                LoadLanguage(language);

                LunaXmlSerializer.WriteToXmlFile(AdminWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(AdminWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(BannedPartsResourcesWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(BannedPartsResourcesWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ChatWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ChatWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ConnectionWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ConnectionWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ModWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ModWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(OptionsWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(OptionsWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ServerListWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ServerListWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(StatusWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(StatusWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(DisclaimerDialogText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(DisclaimerDialogText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(InstallDialogText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(InstallDialogText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ScreenshotWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ScreenshotWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ScreenText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ScreenText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ButtonTooltips, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ButtonTooltips)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(UpdateWindowText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(UpdateWindowText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(CompatibleDialogText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(CompatibleDialogText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(RevertDialogText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(RevertDialogText)}.xml"));
                LunaXmlSerializer.WriteToXmlFile(ServerListFiltersText, CommonUtil.CombinePaths(LocalizationFolder, language, $"{nameof(ServerListFiltersText)}.xml"));
            }

            LoadLanguage(currentLanguage);
        }

        #endregion

        #region Private

        private static void LoadWindowTexts<T>(string language, ref T classToReplace) where T : class, new()
        {
            try
            {
                var filePath = CommonUtil.CombinePaths(LocalizationFolder, language, $"{classToReplace.GetType().Name}.xml");
                if (!File.Exists(filePath))
                    LunaXmlSerializer.WriteToXmlFile(new T(), filePath);

                classToReplace = LunaXmlSerializer.ReadXmlFromPath<T>(filePath);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"Error reading '{classToReplace.GetType().Name}.xml' for language '{language}' Details: {e}");
            }
        }

        #endregion
    }
}
