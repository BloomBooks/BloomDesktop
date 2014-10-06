/* From original VB code by tetsushmz, translated to c# by AcelDG
 * From http://www.codeproject.com/Articles/32083/Displaying-a-ToolTip-when-the-Mouse-Hovers-Over-a
 * licensed under The Code Project Open License (CPOL)
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.ToPalaso
{

	[ProvideProperty("ToolTipWhenDisabled", typeof(Control)),
	ProvideProperty("SizeOfToolTipWhenDisabled", typeof(Control))]
	/// <summary>
	/// EnhancedToolTip supports the ToolTipWhenDisabled and SizeOfToolTipWhenDisabled
	/// extender properties that can be used to show tooltip messages when the associated
	/// control is disabled.
	/// </summary>
	public class BetterToolTip : ToolTip, IMultiStringContainer
	{
		#region ========== Required constructor ==========
		// This constructor is required for the Windows Forms Designer to instantiate
		// an object of this class with New(Me.components).
		// To verify this, just remove this constructor. Build it and then put the
		// component on a form. Take a look at the Designer.vb file for InitializeComponents(),
		// and search for the line where it instantiates this class.
		public BetterToolTip(IContainer p_container)
			: base()
		{
			// Required for Windows.Forms Class Composition Designer support
			if (p_container != null)
			{
				p_container.Add(this);
			}

			// Suscribes to Popup event listener
			// Comment out following lines if you are okay with the same Title/Icon for disabled controls.
			this.m_EvtPopup = new PopupEventHandler(EnhancedToolTip_Popup);
			this.Popup += this.m_EvtPopup;
		}
		#endregion

		#region ========== ToolTipWhenDisabled extender property support ==========
		private Dictionary<Control, string> m_ToolTipWhenDisabled = new Dictionary<Control, string>();
		private Dictionary<Control, BetterTooltipTransparentOverlay> m_BetterTooltipTransparentOverlay = new Dictionary<Control, BetterTooltipTransparentOverlay>();
		private Dictionary<Control, PaintEventHandler> m_EvtControlPaint = new Dictionary<Control, PaintEventHandler>();
		private Dictionary<Control, EventHandler> m_EvtControlEnabledChanged = new Dictionary<Control, EventHandler>();

		public void SetToolTipWhenDisabled(Control p_control, string value)
		{
			if (p_control == null)
			{
				throw new ArgumentNullException("control");
			}

			if (!String.IsNullOrEmpty(value))
			{
				this.m_ToolTipWhenDisabled[p_control] = value;
				if (!p_control.Enabled)
				{
					// When the control is disabled at design time, the EnabledChanged
					// event won't fire. So, on the first Paint event, we should call
					// PutOnBetterTooltipTransparentOverlay().
					m_EvtControlPaint.Add(p_control, new PaintEventHandler(control_Paint));
					p_control.Paint += m_EvtControlPaint[p_control];
				}
				m_EvtControlEnabledChanged.Add(p_control, new EventHandler(control_EnabledChanged));
				p_control.EnabledChanged += m_EvtControlEnabledChanged[p_control];
			}
			else
			{
				m_ToolTipWhenDisabled.Remove(p_control);
				if (m_EvtControlEnabledChanged != null)
				{
					p_control.EnabledChanged -= m_EvtControlEnabledChanged[p_control];
					m_EvtControlEnabledChanged.Remove(p_control);
					m_EvtControlPaint.Remove(p_control);
				}
			}
		}

		private void control_Paint(object sender, PaintEventArgs e)
		{
			Control l_control;

			l_control = (Control)sender;
			this.PutOnBetterTooltipTransparentOverlay(l_control);
			// Immediately remove the handler because we don't need it any longer.
			if (m_EvtControlPaint != null)
			{
				l_control.Paint -= m_EvtControlPaint[l_control];
			}
		}

		private void control_EnabledChanged(object sender, EventArgs e)
		{
			Control l_control;

			l_control = (Control)sender;
			if (l_control.Enabled)
			{
				this.TakeOffBetterTooltipTransparentOverlay(l_control);
			}
			else
			{
				this.PutOnBetterTooltipTransparentOverlay(l_control);
			}
		}

		[Category("Misc"),
		Description("Determines the ToolTip shown when the mouse hovers over the disabled control."),
		Localizable(true),
		Editor("MultilineStringEditor", "UITypeEditor"),
		DefaultValue("")]
		public string GetToolTipWhenDisabled(Control p_control)
		{
			if (p_control == null)
			{
				throw new ArgumentNullException("control");
			}

			if (m_ToolTipWhenDisabled.ContainsKey(p_control))
			{
				return m_ToolTipWhenDisabled[p_control];
			}
			else
			{
				return String.Empty;
			}
		}

		private void PutOnBetterTooltipTransparentOverlay(Control p_control)
		{
			BetterTooltipTransparentOverlay l_ts;

			l_ts = new BetterTooltipTransparentOverlay();
			l_ts.Location = p_control.Location;
			if (m_SizeOfToolTipWhenDisabled.ContainsKey(p_control))
			{
				l_ts.Size = this.m_SizeOfToolTipWhenDisabled[p_control];
			}
			else
			{
				l_ts.Size = p_control.Size;
			}
			p_control.Parent.Controls.Add(l_ts);
			l_ts.BringToFront();
			this.m_BetterTooltipTransparentOverlay[p_control] = l_ts;
			this.SetToolTip(l_ts, m_ToolTipWhenDisabled[p_control]);
		}

		private void TakeOffBetterTooltipTransparentOverlay(Control p_control)
		{
			BetterTooltipTransparentOverlay l_ts;

			if (m_BetterTooltipTransparentOverlay.ContainsKey(p_control))
			{
				l_ts = m_BetterTooltipTransparentOverlay[p_control];
				p_control.Parent.Controls.Remove(l_ts);
				this.SetToolTip(l_ts, String.Empty);
				l_ts.Dispose();
				m_BetterTooltipTransparentOverlay.Remove(p_control);
			}
		}
		#endregion

		#region L10NSharp IMultiStringContainer support

		internal L10NSharp.UI.L10NSharpExtender L10NSharpExt { get; set; }

		private static string NORMAL_TIP = ".ToolTip";
		private static string DISABLED_TIP = ".ToolTipWhenDisabled";

		/// <summary>
		/// Allows the BetterToolTip to give L10NSharp the information it needs to put strings
		/// into the localization UI to be localized.
		/// </summary>
		/// <returns>A list of LocalizingInfo objects</returns>
		public IEnumerable<LocalizingInfo> GetAllLocalizingInfoObjects()
		{
			var result = new List<LocalizingInfo>();
			foreach (var kvp in m_ToolTipWhenDisabled)
			{
				var ctrl = kvp.Key;
				var idPrefix = L10NSharpExt.GetLocalizingId(ctrl);
				var normalTip = GetToolTip(ctrl);
				if (!string.IsNullOrEmpty(normalTip))
				{
					var liNormal = new LocalizingInfo(ctrl, idPrefix + NORMAL_TIP)
						{Text = normalTip, Category = LocalizationCategory.MultiStringContainer};
					result.Add(liNormal);
				}
				var disabledTip = GetToolTipWhenDisabled(ctrl);
				if (!string.IsNullOrEmpty(disabledTip))
				{
					var liDisabled = new LocalizingInfo(ctrl, idPrefix + DISABLED_TIP)
						{ Text = disabledTip, Category = LocalizationCategory.MultiStringContainer };
					result.Add(liDisabled);
				}
			}
			return result;
		}

		/// <summary>
		/// L10NSharp sends the localized string back to the IMultiStringContainer to be
		/// applied, since L10NSharp doesn't know the internal workings of the container.
		/// We assume that the container is a collection of subcontrols that have string
		/// ids that need localizing.
		/// </summary>
		/// <param name="obj">somewhere in this control is a string to be localized</param>
		/// <param name="id">a key into the subControl allowing it to know what string to localize</param>
		/// <param name="localization">the actual localized string</param>
		public void ApplyLocalizationToString(object obj, string id, string localization)
		{
			if ((obj as Control) == null || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(localization))
				return;

			var subControl = obj as Control;
			var normalTip = GetToolTip(subControl);
			SetToolTip(subControl, null); // setting the tooltip to null helps us get it to refresh dynamically
			var isDisabledToolTip = id.EndsWith(DISABLED_TIP);
			if (isDisabledToolTip)
			{
				// setting an existing TipWhenDisabled throws a dictionary exception,
				// so we need to remove the existing one first
				SetToolTipWhenDisabled(subControl, null);
				SetToolTipWhenDisabled(subControl, localization);
				SetToolTip(subControl, normalTip);
			}
			else
			{
				SetToolTip(subControl, localization);
			}
		}

		#endregion

		#region ========== Support for the oversized transparent sheet to cover multiple visual controls. ==========
		private Dictionary<Control, Size> m_SizeOfToolTipWhenDisabled = new Dictionary<Control, Size>();

		public void SetSizeOfToolTipWhenDisabled(Control p_control, Size value)
		{
			if (p_control == null)
			{
				throw new ArgumentNullException("control");
			}

			if (!value.IsEmpty)
			{
				m_SizeOfToolTipWhenDisabled[p_control] = value;
			}
			else
			{
				m_SizeOfToolTipWhenDisabled.Remove(p_control);
			}
		}

		[Category("Misc"),
		Description("Determines the size of the ToolTip when the control is disabled." +
					 " Leave it to 0,0, unless you want the ToolTip to pop up over wider" +
					 " rectangular area than this control."),
		DefaultValue(typeof(Size), "0,0")]
		public Size GetSizeOfToolTipWhenDisabled(Control p_control)
		{
			if (p_control == null)
			{
				throw new ArgumentNullException("control");
			}

			if (m_SizeOfToolTipWhenDisabled.ContainsKey(p_control))
			{
				return m_SizeOfToolTipWhenDisabled[p_control];
			}
			else
			{
				return Size.Empty;
			}
		}
		#endregion

		#region ========== Comment out this region if you are okay with the same Title/Icon for disabled controls. ==========
		private PopupEventHandler m_EvtPopup;

		private string m_strToolTipTitle;
		/// <summary>
		/// ToolTip title when control is disabled
		/// </summary>
		public new string ToolTipTitle
		{
			get
			{
				return base.ToolTipTitle;
			}
			set
			{
				base.ToolTipTitle = value;
				this.m_strToolTipTitle = value;
			}
		}

		private ToolTipIcon m_ToolTipIcon;
		/// <summary>
		/// ToolTip icon when control is disabled
		/// </summary>
		public new ToolTipIcon ToolTipIcon
		{
			get
			{
				return base.ToolTipIcon;
			}
			set
			{
				base.ToolTipIcon = value;
				this.m_ToolTipIcon = value;
			}
		}

		private void EnhancedToolTip_Popup(object sender, PopupEventArgs e)
		{
			if (e.AssociatedControl is BetterTooltipTransparentOverlay)
			{
				base.ToolTipTitle = String.Empty;
				base.ToolTipIcon = ToolTipIcon.None;
			}
			else
			{
				base.ToolTipTitle = this.m_strToolTipTitle;
				base.ToolTipIcon = this.m_ToolTipIcon;
			}
		}

		protected override void Dispose(bool disposing)
		{
			// Removing Popup Handler.
			if (this.m_EvtPopup != null)
			{
				this.Popup -= this.m_EvtPopup;
			}
			base.Dispose(disposing);
		}
		#endregion
	}
}
