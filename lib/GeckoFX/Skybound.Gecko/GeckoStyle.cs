﻿#region ***** BEGIN LICENSE BLOCK *****
/* Version: MPL 1.1/GPL 2.0/LGPL 2.1
 *
 * The contents of this file are subject to the Mozilla Public License Version
 * 1.1 (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 * http://www.mozilla.org/MPL/
 *
 * Software distributed under the License is distributed on an "AS IS" basis,
 * WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License
 * for the specific language governing rights and limitations under the
 * License.
 *
 * The Original Code is Skybound Software code.
 *
 * The Initial Developer of the Original Code is Skybound Software.
 * Portions created by the Initial Developer are Copyright (C) 2008-2009
 * the Initial Developer. All Rights Reserved.
 *
 * Contributor(s):
 *
 * Alternatively, the contents of this file may be used under the terms of
 * either the GNU General Public License Version 2 or later (the "GPL"), or
 * the GNU Lesser General Public License Version 2.1 or later (the "LGPL"),
 * in which case the provisions of the GPL or the LGPL are applicable instead
 * of those above. If you wish to allow use of your version of this file only
 * under the terms of either the GPL or the LGPL, and not to allow others to
 * use your version of this file under the terms of the MPL, indicate your
 * decision by deleting the provisions above and replace them with the notice
 * and other provisions required by the GPL or the LGPL. If you do not delete
 * the provisions above, a recipient may use your version of this file under
 * the terms of any one of the MPL, the GPL or the LGPL.
 */
#endregion END LICENSE BLOCK

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Skybound.Gecko
{
	/// <summary>
	/// Represents a DOM style sheet.
	/// </summary>
	public class GeckoStyleSheet
	{
		GeckoStyleSheet(nsIDOMCSSStyleSheet styleSheet)
		{
			_DomStyleSheet = styleSheet;
		}
		
		internal static GeckoStyleSheet Create(nsIDOMCSSStyleSheet styleSheet)
		{
			return (styleSheet == null) ? null : new GeckoStyleSheet(styleSheet);
		}
		
		/// <summary>
		/// Gets the underlying XPCOM object.
		/// </summary>
		public object DomStyleSheet
		{
			get { return _DomStyleSheet; }
		}
		nsIDOMCSSStyleSheet _DomStyleSheet;
		
		/// <summary>
		/// Gets or sets whether the style sheet is disabled.
		/// </summary>
		public bool Disabled
		{
			get { return _DomStyleSheet.GetDisabled(); }
			set { _DomStyleSheet.SetDisabled(value); }
		}
		
		/// <summary>
		/// Gets the HREF of the style sheet.
		/// </summary>
		public string Href
		{
			get { return nsString.Get(_DomStyleSheet.GetHref); }
		}
		
		/// <summary>
		/// Gets the parent of this style sheet, if it was imported using an @import rule.
		/// </summary>
		public GeckoStyleSheet ParentStyleSheet
		{
			get { return Create((nsIDOMCSSStyleSheet)_DomStyleSheet.GetParentStyleSheet()); }
		}
		
		/// <summary>
		/// Gets the <see cref="GeckoStyleRule"/> which imported this style sheet.
		/// </summary>
		public GeckoStyleRule OwnerRule
		{
			get { return GeckoStyleRule.Create((nsIDOMCSSRule)_DomStyleSheet.GetOwnerRule()); }
		}
		
		/// <summary>
		/// Gets the <see cref="GeckoNode"/> of the DOM element which imported this style
		/// sheet.  Typically, this is a LINK tag.
		/// </summary>
		public GeckoNode OwnerNode
		{
			get { return GeckoNode.Create(_DomStyleSheet.GetOwnerNode()); }
		}
		
		public override string ToString()
		{
			return "Href=" + this.Href;
		}
		
		/// <summary>
		/// Gets the collection of rules in the style sheet.
		/// </summary>
		public StyleRuleCollection CssRules
		{
			get { return (_CssRules == null) ? (_CssRules = new StyleRuleCollection(this)) : _CssRules; }
		}
		StyleRuleCollection _CssRules;
		
		/// <summary>
		/// Represents a collection of rules in a style sheet.
		/// </summary>
		#region public class StyleRuleCollection : IEnumerable<GeckoStyleRule>
		public class StyleRuleCollection : IEnumerable<GeckoStyleRule>
		{
			internal StyleRuleCollection(GeckoStyleSheet styleSheet)
			{
				StyleSheet = styleSheet;
				this.List = GetRuleList();
			}
			
			GeckoStyleSheet StyleSheet;
			nsIDOMCSSRuleList List;
			
			nsIDOMCSSRuleList GetRuleList()
			{
				using (AutoJSContext context = new AutoJSContext())
				{
					nsIDOMCSSRuleList ret;
					int hresult = StyleSheet._DomStyleSheet.GetCssRules(out ret);
					//return (StyleSheet._DomStyleSheet.GetCssRules(out ret) != 0) ? null : ret;
					return ret;
				}
			}
			
			/// <summary>
			/// Attempts to reload the rule list.
			/// </summary>
			public void Reload()
			{
				this.List = GetRuleList();
			}
			
			/// <summary>
			/// Gets whether the collection is read-only.
			/// </summary>
			public bool IsReadOnly
			{
				get { return List == null; }
			}
			
			/// <summary>
			/// Gets the number of items in the collection.
			/// </summary>
			public int Count
			{
				get { return (List == null) ? 0 : List.GetLength(); }
			}
			
			/// <summary>
			/// Returns the <see cref="GeckoStyleRule"/> at a given index in the collection.
			/// </summary>
			/// <param name="index"></param>
			/// <returns></returns>
			public GeckoStyleRule this[int index]
			{
				get
				{
					if (index < 0 || index >= Count)
						throw new ArgumentOutOfRangeException("index");
					
					return GeckoStyleRule.Create(List.Item(index));
				}
			}
			
			/// <summary>
			/// Adds a new rule to the end of the collection.
			/// </summary>
			/// <param name="rule"></param>
			public void Add(string rule)
			{
				Insert(Count, rule);
			}
			
			/// <summary>
			/// Inserts a rule at the specified position in the collection.  The return value is the index in the list where the item was actually inserted,
			/// or -1 if the rule contains syntax errors and could not be added to the collection.
			/// </summary>
			/// <param name="index"></param>
			/// <param name="rule"></param>
			public int Insert(int index, string rule)
			{
				if (IsReadOnly)
					throw new InvalidOperationException("This collection is read-only.");
				else if (index < 0 || index > Count)
					throw new ArgumentOutOfRangeException("index");
				else if (string.IsNullOrEmpty(rule))
					return -1;
				
				const int NS_ERROR_DOM_SYNTAX_ERR = unchecked((int)0x8053000c);
				
				using (AutoJSContext context = new AutoJSContext())
				{
					int hresult = StyleSheet._DomStyleSheet.InsertRule(new nsAString(rule), index, out index);
					
					if (hresult == NS_ERROR_DOM_SYNTAX_ERR)
					{
						return -1;
					}
					else if (hresult != 0)
					{
						throw new COMException("", hresult);
					}
				}
				
				return index;
			}
			
			/// <summary>
			/// Removes a specific rule from the collection.
			/// </summary>
			/// <param name="index"></param>
			public void RemoveAt(int index)
			{
				if (IsReadOnly)
					throw new InvalidOperationException("This collection is read-only.");
				else if (index < 0 || index >= Count)
					throw new ArgumentOutOfRangeException("index");
				
				using (AutoJSContext context = new AutoJSContext())
				{
					StyleSheet._DomStyleSheet.DeleteRule(index);
				}
			}
			
			/// <summary>
			/// Removes all rules from the collection.
			/// </summary>
			public void Clear()
			{
				if (IsReadOnly && Count > 0)
					throw new InvalidOperationException("This collection is read-only.");
				
				using (AutoJSContext context = new AutoJSContext())
				{
					for (int i = Count - 1; i >= 0; i--)
						StyleSheet._DomStyleSheet.DeleteRule(i);
				}
			}
			
			#region IEnumerable<GeckoStyleRule> Members
			
			/// <summary>
			/// Returns an IEnumerator which can enumerate through the rules in the collection.
			/// </summary>
			/// <returns></returns>
			public IEnumerator<GeckoStyleRule> GetEnumerator()
			{
				int length = Count;
				for (int i = 0; i < length; i++)
				{
					yield return GeckoStyleRule.Create((nsIDOMCSSRule)List.Item(i));
				}
			}
			
			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
			{
				foreach (GeckoStyleRule element in this)
					yield return element;
			}

			#endregion
		}
		#endregion
	}
	
	/// <summary>
	/// Represents a CSS rule in a <see cref="GeckoStyleSheet"/>.
	/// </summary>
	public class GeckoStyleRule
	{
		GeckoStyleRule(nsIDOMCSSRule rule)
		{
			_DomStyleRule = rule;
		}
		
		internal static GeckoStyleRule Create(nsIDOMCSSRule rule)
		{
			return (rule == null) ? null : new GeckoStyleRule(rule);
		}
		
		/// <summary>
		/// Gets the underlying XPCOM object.
		/// </summary>
		public object DomStyleRule
		{
			get { return _DomStyleRule; }
		}
		nsIDOMCSSRule _DomStyleRule;
		
		/// <summary>
		/// Gets the selector text for this rule, or null if it is not a style rule; otherwise, null.
		/// </summary>
		public string SelectorText
		{
			get
			{
				nsIDOMCSSStyleRule rule = Xpcom.QueryInterface<nsIDOMCSSStyleRule>(DomStyleRule);
				if (rule != null)
				{
					return nsString.Get(rule.GetSelectorText);
				}
				return null;
			}
		}
		
		/// <summary>
		/// Gets this rule formatted as CSS text.
		/// </summary>
		public string CssText
		{
			get { return nsString.Get(_DomStyleRule.GetCssText); }
		}
		
		/// <summary>
		/// Gets or sets the style properties of this rule, if it is a style rule; otherwise, null.
		/// </summary>
		public string StyleCssText
		{
			get
			{
				nsIDOMCSSStyleRule rule = Xpcom.QueryInterface<nsIDOMCSSStyleRule>(DomStyleRule);
				if (rule != null)
				{
					return nsString.Get(rule.GetStyle().GetCssText);
				}
				return null;
			}
			set
			{
				nsIDOMCSSStyleRule rule = Xpcom.QueryInterface<nsIDOMCSSStyleRule>(DomStyleRule);
				if (rule != null)
				{
					nsString.Set(rule.GetStyle().SetCssText, value);
				}
				else
				{
					throw new InvalidOperationException("This rule does not support StyleCssText.");
				}
			}
		}
		
		/// <summary>
		/// Gets the <see cref="GeckoStyleSheet"/> which contains this rule.
		/// </summary>
		public GeckoStyleSheet ParentStyleSheet
		{
			get { return GeckoStyleSheet.Create((nsIDOMCSSStyleSheet)_DomStyleRule.GetParentStyleSheet()); }
		}
		
		/// <summary>
		/// Gets the <see cref="GeckoStyleSheet"/> which this rule imports, if it is an @import rule; otherwise, null.
		/// </summary>
		public GeckoStyleSheet ImportedStyleSheet
		{
			get
			{
				nsIDOMCSSImportRule rule = Xpcom.QueryInterface<nsIDOMCSSImportRule>(DomStyleRule);
				if (rule != null)
				{
					return GeckoStyleSheet.Create((nsIDOMCSSStyleSheet)rule.GetStyleSheet());
				}
				return null;
			}
		}
		
		/// <summary>
		/// Gets the HREF of the style sheet imported by this rule, if it is an @import rule; otherwise, null.
		/// </summary>
		public string ImportedHref
		{
			get
			{
				nsIDOMCSSImportRule rule = Xpcom.QueryInterface<nsIDOMCSSImportRule>(DomStyleRule);
				if (rule != null)
				{
					return nsString.Get(rule.GetHref);
				}
				return null;
			}
		}
		
		/// <summary>
		/// Gets the media type of the style sheet imported by this rule, if it is an @import rule; otherwise, null.
		/// </summary>
		public GeckoMediaList ImportedMedia
		{
			get
			{
				nsIDOMCSSImportRule rule = Xpcom.QueryInterface<nsIDOMCSSImportRule>(DomStyleRule);
				if (rule != null)
				{
					return new GeckoMediaList(rule.GetMedia());
				}
				return null;
			}
		}
		
		/// <summary>
		/// Gets the <see cref="GeckoRuleType"/> of this rule.
		/// </summary>
		public GeckoRuleType RuleType
		{
			get { return (GeckoRuleType)_DomStyleRule.GetType(); }
		}
		
		public override string ToString()
		{
			return this.CssText;
		}
	}
	
	/// <summary>
	/// Represents a list of media types.
	/// </summary>
	public class GeckoMediaList : IEnumerable<string>
	{
		internal GeckoMediaList(nsIDOMMediaList mediaList)
		{
			this.MediaList = mediaList;
		}
		nsIDOMMediaList MediaList;
		
		/// <summary>
		/// Gets the number of mediums in the list.
		/// </summary>
		public int Count
		{
			get { return MediaList.GetLength(); }
		}
		
		/// <summary>
		/// Returns the medium at the given index in the list.
		/// </summary>
		/// <param name="index"></param>
		/// <returns></returns>
		public string this[int index]
		{
			get
			{
				if (index < 0 || index >= Count)
					throw new ArgumentOutOfRangeException("index");
				
				using (nsAString str = new nsAString())
				{
					MediaList.Item(index, str);
					return str.ToString();
				}
			}
		}
		
		/// <summary>
		/// Appends the specified medium to the list.
		/// </summary>
		/// <param name="medium"></param>
		public void AppendMedium(string medium)
		{
			MediaList.AppendMedium(new nsAString(medium));
		}
		
		/// <summary>
		/// Deletes the specified medium from the list.
		/// </summary>
		/// <param name="medium"></param>
		public void DeleteMedium(string medium)
		{
			MediaList.DeleteMedium(new nsAString(medium));
		}
		
		/// <summary>
		/// Gets or sets the complete list of mediums as a single string.
		/// </summary>
		public string MediaText
		{
			get { return nsString.Get(MediaList.GetMediaText); }
			set { nsString.Set(MediaList.SetMediaText, value); }
		}

		public override string ToString()
		{
			return MediaText;
		}
		
		#region IEnumerable<string> Members

		public IEnumerator<string> GetEnumerator()
		{
			int length = this.Count;
			for (int i = 0; i < length; i++)
			{
				yield return this[i];
			}
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			foreach (string str in this)
				yield return str;
		}

		#endregion
	}
	
	/// <summary>
	/// Specifies the various types of rules for a <see cref="GeckoStyleRule"/>.
	/// </summary>
	public enum GeckoRuleType
	{
		Unknown = 0,
		Style = 1,
		CharSet = 2,
		Import = 3,
		Media = 4,
		FontFace = 5,
		Page = 6,
	}
}
