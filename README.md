# Umbraco Hebrew Search

This plugin contains hebrew search for lucene.net using HebMorph

It is using an HebMorph version from 2012, which it's source code can be found here:

https://github.com/synhershko/HebMorph/tree/5bdd9245c2d5a1413c782ae4cc97240e543d6fb2

this is the last version of HebMorph that uses Lucene.Net 2.9.4.1 (as far as I know)

which is the version that is being used by Umbraco.

Instructions:

1) Install the plugin using the packages in your Umbraco backoffice.

2) Configure the search analyzer in ExamineSettings.config, for example change ExternalSearcher to this:

<add name="ExternalSearcher" type="UmbracoExamine.UmbracoExamineSearcher, UmbracoExamine" analyzer="Lucene.Net.Analysis.Hebrew.MorphAnalyzer, Lucene.Net.Hebrew" />
