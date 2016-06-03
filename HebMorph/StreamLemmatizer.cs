/***************************************************************************
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HebMorph.HSpell;

namespace HebMorph
{
    public class StreamLemmatizer : Lemmatizer
    {
        private Tokenizer _tokenizer;

        public StreamLemmatizer()
            : base()
        {
        }

        public StreamLemmatizer(System.IO.TextReader input)
            : base()
        {
            _tokenizer = new Tokenizer(input);
        }

        public StreamLemmatizer(string hspellPath, bool loadMorphologicalData, bool allowHeHasheela)
            : base(hspellPath, loadMorphologicalData, allowHeHasheela)
        {
        }

        public StreamLemmatizer(System.IO.TextReader input, string hspellPath, bool loadMorphologicalData, bool allowHeHasheela)
            : base(hspellPath, loadMorphologicalData, allowHeHasheela)
        {
            _tokenizer = new Tokenizer(input);
        }

        public void SetStream(System.IO.TextReader input)
        {
            if (_tokenizer == null)
                _tokenizer = new Tokenizer(input);
            else
                _tokenizer.Reset(input);
        }

        private int _startOffset, _endOffset;
        public int StartOffset
        {
            get { return _startOffset; }
        }
        public int EndOffset
        {
            get { return _endOffset; }
        }

        public bool TolerateWhenLemmatizingStream = true;

        public int LemmatizeNextToken(out string nextToken, IList<Token> retTokens)
        {
            retTokens.Clear();

            int currentPos = 0;

        	// Used to loop over certain noise cases
            while (true)
            {
                Tokenizer.TokenType tokenType = _tokenizer.NextToken(out nextToken);
                if (tokenType == 0)
                    return 0; // EOS

                _startOffset = _tokenizer.Offset;
                _endOffset = _startOffset + _tokenizer.LengthInSource;

                ++currentPos;

                if ((tokenType & Tokenizer.TokenType.Hebrew) > 0)
                {
                    // Right now we are blindly removing all Niqqud characters. Later we will try and make some
                    // use of Niqqud for some cases. We do this before everything else to allow for a correct
                    // identification of prefixes.
                    nextToken = RemoveNiqqud(nextToken);

                    // Ignore "words" which are actually only prefixes in a single word.
                    // This first case is easy to spot, since the prefix and the following word will be
                    // separated by a dash, and marked as a construct (������) by the Tokenizer
                    if ((tokenType & Tokenizer.TokenType.Construct) > 0
                        || (tokenType & Tokenizer.TokenType.Acronym) > 0)
                    {
                        if (IsLegalPrefix(nextToken))
                        {
                            --currentPos; // this should be treated as a word prefix
                            continue;
                        }
                    }

                    // This second case is a bit more complex. We take a risk of splitting a valid acronym or
                    // abbrevated word into two, so we send it to an external function to analyze the word, and
                    // get a possibly corrected word. Examples for words we expect to simplify by this operation
                    // are �"����", �"�����.
                    if ((tokenType & Tokenizer.TokenType.Acronym) > 0)
                    {
                        nextToken = TryStrippingPrefix(nextToken);

                        // Re-detect acronym, in case it was a false positive
                        if (nextToken.IndexOf('"') == -1)
                            tokenType &= ~Tokenizer.TokenType.Acronym;
                    }

                    // TODO: Perhaps by easily identifying the prefixes above we can also rule out some of the
                    // stem ambiguities retreived later...

					// Support for external dictionaries, for preventing OOV words or providing synonyms
                	string correctedWord = LookupWordCorrection(nextToken);
					if (!string.IsNullOrEmpty(correctedWord))
					{
						retTokens.Add(new HebrewToken(correctedWord, 0, DMask.D_CUSTOM, correctedWord, 1.0f));
						nextToken = correctedWord;
						break;
					}

                	IList<HebrewToken> lemmas = Lemmatize(nextToken);
                    if (lemmas.Count > 0)
                    {
                        // TODO: Filter Stop Words based on morphological data (hspell 'x' identification)
                        // TODO: Check for worthy lemmas, if there are none then perform tolerant lookup and check again...
                        if ((tokenType & Tokenizer.TokenType.Construct) > 0)
                        {
                            // TODO: Test for (lemma.Mask & DMask.D_OSMICHUT) > 0
                        }

                        foreach (Token t in lemmas) // temp catch-all
                            retTokens.Add(t);
                    }

                    if (retTokens.Count == 0 && (tokenType & Tokenizer.TokenType.Acronym) > 0)
                    {
                        // TODO: Perform Gimatria test
                        // TODO: Treat an acronym as a noun and strip affixes accordingly?
                        retTokens.Add(new HebrewToken(nextToken, 0, DMask.D_ACRONYM, nextToken, 1.0f));
                    }
                    else if (TolerateWhenLemmatizingStream && retTokens.Count == 0)
                    {
                        lemmas = LemmatizeTolerant(nextToken);
                        if (lemmas.Count > 0)
                        {
                            // TODO: Keep only worthy lemmas, based on characteristics and score / confidence

                            if ((tokenType & Tokenizer.TokenType.Construct) > 0)
                            {
                                // TODO: Test for (lemma.Mask & DMask.D_OSMICHUT) > 0
                            }

                            foreach (Token t in lemmas) // temp catch-all
                                retTokens.Add(t);
                        }
                        else // Word unknown to hspell - OOV case
                        {
                            // TODO: Right now we store the word as-is. Perhaps we can assume this is a Noun or a name,
                            // and try removing prefixes and suffixes based on that?
                            //retTokens.Add(new HebrewToken(nextToken, 0, 0, null, 1.0f));
                        }
                    }
                }
                else if ((tokenType & Tokenizer.TokenType.Numeric) > 0)
                    retTokens.Add(new Token(nextToken, true));
                else
                    retTokens.Add(new Token(nextToken));

                break;
            }

            return currentPos;
        }

		protected virtual string LookupWordCorrection(string word)
		{
			return null;
		}
    }
}
