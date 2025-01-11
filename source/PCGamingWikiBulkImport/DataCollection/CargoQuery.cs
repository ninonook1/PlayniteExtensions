﻿using RestSharp;
using System.Collections.Generic;
using System.Linq;

namespace PCGamingWikiBulkImport.DataCollection
{
    public interface ICargoQuery
    {
        CargoResultRoot<CargoResultGame> GetGamesByExactValues(string table, string field, IEnumerable<string> values, int offset);
        CargoResultRoot<CargoResultGame> GetGamesByHolds(string table, string field, string holds, int offset);
        CargoResultRoot<CargoResultGame> GetGamesByHoldsLike(string table, string field, string holds, int offset);
        IEnumerable<ItemCount> GetValueCounts(string table, string field, string filter = null);
    }

    internal class CargoQuery : ICargoQuery
    {
        private RestClient restClient = new RestClient("https://www.pcgamingwiki.com/w/api.php")
            .AddDefaultQueryParameter("action", "cargoquery")
            .AddDefaultQueryParameter("limit", "max")
            .AddDefaultQueryParameter("format", "json");

        public IEnumerable<ItemCount> GetValueCounts(string table, string field, string filter = null)
        {
            string having = "Value IS NOT NULL";

            if (!string.IsNullOrWhiteSpace(filter))
            {
                var escapedFilter = filter.Replace(@"\", @"\\").Replace("'", @"\'");
                having = $"Value LIKE '%{escapedFilter}%'";
            }

            var request = new RestRequest()
                    .AddQueryParameter("tables", table)
                    .AddQueryParameter("fields", $"{table}.{field}=Value,COUNT(*)=Count")
                    .AddQueryParameter("group_by", $"{table}.{field}")
                    .AddQueryParameter("having", having);

            var result = restClient.Execute<CargoResultRoot<ItemCount>>(request);
            return result.Data?.CargoQuery.Select(t => t.Title) ?? Enumerable.Empty<ItemCount>();
        }

        public CargoResultRoot<CargoResultGame> GetGamesByHolds(string table, string field, string holds, int offset)
        {
            var request = GetBaseGameRequest(table, field)
                .AddQueryParameter("where", $"{table}.{field} HOLDS '{holds}'")
                .AddQueryParameter("offset", $"{offset:0}");

            var response = restClient.Execute<CargoResultRoot<CargoResultGame>>(request);
            return response.Data;
        }

        public CargoResultRoot<CargoResultGame> GetGamesByHoldsLike(string table, string field, string holds, int offset)
        {
            var request = GetBaseGameRequest(table, field)
                .AddQueryParameter("where", $"{table}.{field} HOLDS LIKE '{holds}'")
                .AddQueryParameter("offset", $"{offset:0}");

            var response = restClient.Execute<CargoResultRoot<CargoResultGame>>(request);
            return response.Data;
        }

        public CargoResultRoot<CargoResultGame> GetGamesByExactValues(string table, string field, IEnumerable<string> values, int offset)
        {
            var valuesList = string.Join(", ", values.Select(v => $"'{v}'"));

            var request = GetBaseGameRequest(table, field)
                .AddQueryParameter("where", $"{table}.{field} IN ({valuesList})")
                .AddQueryParameter("offset", $"{offset:0}");

            var response = restClient.Execute<CargoResultRoot<CargoResultGame>>(request);
            return response.Data;
        }

        private RestRequest GetBaseGameRequest(string table, string field)
        {
            var baseTable = CargoTables.GameInfoBoxTableName;

            RestRequest request = new RestRequest()
                    .AddQueryParameter("fields", $"{baseTable}._pageName=Name,{baseTable}.Released,{baseTable}.Available_on=OS,{baseTable}.Steam_AppID=SteamID,{baseTable}.GOGcom_ID=GOGID,{table}.{field}=Value");

            if (table == baseTable)
                request.AddQueryParameter("tables", baseTable);
            else
                request.AddQueryParameter("tables", $"{baseTable},{table}")
                       .AddQueryParameter("join_on", $"{baseTable}._pageID={table}._pageID");

            return request;
        }
    }
}
