﻿/***************************************************************************
 *   Copyright (C) 2010 by                                                 *
 *      Itamar Syn-Hershko <itamar at code972 dot com>                     *
 *                                                                         *
 *   Distributed under the GNU General Public License, Version 2.0.        *
 *                                                                         *
 *   This program is free software; you can redistribute it and/or modify  *
 *   it under the terms of the GNU General Public License as published by  *
 *   the Free Software Foundation (v2).                                    *
 *                                                                         *
 *   This program is distributed in the hope that it will be useful,       *
 *   but WITHOUT ANY WARRANTY; without even the implied warranty of        *
 *   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the         *
 *   GNU General Public License for more details.                          *
 *                                                                         *
 *   You should have received a copy of the GNU General Public License     *
 *   along with this program; if not, write to the                         *
 *   Free Software Foundation, Inc.,                                       *
 *   51 Franklin Steet, Fifth Floor, Boston, MA  02111-1307, USA.          *
 ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Text;
using Lucene.Net.Analysis.Tokenattributes;

namespace Lucene.Net.Analysis.Hebrew
{
    public class StreamLemmasFilter : Tokenizer
    {
        private HebMorph.StreamLemmatizer _streamLemmatizer;

        private TermAttribute termAtt;
        private OffsetAttribute offsetAtt;
        private PositionIncrementAttribute posIncrAtt;
        private TypeAttribute typeAtt;
        //protected PayloadAttribute payAtt;

        public bool alwaysSaveMarkedOriginal;
        public HebMorph.LemmaFilters.LemmaFilterBase lemmaFilter = null;

        private List<HebMorph.Token> stack = new List<HebMorph.Token>();
        private IList<HebMorph.Token> filterCache = new List<HebMorph.Token>();
        private int index = 0;
        private Dictionary<string, bool> previousLemmas = new Dictionary<string,bool>();

        #region Constructors
        public StreamLemmasFilter(System.IO.TextReader input, HebMorph.StreamLemmatizer _lemmatizer)
            //: base(input) <- converts to CharStream, and causes issues due to a call to ReadToEnd in ctor
        {
            Init(input, _lemmatizer, null, false);
        }

        public StreamLemmasFilter(System.IO.TextReader input, HebMorph.StreamLemmatizer _lemmatizer, bool AlwaysSaveMarkedOriginal)
            //: base(input) <- converts to CharStream, and causes issues due to a call to ReadToEnd in ctor
        {
            Init(input, _lemmatizer, null, AlwaysSaveMarkedOriginal);
        }

        public StreamLemmasFilter(System.IO.TextReader input, HebMorph.StreamLemmatizer _lemmatizer,
            HebMorph.LemmaFilters.LemmaFilterBase _lemmaFilter, bool AlwaysSaveMarkedOriginal)
        //: base(input) <- converts to CharStream, and causes issues due to a call to ReadToEnd in ctor
        {
            Init(input, _lemmatizer, _lemmaFilter, AlwaysSaveMarkedOriginal);
        }

        public StreamLemmasFilter(System.IO.TextReader input, HebMorph.StreamLemmatizer _lemmatizer,
            HebMorph.LemmaFilters.LemmaFilterBase _lemmaFilter)
        //: base(input) <- converts to CharStream, and causes issues due to a call to ReadToEnd in ctor
        {
            Init(input, _lemmatizer, _lemmaFilter, false);
        }

        private void Init(System.IO.TextReader input, HebMorph.StreamLemmatizer _lemmatizer,
            HebMorph.LemmaFilters.LemmaFilterBase _lemmaFilter, bool AlwaysSaveMarkedOriginal)
        {
            termAtt = (TermAttribute)AddAttribute(typeof(TermAttribute));
            offsetAtt = (OffsetAttribute)AddAttribute(typeof(OffsetAttribute));
            posIncrAtt = (PositionIncrementAttribute)AddAttribute(typeof(PositionIncrementAttribute));
            typeAtt = (TypeAttribute)AddAttribute(typeof(TypeAttribute));
            //payAtt = (PayloadAttribute)AddAttribute(typeof(PayloadAttribute));

        	this.input = input;
            this._streamLemmatizer = _lemmatizer;
            this._streamLemmatizer.SetStream(input);
            this.alwaysSaveMarkedOriginal = AlwaysSaveMarkedOriginal;
            this.lemmaFilter = _lemmaFilter;
        }
        #endregion

        public override bool IncrementToken()
        {
            // Index all unique lemmas at the same position
            while (index < stack.Count)
            {
                HebMorph.HebrewToken res = stack[index++] as HebMorph.HebrewToken;

                if (res == null || previousLemmas.ContainsKey(res.Lemma)) // Skip multiple lemmas (we will merge morph properties later)
                    continue;

                previousLemmas.Add(res.Lemma, true);

                if (CreateHebrewToken(res))
                    return true;
            }

            // Reset state
            ClearAttributes();
            index = 0;
            stack.Clear();
            previousLemmas.Clear();

            // Lemmatize next word in stream. The HebMorph lemmatizer will always return a token, unless
            // an unrecognized Hebrew word is hit, then an empty tokens array will be returned.
            string word = string.Empty; // to hold the original word from the stream
            if (_streamLemmatizer.LemmatizeNextToken(out word, stack) == 0)
                return false; // EOS

            // Store the location of the word in the original stream
            offsetAtt.SetOffset(CorrectOffset(_streamLemmatizer.StartOffset), CorrectOffset(_streamLemmatizer.EndOffset));

            // A non-Hebrew word
            if (stack.Count == 1 && !(stack[0] is HebMorph.HebrewToken))
            {
                SetTermText(word);

                HebMorph.Token tkn = stack[0];
                if (tkn.IsNumeric)
                    typeAtt.SetType(HebrewTokenizer.TokenTypeSignature(HebrewTokenizer.TOKEN_TYPES.Numeric));
                else
                {
                    typeAtt.SetType(HebrewTokenizer.TokenTypeSignature(HebrewTokenizer.TOKEN_TYPES.NonHebrew));

                    // Applying LowerCaseFilter for Non-Hebrew terms
                    char[] buffer = termAtt.TermBuffer();
                    int length = termAtt.TermLength();
                    for (int i = 0; i < length; i++)
                        buffer[i] = System.Char.ToLower(buffer[i]);
                }

                stack.Clear();
                return true;
            }

            // If we arrived here, we hit a Hebrew word
            // Do some filtering if requested...
            if (lemmaFilter != null && lemmaFilter.FilterCollection(stack, filterCache) != null)
            {
                stack.Clear();
                stack.AddRange(filterCache);
            }

            // OOV case -- for now store word as-is and return true
            if (stack.Count == 0)
            {
                // TODO: To allow for more advanced uses, fill stack with processed tokens and
                // SetPositionIncrement(0)

                SetTermText(word + "$");
                typeAtt.SetType(HebrewTokenizer.TokenTypeSignature(HebrewTokenizer.TOKEN_TYPES.Hebrew));
                return true;
            }

            // If only one lemma was returned for this word
            if (stack.Count == 1)
            {
                HebMorph.HebrewToken hebToken = stack[0] as HebMorph.HebrewToken;

                // Index the lemma alone if it exactly matches the word minus prefixes
                if (!alwaysSaveMarkedOriginal && hebToken.Lemma.Equals(word.Substring(hebToken.PrefixLength)))
                {
                    CreateHebrewToken(hebToken);
                    posIncrAtt.SetPositionIncrement(1);
                    stack.Clear();
                    return true;
                }
                // Otherwise, index the lemma plus the original word marked with a unique flag to increase precision
                else
                {
                    // DILEMMA: Does indexing word.Substring(hebToken.PrefixLength) + "$" make more or less sense?
                    // For now this is kept the way it is below to support duality of SimpleAnalyzer and MorphAnalyzer
                    SetTermText(word + "$");
                }
            }

            // More than one lemma exist. Mark and store the original term to increase precision, while all
            // lemmas will be popped out of the stack and get stored at the next call to IncrementToken.
            else
            {
                SetTermText(word + "$");
            }

            typeAtt.SetType(HebrewTokenizer.TokenTypeSignature(HebrewTokenizer.TOKEN_TYPES.Hebrew));

            return true;
        }

        protected virtual bool CreateHebrewToken(HebMorph.HebrewToken hebToken)
        {
            SetTermText(hebToken.Lemma == null ? hebToken.Text.Substring(hebToken.PrefixLength) : hebToken.Lemma);
            posIncrAtt.SetPositionIncrement(0);

            // TODO: typeAtt.SetType(TokenTypeSignature(TOKEN_TYPES.Acronym));
            typeAtt.SetType(HebrewTokenizer.TokenTypeSignature(HebrewTokenizer.TOKEN_TYPES.Hebrew));

            /*
             * Morph payload
             * 
            byte[] data = new byte[1];
            data[0] = (byte)morphResult.Mask; // TODO: Set bits selectively
            Payload payload = new Payload(data);
            payAtt.SetPayload(payload);
            */

            return true;
        }

        private void SetTermText(string token)
        {
            // Record the term string
            if (termAtt.TermLength() < token.Length)
                termAtt.SetTermBuffer(token);
            else // Perform a copy to save on memory operations
            {
                char[] buf = termAtt.TermBuffer();
                token.CopyTo(0, buf, 0, token.Length);
            }
            termAtt.SetTermLength(token.Length);
        }

        public override void Reset(System.IO.TextReader input)
        {
            base.Reset(input);
            stack.Clear();
            index = 0;
            _streamLemmatizer.SetStream(input);
        }
    }
}
