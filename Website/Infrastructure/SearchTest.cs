using Examine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Umbraco.Core.Models;
using Umbraco.Web;

namespace Website.Infrastructure
{
    public static class SearchTest
    {
        public static IEnumerable<IPublishedContent> Search(string term)
        {
            var searcher = ExamineManager.Instance.SearchProviderCollection["ExternalSearcher"];
            var searchCriteria = searcher.CreateSearchCriteria();
            var query = searchCriteria.GroupedOr(new string[] { "title", "description" }, term).Compile();

            var examineResults = searcher.Search(query, 100);

            var contentResults = new UmbracoHelper(UmbracoContext.Current).TypedContent(examineResults.Select(i => i.Id));

            return contentResults;
        }
    }
}