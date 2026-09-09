using LmpClient.Systems.Chat;
using LmpClient.Systems.CraftLibrary;
using LmpClient.Systems.PlayerColorSys;
using LmpClient.Systems.Screenshot;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.Status;
using LmpClient.Systems.Warp;
using LmpClient.Windows.Admin;
using LmpClient.Windows.Chat;
using LmpClient.Windows.CraftLibrary;
using LmpClient.Windows.Debug;
using LmpClient.Windows.Options;
using LmpClient.Windows.Screenshots;
using LmpClient.Windows.Systems;
using LmpClient.Windows.Vessels;
using LmpCommon;
using UnityEngine;

namespace LmpClient.Windows.Status
{
    public partial class StatusWindow
    {
        private static readonly WarpSystem WarpSystem = WarpSystem.Singleton;

        protected override void DrawWindowContent(int windowId)
        {
            GUILayout.BeginVertical();
            GUI.DragWindow(MoveRect);

            DrawTopButtons();
            DrawSubspaces();
#if DEBUG
            DrawDebugSection();
#endif
            DrawBottomButtons();
            GUILayout.EndVertical();
        }

        private static void DrawBottomButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(DisconnectIcon))
                MainSystem.Singleton.DisconnectFromGame();
            OptionsWindow.Singleton.Display = GUILayout.Toggle(OptionsWindow.Singleton.Display, SettingsIcon, ToggleButtonStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawTopButtons()
        {
            GUILayout.BeginHorizontal();

            ChatWindow.Singleton.Display = GUILayout.Toggle(ChatWindow.Singleton.Display,
                ChatSystem.Singleton.NewMessageReceived && Flash ? ChatRedIcon : ChatIcon, ToggleButtonStyle);
            CraftLibraryWindow.Singleton.Display = GUILayout.Toggle(CraftLibraryWindow.Singleton.Display,
                CraftLibrarySystem.Singleton.NewContent && Flash ? RocketRedIcon : RocketIcon, ToggleButtonStyle);
            ScreenshotsWindow.Singleton.Display = GUILayout.Toggle(ScreenshotsWindow.Singleton.Display,
                ScreenshotSystem.Singleton.NewContent && Flash ? CameraRedIcon : CameraIcon, ToggleButtonStyle);

            if (SettingsSystem.ServerSettings.AllowAdmin)
            {
                AdminWindow.Singleton.Display = GUILayout.Toggle(AdminWindow.Singleton.Display, AdminIcon, ToggleButtonStyle);
            }

            GUILayout.EndHorizontal();
        }

        #region Subspace drawing 

        private static void DrawSubspaces()
        {
            _scrollPosition =
                GUILayout.BeginScrollView(_scrollPosition, _subspaceListStyle, GUILayout.ExpandHeight(true));

            for (var i = 0; i < SubspaceDisplay.Count; i++)
            {
                GUILayout.BeginVertical(_subspaceStyle, GUILayout.ExpandWidth(true));
                GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                if (SubspaceDisplay[i].SubspaceId == -1)
                {
                    GUILayout.Label(StatusTexts.GetTimeLabel(SubspaceDisplay[i]));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(StatusTexts.WarpingLabelTxt, BoldRedLabelStyle);
                }
                else
                {
                    GUILayout.Label(StatusTexts.GetTimeLabel(SubspaceDisplay[i]));
                    GUILayout.FlexibleSpace();
                    if (NotWarpingAndIsFutureSubspace(SubspaceDisplay[i].SubspaceId) && GUILayout.Button(SyncIcon))
                        WarpSystem.Singleton.SyncToSubspace(SubspaceDisplay[i].SubspaceId);
                }

                GUILayout.EndHorizontal();

                for (var j = 0; j < SubspaceDisplay[i].Players.Count; j++)
                {
                    DrawPlayerEntry(StatusSystem.Singleton.GetPlayerStatus(SubspaceDisplay[i].Players[j]));
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();

            //Our scroll list will capture the scrollwheel.
            if (Event.current.type == EventType.ScrollWheel) Event.current.Use();
        }

        private static bool NotWarpingAndIsFutureSubspace(int subspaceId)
        {
            return !WarpSystem.CurrentlyWarping && WarpSystem.CurrentSubspace != subspaceId &&
                   WarpSystem.Subspaces.ContainsKey(WarpSystem.CurrentSubspace) && WarpSystem.Subspaces.ContainsKey(subspaceId) &&
                   WarpSystem.Subspaces[WarpSystem.CurrentSubspace] < WarpSystem.Subspaces[subspaceId];
        }

        private static void DrawPlayerEntry(PlayerStatus playerStatus)
        {
            if (playerStatus == null)
                return;
            GUILayout.BeginHorizontal();
            if (!_playerNameStyle.ContainsKey(playerStatus.PlayerName))
            {
                _playerNameStyle[playerStatus.PlayerName] = new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = PlayerColorSystem.Singleton.GetPlayerColor(playerStatus.PlayerName) },
                    hover = { textColor = PlayerColorSystem.Singleton.GetPlayerColor(playerStatus.PlayerName) },
                    active = { textColor = PlayerColorSystem.Singleton.GetPlayerColor(playerStatus.PlayerName) },
                    fontStyle = FontStyle.Bold,
                    stretchWidth = true,
                    wordWrap = false
                };
            }
            GUILayout.Label(playerStatus.PlayerName, _playerNameStyle[playerStatus.PlayerName]);
            GUILayout.FlexibleSpace();
            GUILayout.Label(playerStatus.DisplayText, _stateTextStyle);
            GUILayout.EndHorizontal();
        }

        #endregion

        #region Debug Section

        private static void DrawDebugSection()
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            DebugWindow.Singleton.Display = GUILayout.Toggle(DebugWindow.Singleton.Display, StatusTexts.DebugBtnTxt, ToggleButtonStyle);
            SystemsWindow.Singleton.Display = GUILayout.Toggle(SystemsWindow.Singleton.Display, StatusTexts.SystemsBtnTxt, ToggleButtonStyle);
            VesselsWindow.Singleton.Display = GUILayout.Toggle(VesselsWindow.Singleton.Display, StatusTexts.VesselsBtnTxt, ToggleButtonStyle);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawDebugSwitches();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private static void DrawDebugSwitches()
        {
            var d1 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug1, StatusTexts.Debug1BtnTxt, ToggleButtonStyle);
            if (d1 != SettingsSystem.CurrentSettings.Debug1)
            {
                SettingsSystem.CurrentSettings.Debug1 = d1;
                SettingsSystem.SaveSettings();
            }
            var d2 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug2, StatusTexts.Debug2BtnTxt, ToggleButtonStyle);
            if (d2 != SettingsSystem.CurrentSettings.Debug2)
            {
                SettingsSystem.CurrentSettings.Debug2 = d2;
                SettingsSystem.SaveSettings();
            }
            var d3 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug3, StatusTexts.Debug3BtnTxt, ToggleButtonStyle);
            if (d3 != SettingsSystem.CurrentSettings.Debug3)
            {
                SettingsSystem.CurrentSettings.Debug3 = d3;
                SettingsSystem.SaveSettings();
            }
            var d4 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug4, StatusTexts.Debug4BtnTxt, ToggleButtonStyle);
            if (d4 != SettingsSystem.CurrentSettings.Debug4)
            {
                SettingsSystem.CurrentSettings.Debug4 = d4;
                SettingsSystem.SaveSettings();
            }
            var d5 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug5, StatusTexts.Debug5BtnTxt, ToggleButtonStyle);
            if (d5 != SettingsSystem.CurrentSettings.Debug5)
            {
                SettingsSystem.CurrentSettings.Debug5 = d5;
                SettingsSystem.SaveSettings();
            }
            var d6 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug6, StatusTexts.Debug6BtnTxt, ToggleButtonStyle);
            if (d6 != SettingsSystem.CurrentSettings.Debug6)
            {
                SettingsSystem.CurrentSettings.Debug6 = d6;
                SettingsSystem.SaveSettings();
            }
            var d7 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug7, StatusTexts.Debug7BtnTxt, ToggleButtonStyle);
            if (d7 != SettingsSystem.CurrentSettings.Debug7)
            {
                SettingsSystem.CurrentSettings.Debug7 = d7;
                SettingsSystem.SaveSettings();
            }
            var d8 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug8, StatusTexts.Debug8BtnTxt, ToggleButtonStyle);
            if (d8 != SettingsSystem.CurrentSettings.Debug8)
            {
                SettingsSystem.CurrentSettings.Debug8 = d8;
                SettingsSystem.SaveSettings();
            }
            var d9 = GUILayout.Toggle(SettingsSystem.CurrentSettings.Debug9, StatusTexts.Debug9BtnTxt, ToggleButtonStyle);
            if (d9 != SettingsSystem.CurrentSettings.Debug9)
            {
                SettingsSystem.CurrentSettings.Debug9 = d9;
                SettingsSystem.SaveSettings();
            }

        }

        #endregion
    }
}
