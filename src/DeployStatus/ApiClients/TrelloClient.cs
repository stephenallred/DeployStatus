﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DeployStatus.Configuration;
using log4net;
using RestSharp;
using RestSharp.Authenticators;

namespace DeployStatus.ApiClients
{    
    internal class TrelloClient
    {
        private readonly RestClient restClient;
        private TrelloEmailResolver emailResolver;
        private readonly string deploymentLinkingSearchTemplate;

        public TrelloClient(TrelloApiConfiguration configuration)
        {
            restClient = new RestClient("https://api.trello.com/1/");
            restClient.Authenticator = GetAuthenticator(configuration.Authentication);

            emailResolver = configuration.EmailResolver;
            deploymentLinkingSearchTemplate = GetDeploymentLinkingSearchTemplate(configuration.DeploymentLinkingConfiguration);
        }

        private static SimpleAuthenticator GetAuthenticator(TrelloAuthentication trelloAuthentication)
        {
            return new SimpleAuthenticator("key", trelloAuthentication.Key, "token", trelloAuthentication.Token);
        }

        private static string GetDeploymentLinkingSearchTemplate(DeploymentLinkingConfiguration deploymentLinkingConfiguration)
        {
            return $"board:\"{deploymentLinkingConfiguration.BoardName}\" is:open {string.Join(" ", deploymentLinkingConfiguration.FilterCardsFromColumns.Select(x => $"-list:{x}"))}" + " {0}";
        }

        public async Task<IEnumerable<TrelloCardInfo>> GetCardsContaining(string searchString)
        {
            var searchResult = await ExecuteSearchCards(searchString);
            var membersFromServer = await Task.WhenAll(searchResult.Cards
                .SelectMany(x => x.IdMembers)
                .Distinct()
                .Select(async x => await ExecuteGetMember(x)));

            var membersById = membersFromServer.ToDictionary(x => x.Id, x => x);
            var trelloCardInfos = searchResult.Cards.Select(x =>
            {
                var members = x.IdMembers.Select(y => GetTrelloMemberInfo(membersById[y].FullName)).ToList();
                return new TrelloCardInfo(x.Id, x.Name, x.Url, members);
            });

            return trelloCardInfos.ToList();
        }

        private TrelloMemberInfo GetTrelloMemberInfo(string fullName)
        {
            return new TrelloMemberInfo(fullName, emailResolver.GetEmail(fullName));
        }

        private async Task<SearchResult> ExecuteSearchCards(string searchString)
        {
            var restRequest = new RestRequest("search/");
            restRequest.AddParameter("modelTypes", "cards,members");
            restRequest.AddParameter("query", string.Format(deploymentLinkingSearchTemplate, searchString));
            restRequest.AddParameter("cards_limit", 10); // cap at 10 cards for now
            restRequest.AddParameter("card_fields", "idMembers,url,name");

            var result = await restClient.ExecuteGetTaskAsync<SearchResult>(restRequest);
            return result.Data;
        }

        private async Task<Member> ExecuteGetMember(string memberId)
        {
            var restRequest = new RestRequest("members/{id}");
            restRequest.AddUrlSegment("id", memberId);
            restRequest.AddParameter("fields", "fullName");
            
            var result = await restClient.ExecuteGetTaskAsync<Member>(restRequest);            
            return result.Data;
        }

        private class SearchResult
        {
            public List<Card> Cards { get; private set; }
        }

        private class Card
        {
            public string Id { get; private set; }
            public List<string> IdMembers { get; private set; }
            public string Name { get; private set; }
            public string Url { get; private set; }
        }

        private class Member
        {
            public string Id { get; private set; }
            public string FullName { get; private set; }
        }
    }
}