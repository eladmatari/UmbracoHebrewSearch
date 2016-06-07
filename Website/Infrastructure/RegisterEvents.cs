using HebMorph;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Umbraco.Core;

namespace Website.Infrastructure
{
    public class RegisterEvents : ApplicationEventHandler
    {
        protected override void ApplicationStarted(UmbracoApplicationBase umbracoApplication, ApplicationContext applicationContext)
        {
            SetExamineSearch();
        }

        private void SetExamineSearch()
        {
            var prefixes = new string[] { "ב", "מה", "שה", "ל", "מ", "ו", "וה", "ושה", "כשה", "וב", "ומה", "ול", "ומ", "כש", "כשב", "ש" };

            StreamLemmatizer.LemmatizeUnknownWord = (word) =>
            {
                return (string[])ApplicationContext.Current.ApplicationCache.RequestCache.GetCacheItem("LemmatizeUnknownWord:" + word, () =>
                {
                    var words = prefixes.Where(i => word.StartsWith(i)).Select(i => word.Remove(0, i.Length)).ToList();
                    words.Add(word);

                    foreach (var currWord in new List<string>(words))
                    {
                        words.AddRange(prefixes.Select(i => i + currWord));
                    }

                    return words.Where(i => i.Length > 1).ToArray();
                });
            };
        }
    }
}