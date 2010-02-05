#region ***** BEGIN LICENSE BLOCK *****
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
using System.Text;

namespace Skybound.Gecko
{
	public delegate void GeckoDomEventHandler(Object sender, GeckoDomEventArgs e);
	
	/// <summary>
	/// Provides data about a DOM event.
	/// </summary>
	public class GeckoDomEventArgs : EventArgs
	{
		internal GeckoDomEventArgs(nsIDOMEvent ev)
		{
			_Event = ev;
		}
		
		nsIDOMEvent _Event;
		
		/// <summary>
		/// Gets or sets whether the event was handled.  Setting this property prevents the target DOM object
		/// from receiving the event (if Cancelable is true).
		/// </summary>
		public bool Handled
		{
			get { return _Handled; }
			set { _Handled = value; }
		}
		bool _Handled;
		
		public bool Bubbles
		{
			get { return _Event.GetBubbles(); }
		}
		
		public bool Cancelable
		{
			get { return _Event.GetCancelable(); }
		}
		
		public GeckoElement CurrentTarget
		{
			get { return GeckoElement.Create(Xpcom.QueryInterface<nsIDOMHTMLElement>(_Event.GetCurrentTarget())); }
		}
		
		/// <summary>
		/// Gets the final destination of the event.
		/// </summary>
		public GeckoElement Target
		{
			get { return GeckoElement.Create(Xpcom.QueryInterface<nsIDOMHTMLElement>(_Event.GetTarget())); }
		}
	};
	
	public class GeckoDomUIEventArgs : GeckoDomEventArgs
	{
		internal GeckoDomUIEventArgs(nsIDOMUIEvent ev) : base((nsIDOMEvent)ev)
		{
			_Event = ev;
		}
		
		nsIDOMUIEvent _Event;
		
		public int Detail
		{
			get { return _Event.GetDetail(); }
		}
	};
	
	public delegate void GeckoDomKeyEventHandler(Object sender, GeckoDomKeyEventArgs e);
	
	/// <summary>
	/// Provides data about a DOM key event.
	/// </summary>
	public class GeckoDomKeyEventArgs : GeckoDomUIEventArgs
	{
		internal GeckoDomKeyEventArgs(nsIDOMKeyEvent ev) : base((nsIDOMUIEvent)ev)
		{
			_Event = ev;
		}
		
		nsIDOMKeyEvent _Event;
		
		public uint KeyChar
		{
			get { return _Event.GetCharCode(); }
		}
		
		public uint KeyCode
		{
			get { return _Event.GetKeyCode(); }
		}
		
		public bool AltKey
		{
			get { return _Event.GetAltKey(); }
		}
		
		public bool CtrlKey
		{
			get { return _Event.GetCtrlKey(); }
		}
		
		public bool ShiftKey
		{
			get { return _Event.GetShiftKey(); }
		}
	};
	
	public delegate void GeckoDomMouseEventHandler(object sender, GeckoDomMouseEventArgs e);
	
	/// <summary>
	/// Provides data about a DOM mouse event.
	/// </summary>
	public class GeckoDomMouseEventArgs : GeckoDomUIEventArgs
	{
		internal GeckoDomMouseEventArgs(nsIDOMMouseEvent ev) : base((nsIDOMUIEvent)ev)
		{
			_Event = ev;
		}
		
		nsIDOMMouseEvent _Event;
		
		public int ClientX
		{
			get { return _Event.GetClientX(); }
		}
		
		public int ClientY
		{
			get { return _Event.GetClientY(); }
		}
		
		public int ScreenX
		{
			get { return _Event.GetScreenX(); }
		}
		
		public int ScreenY
		{
			get { return _Event.GetScreenY(); }
		}
		
		public ushort Button
		{
			get { return _Event.GetButton(); }
		}
		
		public bool AltKey
		{
			get { return _Event.GetAltKey(); }
		}
		
		public bool CtrlKey
		{
			get { return _Event.GetCtrlKey(); }
		}
		
		public bool ShiftKey
		{
			get { return _Event.GetShiftKey(); }
		}
	};
}
