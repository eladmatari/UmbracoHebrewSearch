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

namespace HebMorph.DataStructures
{
    using System;
    using System.Collections.Generic;
    using ToleranceFuncDelegate = HebMorph.LookupTolerators.ToleranceFuncDelegate;

    public class DictRadix<T> : IEnumerable<T>
    {
        public class DictNode
        {
            public char[] Key;
            public T Value;
            public DictNode[] Children;
        }

        protected class TolerantLookupCrawler
        {
            protected class MatchCandidate
            {
                public MatchCandidate(byte _keyPos, string _word, float _score)
                {
                    this.keyPos = _keyPos;
                    this.Word = _word;
                    this.Score = _score;
                }

                public byte keyPos;
                public string Word;
                public float Score = 1.0f;
            }

            public TolerantLookupCrawler(DictRadix<T> _enclosingInstance, ToleranceFuncDelegate[] _tolFuncs)
            {
                this.enclosingInstance = _enclosingInstance;
                this.toleranceFunctions = _tolFuncs;
            }

            ToleranceFuncDelegate[] toleranceFunctions;
            DictRadix<T> enclosingInstance;

            char[] key;
            List<LookupResult> resultSet = new List<LookupResult>();

            public List<LookupResult> LookupTolerant(string strKey)
            {
                lock (resultSet)
                {
                    key = strKey.ToCharArray();
                    resultSet.Clear();
                    LookupTolerantImpl(enclosingInstance.RootNode, new MatchCandidate(0, string.Empty, 1.0f));
                }
                if (resultSet.Count > 0)
                    return resultSet;
                return null;
            }

            private void LookupTolerantImpl(DictNode cur, MatchCandidate mc)
            {
                if (cur.Children == null)
                    return;

                //System.Diagnostics.Trace.WriteLine("--------------------------");
                //System.Diagnostics.Trace.WriteLine(string.Format("Processing children for word {0}", mc.Word));
                for (byte childPos = 0; childPos < cur.Children.Length; childPos++)
                {
                    DictNode child = cur.Children[childPos];
                    DoKeyMatching(child, 0, mc);
                }
                //System.Diagnostics.Trace.WriteLine(string.Format("Completed processing node children for word {0}", mc.Word));
                //System.Diagnostics.Trace.WriteLine("--------------------------");
            }

            private void DoKeyMatching(DictNode node, byte nodeKeyPos, MatchCandidate mc)
            {
                byte currentKeyPos = mc.keyPos, startingNodeKeyPos = nodeKeyPos;
                while (nodeKeyPos < node.Key.Length && currentKeyPos < key.Length)
                {
                    // toleration
                    foreach (ToleranceFuncDelegate tf in toleranceFunctions)
                    {
                        byte tmpKeyPos = mc.keyPos;
                        float tmpScore = mc.Score;
                        byte? tret = tf(key, ref tmpKeyPos, mc.Word, ref tmpScore, node.Key[nodeKeyPos]);
                        if (tret != null)
                        {
                            //System.Diagnostics.Trace.WriteLine(string.Format("{0} tolerated a char, attempting word {1}", tf.Method.Name, mc.Word + node._Key[nodeKeyPos]));

                            string consumedLetters = string.Empty;
                            if (((byte)tret > 0) && ((byte)tret <= node.Key.Length))
                                consumedLetters = new string(node.Key, nodeKeyPos, (byte)tret);
                            MatchCandidate nmc = new MatchCandidate(tmpKeyPos, mc.Word + consumedLetters, tmpScore);
                            if ((nodeKeyPos + (byte)tret) == node.Key.Length)
                                LookupTolerantImpl(node, nmc);
                            else
                                DoKeyMatching(node, (byte)(nodeKeyPos + (byte)tret), nmc);
                        }
                    }

                    // standard key matching
                    if (node.Key[nodeKeyPos] != key[currentKeyPos])
                        break;

                    //System.Diagnostics.Trace.WriteLine(string.Format("Matched char: {0}", key[currentKeyPos]));
                    currentKeyPos++;
                    nodeKeyPos++;
                }

                if (nodeKeyPos == node.Key.Length)
                {
                    if (currentKeyPos == key.Length)
                    {
                        //System.Diagnostics.Trace.WriteLine(string.Format("Consumed the whole key"));
                        if (node.Value != null)
                            resultSet.Add(
                                new LookupResult(mc.Word + new string(node.Key, startingNodeKeyPos, nodeKeyPos - startingNodeKeyPos),
                                node.Value, mc.Score)
                                );
                    }
                    else
                    {
                        MatchCandidate nmc = new MatchCandidate(currentKeyPos,
                            mc.Word + new string(node.Key, startingNodeKeyPos, nodeKeyPos - startingNodeKeyPos),
                            mc.Score);
                        LookupTolerantImpl(node, nmc);
                    }
                }
            }
        }

        protected DictNode m_root;
        public DictNode RootNode { get { return m_root; } }

        protected int m_nCount = 0;
        public int Count { get { return m_nCount; } }

        private bool m_bAllowValueOverride = false;
        public bool AllowValueOverride { get { return m_bAllowValueOverride; } set { m_bAllowValueOverride = value; } }

        public DictRadix()
        {
            m_root = new DictNode();
        }

        public T Lookup(string key)
        {
            return Lookup(key.ToCharArray());
        }
        
        public T Lookup(char[] key)
        {
            DictNode dn = LookupImpl(key);
            if (dn == null) return default(T);
            return dn.Value;
        }
        
        /// <summary>
        /// Simple, efficient method for exact lookup in the radix
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private DictNode LookupImpl(char[] key)
        {
            byte keyPos = 0, n;
            int keyLength = GetCharArrayLength(key);

            DictNode cur = m_root;
            while (cur != null && cur.Children != null)
            {
                for (byte childPos = 0; ; childPos++)
                {
                    DictNode child = cur.Children[childPos];
                    
                    // Do key matching
                    n = 0;
                    while (n < child.Key.Length && keyPos < keyLength && child.Key[n] == key[keyPos])
                    {
                        keyPos++;
                        n++;
                    }

                    if (n == child.Key.Length) // We consumed the child's key, and so far it matches our key
                    {
                        // We consumed both the child's key and the requested key, meaning we found the requested node
                        if (keyLength == keyPos)
                        {
                            return child;
                        }
                        // We consumed this child's key, but the key we are looking for isn't over yet
                        else if (keyLength > keyPos)
                        {
                            cur = child;
                            break;
                        }
                    }
                    else if (n > 0 // Incomplete match to child's key (worths nothing)
                        || childPos + 1 == cur.Children.Length // We looked at all the node's children
                        )
                    {
                        return null;
                    }
                }
            }
            
            return null;
        }

        public class LookupResult
        {
            public LookupResult(string _word, T _data, float _score)
            {
                this.Word = _word;
                this.Data = _data;
                this.Score = _score;
            }

            public string Word;
            public T Data;
            public float Score;
        }

        public List<LookupResult> LookupTolerant(string strKey, ToleranceFuncDelegate[] tolFuncs)
        {
            TolerantLookupCrawler tlc = new TolerantLookupCrawler(this, tolFuncs);
            return tlc.LookupTolerant(strKey);
        }

        private static int GetCharArrayLength(char[] ar)
        {
            int i = 0;
            while (ar.Length > i && ar[i] != '\0') i++;
            return i;
        }

        public void AddNode(string key, T data)
        {
            AddNode(key.ToCharArray(), data);
        }

        public void AddNode(char[] key, T data)
        {
            // Since key might be a buffer array which is longer than the actual word in it, we can't
            // just use key.Length
            int keyLength = GetCharArrayLength(key);

            int keyPos = 0;
            DictNode cur = m_root;
            while (cur != null)
            {
                // No children, but key is definitely a descendant
                if (cur.Children == null)
                {
                    // TODO: This assumes cur has a value and therefore is a leaf, hence we branch
                    // instead of merging keys. Is this always the case?

                    DictNode newChild = new DictNode();
                    newChild.Key = new char[keyLength - keyPos];
                    Array.Copy(key, keyPos, newChild.Key, 0, newChild.Key.Length);
                    newChild.Value = data;

                    cur.Children = new DictNode[1];
                    cur.Children[0] = newChild;
                    m_nCount++;
                    return;
                }

                // Iterate through all children of the current node, and either switch node based on the
                // key, find a node to split into 2, or add a new child with the remaining path
                int childPos = 0;
                bool bFoundChild = false;
                for (; childPos < cur.Children.Length; childPos++)
                {
                    DictNode child = cur.Children[childPos];

                    int n = 0;

                    // By definition, there is no such thing as a null _Key
                    while (n < child.Key.Length && keyPos < keyLength && child.Key[n] == key[keyPos] && key[keyPos] != '\0')
                    {
                        keyPos++;
                        n++;
                    }

                    // If it was a match, even partial
                    if (n > 0)
                    {
                        bFoundChild = true;

                        // We consumed this child's key, but the key we are looking for isn't over yet
                        if (n == child.Key.Length && keyLength > keyPos)
                        {
                            cur = child;
                            break;
                        }
                        // We consumed none of the keys
                        else if (child.Key.Length > n && keyLength > keyPos)
                        {
                            // split
                            DictNode bridgeChild = new DictNode();
                            bridgeChild.Key = new char[n];
                            Array.Copy(child.Key, 0, bridgeChild.Key, 0, n);

                            int childNewKeyLen = child.Key.Length - n;
                            char[] childNewKey = new char[childNewKeyLen];
                            Array.Copy(child.Key, n, childNewKey, 0, childNewKeyLen);
                            child.Key = childNewKey;

                            bridgeChild.Children = new DictNode[2];

                            DictNode newNode = new DictNode();
                            newNode.Key = new char[keyLength - keyPos];
                            Array.Copy(key, keyPos, newNode.Key, 0, newNode.Key.Length);
                            newNode.Value = data;

                            if (child.Key[0].CompareTo(newNode.Key[0]) < 0)
                            {
                                bridgeChild.Children[0] = child;
                                bridgeChild.Children[1] = newNode;
                            }
                            else
                            {
                                bridgeChild.Children[0] = newNode;
                                bridgeChild.Children[1] = child;
                            }

                            cur.Children[childPos] = bridgeChild;

                            m_nCount++;

                            return;
                        }
                        // We consumed the requested key, but the there's still more chars in the child's key
                        else if (child.Key.Length > n && keyLength == keyPos)
                        {
                            // split
                            DictNode newChild = new DictNode();
                            newChild.Key = new char[n];
                            Array.Copy(child.Key, 0, newChild.Key, 0, n);

                            int childNewKeyLen = child.Key.Length - n;
                            char[] childNewKey = new char[childNewKeyLen];
                            Array.Copy(child.Key, n, childNewKey, 0, childNewKeyLen);
                            child.Key = childNewKey;

                            newChild.Children = new DictNode[1];
                            newChild.Children[0] = child;
                            newChild.Value = data;

                            cur.Children[childPos] = newChild;
                            
                            m_nCount++;

                            return;
                        }
                        // We consumed both the child's key and the requested key
                        else if (n == child.Key.Length && keyLength == keyPos)
                        {
                            if (object.Equals(child.Value, default(T)))
                            {
                                child.Value = data;
                                m_nCount++;
                            }
                            else if (m_bAllowValueOverride)
                            {
                                // Only override data if this radix object is configured to do this
                                child.Value = data;
                            }
                            return;
                        }
                    }
                }

                if (!bFoundChild)
                {
                    // Dead end - add a new child and return
                    DictNode newChild = new DictNode();
                    newChild.Key = new char[keyLength - keyPos];
                    Array.Copy(key, keyPos, newChild.Key, 0, newChild.Key.Length);
                    newChild.Value = data;

                    DictNode[] newArray = new DictNode[cur.Children.Length + 1];
                    int curPos = 0;
                    for (; curPos < cur.Children.Length; ++curPos)
                    {
                        if (newChild.Key[0].CompareTo(cur.Children[curPos].Key[0]) < 0)
                            break;
                        newArray[curPos] = cur.Children[curPos];
                    }
                    newArray[curPos] = newChild;
                    for (; curPos < cur.Children.Length; ++curPos)
                    {
                        newArray[curPos + 1] = cur.Children[curPos];
                    }
                    cur.Children = newArray;

                    m_nCount++;

                    return;
                }
            }
        }

        public void Clear()
        {
            m_root = new DictNode();
            m_nCount = 0;
        }

        #region Enumeration support
        public class RadixEnumerator : IEnumerator<T>
        {
            private DictRadix<T> radix;
            private LinkedList<DictRadix<T>.DictNode> nodesPath;

            public RadixEnumerator(DictRadix<T> r)
            {
                this.radix = r;
                nodesPath = new LinkedList<DictRadix<T>.DictNode>();
                nodesPath.AddLast(radix.m_root);
            }

            #region IEnumerator Members

            object System.Collections.IEnumerator.Current
            {
                get { return nodesPath.Last.Value.Value; }
            }

            public string CurrentKey
            {
                get
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach (DictRadix<T>.DictNode dn in nodesPath)
                    {
                        sb.Append(dn.Key);
                    }
                    return sb.ToString();
                }
            }

            public bool MoveNext()
            {
                bool goUp = false;

                while (nodesPath.Count > 0)
                {
                    DictRadix<T>.DictNode n = nodesPath.Last.Value;
                    if (goUp || n.Children == null || n.Children.Length == 0)
                    {
                        nodesPath.RemoveLast();
                        if (nodesPath.Count == 0) break;
                        goUp = true;
                        for (int i = 0; i < nodesPath.Last.Value.Children.Length; i++)
                        {
                            // Move to the next child
                            if (nodesPath.Last.Value.Children[i] == n
                                && i + 1 < nodesPath.Last.Value.Children.Length)
                            {
                                nodesPath.AddLast(nodesPath.Last.Value.Children[i + 1]);
                                if (!object.Equals(nodesPath.Last.Value.Value, default(T)))
                                    return true;
                                goUp = false;
                                break;
                            }
                        }
                    }
                    else
                    {
                        nodesPath.AddLast(n.Children[0]);
                        goUp = false;
                        if (!object.Equals(n.Children[0].Value, default(T)))
                            return true;
                    }
                }
                return false;
            }

            public void Reset()
            {
                nodesPath.Clear();
                nodesPath.AddLast(radix.m_root);
            }

            #endregion

            #region IEnumerator<T> Members

            public T Current
            {
                get { return nodesPath.Last.Value.Value; }
            }

            #endregion

            #region IDisposable Members

            public void Dispose()
            {
                // Nothing to dispose of
            }

            #endregion
        }

        #region IEnumerable<T> Members

        public IEnumerator<T> GetEnumerator()
        {
            return (IEnumerator<T>)new RadixEnumerator(this);
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return (System.Collections.IEnumerator)new RadixEnumerator(this);
        }

        #endregion

        #endregion
    }
}
