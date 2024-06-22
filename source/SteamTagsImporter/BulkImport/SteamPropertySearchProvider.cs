﻿using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteExtensions.Metadata.Common;
using PlayniteExtensions.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SteamTagsImporter.BulkImport
{
    public class SteamPropertySearchProvider : ISearchableDataSourceWithDetails<SteamProperty, IEnumerable<GameDetails>>
    {
        private readonly SteamSearch steamSearch;
        private SteamProperty[] steamProperties;
        private SteamProperty[] SteamProperties => steamProperties ?? (steamProperties = steamSearch.GetProperties().ToArray());

        public SteamPropertySearchProvider(SteamSearch steamSearch)
        {
            this.steamSearch = steamSearch;
        }

        public IEnumerable<GameDetails> GetDetails(SteamProperty prop, GlobalProgressActionArgs progressArgs = null, Game searchGame = null)
        {
            int start = 0, total = 0;
            var games = new List<GameDetails>();
            if (progressArgs != null)
                progressArgs.IsIndeterminate = false;

            do
            {
                var searchResult = steamSearch.SearchGames(prop.Param, prop.Value, start);
                total = searchResult.TotalCount;

                games.AddRange(steamSearch.ParseSearchResultHtml(searchResult.ResultsHtml));

                start += 50;

                if (progressArgs != null)
                {
                    progressArgs.ProgressMaxValue = searchResult.TotalCount;
                    progressArgs.CurrentProgressValue = games.Count;
                    progressArgs.Text = $"Downloading {prop.Name}… {games.Count}/{total}";
                }
            } while (start < total && progressArgs?.CancelToken.IsCancellationRequested != true);
            return games;
        }

        public IEnumerable<SteamProperty> Search(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return SteamProperties;

            return SteamProperties.Where(sp => sp.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase) || sp.Category.Contains(query, StringComparison.InvariantCultureIgnoreCase));
        }

        public GenericItemOption<SteamProperty> ToGenericItemOption(SteamProperty item)
        {
            return new GenericItemOption<SteamProperty>(item)
            {
                Name = item.Name,
                Description = item.Category
            };
        }
    }
}
