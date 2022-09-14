﻿using GamesSizeCalculator.SteamSizeCalculation;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteUtilitiesCommon;
using PluginsCommon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;

namespace GamesSizeCalculator
{
    public class GamesSizeCalculator : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private GamesSizeCalculatorSettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("97cc59db-3f80-4852-8bfc-a80304f9efe9");

        public GamesSizeCalculator(IPlayniteAPI api) : base(api)
        {
            settings = new GamesSizeCalculatorSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new GamesSizeCalculatorSettingsView();
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            return new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    Description = ResourceProvider.GetString("LOCGame_Sizes_Calculator_MenuItemDescriptionCalculateSizesSelGames"),
                    MenuSection = "Games Size Calculator",
                    Action = a =>
                    {
                        UpdateGamesListSizes(args.Games, false);
                    }
                },
                new GameMenuItem
                {
                    Description = ResourceProvider.GetString("LOCGame_Sizes_Calculator_MenuItemDescriptinoCalculateSizesSelGamesForce"),
                    MenuSection = "Games Size Calculator",
                    Action = a =>
                    {
                        UpdateGamesListSizes(args.Games, true);
                    }
                }
            };
        }

        private void UpdateGamesListSizes(IEnumerable<Game> games, bool overwrite)
        {
            UpdateGameSizes(games.Distinct().ToList(), overwrite, false);

            PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCGame_Sizes_Calculator_DialogMessageDone"));
        }

        private ISteamAppIdUtility GetDefaultSteamAppUtility()
        {
            var appListCache = new CachedFileDownloader("https://api.steampowered.com/ISteamApps/GetAppList/v2/",
                    Path.Combine(GetPluginUserDataPath(), "SteamAppList.json"),
                    TimeSpan.FromDays(3),
                    Encoding.UTF8);

            return new SteamAppIdUtility(appListCache);
        }

        private bool ShouldCalculateGameDirectorySize(Game game, DateTime? onlyIfNewerThan = null)
        {
            string installDirectory = FileSystem.FixPathLength(game?.InstallDirectory);

            if (installDirectory.IsNullOrEmpty() || !Directory.Exists(installDirectory))
            {
                return false;
            }

            if (onlyIfNewerThan.HasValue &&
                (Directory.GetLastWriteTime(installDirectory) < onlyIfNewerThan.Value))
            {
                return false;
            }

            return true;
        }

        private ulong GetGameDirectorySize(Game game)
        {
            try
            {
                return FileSystem.GetDirectorySizeOnDisk(game.InstallDirectory);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Error while getting directory size in {game.InstallDirectory} of game {game.Name}");
                PlayniteApi.Notifications.Messages.Add(
                    new NotificationMessage("GetInstalledGameSize" + game.Id.ToString(),
                        string.Format(ResourceProvider.GetString("LOCGame_Sizes_Calculator_NotificationMessageErrorGetDirSize"), game.InstallDirectory, game.Name, e.Message),
                        NotificationType.Error)
                    );

                return 0;
            }
        }

        private string GetRomPath(Game game)
        {
            var romPath = game.Roms.First().Path;
            if (romPath.IsNullOrEmpty())
            {
                return null;
            }

            if (!game.InstallDirectory.IsNullOrEmpty())
            {
                romPath = romPath.Replace("{InstallDir}", game.InstallDirectory).Replace(@"\\", @"\");
            }

            romPath = FileSystem.FixPathLength(romPath);

            return romPath;
        }

        private bool ShouldCalculateRomSize(Game game, DateTime? onlyIfNewerThan = null)
        {
            var romPath = GetRomPath(game);
            if (romPath.IsNullOrEmpty())
            {
                return false;
            }

            if (!FileSystem.FileExists(romPath))
            {
                return false;
            }

            if (onlyIfNewerThan.HasValue &&
                (File.GetLastWriteTime(romPath) < onlyIfNewerThan))
            {
                return false;
            }

            return true;
        }

        private ulong GetGameRomSize(Game game)
        {
            var romPath = GetRomPath(game);
            if (romPath.IsNullOrEmpty())
                return 0;

            try
            {
                return FileSystem.GetFileSizeOnDisk(romPath);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Error while getting rom file size in {romPath}");
                PlayniteApi.Notifications.Messages.Add(
                    new NotificationMessage("GetRomSizeError" + game.Id.ToString(),
                        string.Format(ResourceProvider.GetString("LOCGame_Sizes_Calculator_NotificationMessageErrorGetRomFileSize"), game.InstallDirectory, game.Name, e.Message),
                        NotificationType.Error)
                );

                return 0;
            }
        }

        private ulong GetInstallSizeOnline(Game game, IOnlineSizeCalculator sizeCalculator)
        {
            try
            {
                var sizeTask = sizeCalculator.GetInstallSizeAsync(game);
                if (sizeTask.Wait(7000))
                {
                    return sizeTask.Result ?? 0L;
                }
                else
                {
                    logger.Warn($"Timed out while getting online {sizeCalculator.ServiceName} install size for {game.Name}");
                    return 0L;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, $"Error while getting file size online from {sizeCalculator?.ServiceName} for {game?.Name}");
                PlayniteApi.Notifications.Messages.Add(
                    new NotificationMessage("GetOnlineSizeError" + game.Id.ToString(),
                        string.Format(ResourceProvider.GetString("LOCGame_Sizes_Calculator_NotificationMessageErrorGetOnlineSize"), sizeCalculator.ServiceName, game.Name, e.Message),
                        NotificationType.Error));
                return 0;
            }
        }

        private ulong GetInstallSizeOnline(Game game, ICollection<IOnlineSizeCalculator> onlineSizeCalculators)
        {
            ulong size = 0;

            var alreadyRan = new List<IOnlineSizeCalculator>();
            //check the preferred online size calculators first (Steam for Steam games, GOG for GOG games, etc)
            foreach (var sizeCalculator in onlineSizeCalculators)
            {
                if (!sizeCalculator.IsPreferredInstallSizeCalculator(game))
                {
                    continue;
                }

                size = GetInstallSizeOnline(game, sizeCalculator);
                alreadyRan.Add(sizeCalculator);
                if (size != 0)
                {
                    break;
                }
            }

            //go through every online size calculator as a fallback
            if (size == 0)
            {
                foreach (var sizeCalculator in onlineSizeCalculators)
                {
                    if (alreadyRan.Contains(sizeCalculator))
                    {
                        continue;
                    }

                    size = GetInstallSizeOnline(game, sizeCalculator);
                    if (size != 0)
                    {
                        break;
                    }
                }
            }

            return size;
        }

        private enum GameSizeCalculationMethod
        {
            None,
            Directory,
            ROM,
            Online,
        }

        private GameSizeCalculationMethod GetGameSizeCalculationMethod(Game game, ICollection<IOnlineSizeCalculator> onlineSizeCalculators, bool overwrite, bool onlyIfNewerThanSetting = false)
        {
            if (!overwrite && !game.Version.IsNullOrEmpty())
            {
                return GameSizeCalculationMethod.None;
            }

            var onlyIfNewerThan = onlyIfNewerThanSetting ? settings.Settings.LastRefreshOnLibUpdate : (DateTime?)null;

            if (game.IsInstalled)
            {
                if (game.Roms.HasItems())
                {
                    return ShouldCalculateRomSize(game, onlyIfNewerThan) ? GameSizeCalculationMethod.ROM : GameSizeCalculationMethod.None;
                }
                else
                {
                    return ShouldCalculateGameDirectorySize(game, onlyIfNewerThan) ? GameSizeCalculationMethod.Directory : GameSizeCalculationMethod.None;
                }
            }
            else if (onlineSizeCalculators?.Count > 0 && PlayniteUtilities.IsGamePcGame(game))
            {
                return GameSizeCalculationMethod.Online;
            }

            return GameSizeCalculationMethod.None;
        }

        private void CalculateGameSize(Game game, ICollection<IOnlineSizeCalculator> onlineSizeCalculators, GameSizeCalculationMethod sizeCalculationMethod)
        {
            ulong size = 0;
            switch (sizeCalculationMethod)
            {
                case GameSizeCalculationMethod.Directory:
                    size = GetGameDirectorySize(game);
                    break;
                case GameSizeCalculationMethod.ROM:
                    size = GetGameRomSize(game);
                    break;
                case GameSizeCalculationMethod.Online:
                    size = GetInstallSizeOnline(game, onlineSizeCalculators);
                    break;
            }

            if (size == 0)
            {
                return;
            }

            var fSize = GetBytesReadable(size);
            if (game.Version.IsNullOrEmpty() || (!game.Version.IsNullOrEmpty() && game.Version != fSize))
            {
                logger.Info($"Updated {game.Name} version field from {game.Version} to {fSize}");
                game.Version = fSize;
                PlayniteApi.Database.Games.Update(game);
            }
        }

        // Returns the human-readable file size for an arbitrary, 64-bit file size 
        // Returns in format "111.111 GB"
        // From https://stackoverflow.com/a/11124118
        private static string GetBytesReadable(ulong i)
        {
            // Only use GB so values can be sorted on Playnite
            double readable = i >> 20;

            // Divide by 1024 to get fractional value
            readable /= 1024;

            // Return formatted number with suffix
            return readable.ToString("000.000 GB");
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            if (!settings.Settings.CalculateNewGamesOnLibraryUpdate && !settings.Settings.CalculateModifiedGamesOnLibraryUpdate)
            {
                settings.Settings.LastRefreshOnLibUpdate = DateTime.Now;
                SavePluginSettings(settings.Settings);
                return;
            }

            var progressTitle = ResourceProvider.GetString("LOCGame_Sizes_Calculator_DialogMessageCalculatingSizes");
            var progressOptions = new GlobalProgressOptions(progressTitle, true);
            progressOptions.IsIndeterminate = false;
            PlayniteApi.Dialogs.ActivateGlobalProgress((a) =>
            {
                ProcessGamesOnLibUpdate(a);
            }, progressOptions);

            settings.Settings.LastRefreshOnLibUpdate = DateTime.Now;
            SavePluginSettings(settings.Settings);
        }

        private ICollection<IOnlineSizeCalculator> GetOnlineSizeCalculators(SteamApiClient steamClient)
        {
            var onlineSizeCalculators = new List<IOnlineSizeCalculator>();
            if (settings.Settings.GetUninstalledGameSizeFromSteam)
            {
                onlineSizeCalculators.Add(new SteamSizeCalculator(steamClient, GetDefaultSteamAppUtility(), settings.Settings));
            }
            if (settings.Settings.GetUninstalledGameSizeFromGog)
            {
                onlineSizeCalculators.Add(new GOG.GogSizeCalculator(new GOG.HttpDownloaderWrapper(), settings.Settings.GetSizeFromGogNonGogGames));
            }
            return onlineSizeCalculators;
        }

        private void ProcessGamesOnLibUpdate(GlobalProgressActionArgs a)
        {
            ICollection<Game> games = PlayniteApi.Database.Games;
            if (!settings.Settings.GetUninstalledGameSizeFromSteam && !settings.Settings.GetUninstalledGameSizeFromGog)
            {
                games = PlayniteApi.Database.Games.Where(x => x.IsInstalled).ToList();
            }

            UpdateGameSizes(games, overwrite: false, onlyNewOrModified: true);
        }

        private void UpdateGameSizes(ICollection<Game> games, bool overwrite, bool onlyNewOrModified)
        {
            string progressBase = ResourceProvider.GetString("LOCGame_Sizes_Calculator_DialogMessageCalculatingSizes");

            var progressOptions = new GlobalProgressOptions(progressBase, true);
            progressOptions.IsIndeterminate = false;
            PlayniteApi.Dialogs.ActivateGlobalProgress((a) =>
            {
                a.ProgressMaxValue = games.Count;

                using (PlayniteApi.Database.BufferedUpdate())
                using (var steamClient = new SteamApiClient())
                {
                    var onlineSizeCalculators = GetOnlineSizeCalculators(steamClient);
                    var workload = new Dictionary<Game, GameSizeCalculationMethod>();
                    //loop 1: check which games need to be calculated
                    foreach (var game in games)
                    {
                        if (a.CancelToken.IsCancellationRequested)
                            break;

                        a.CurrentProgressValue++;

                        GameSizeCalculationMethod calcMethod = GameSizeCalculationMethod.None;

                        if (onlyNewOrModified)
                        {
                            if (game.Added != null && game.Added > settings.Settings.LastRefreshOnLibUpdate)
                            {
                                if (!settings.Settings.CalculateNewGamesOnLibraryUpdate)
                                    continue;

                                calcMethod = GetGameSizeCalculationMethod(game, onlineSizeCalculators, false);
                            }
                            else if (settings.Settings.CalculateModifiedGamesOnLibraryUpdate)
                            {
                                // To make sure only Version fields filled by the extension are
                                // replaced
                                if (!game.Version.IsNullOrEmpty() && !game.Version.EndsWith(" GB"))
                                    continue;

                                //don't get install size online for locally installed games: online size calculators = null
                                calcMethod = GetGameSizeCalculationMethod(game, null, true, true);
                            }
                        }
                        else
                        {
                            calcMethod = GetGameSizeCalculationMethod(game, onlineSizeCalculators, overwrite);
                        }

                        if (calcMethod != GameSizeCalculationMethod.None && !workload.ContainsKey(game))
                        {
                            workload.Add(game, calcMethod);
                        }
                    }

                    if (workload.Count == 0)
                        return;

                    a.CurrentProgressValue = 0;
                    a.ProgressMaxValue = workload.Count;

                    //loop 2: calculate (splitting these results in a more accurate progress bar)
                    foreach (var item in workload)
                    {
                        var game = item.Key;
                        var calcMethod = item.Value;

                        a.CurrentProgressValue++;
                        a.Text = $"{progressBase}\n{a.CurrentProgressValue}/{a.ProgressMaxValue}\n{game.Name}";

                        CalculateGameSize(game, onlineSizeCalculators, calcMethod);
                    }
                }
            }, new GlobalProgressOptions(progressBase, true) { IsIndeterminate = false });
        }
    }
}