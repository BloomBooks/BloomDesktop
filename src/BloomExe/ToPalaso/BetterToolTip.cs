/* From original VB code by tetsushmz, translated to c# by AcelDG
 * From http://www.codeproject.com/Articles/32083/Displaying-a-ToolTip-when-the-Mouse-Hovers-Over-a
 * licensed under The Code Project Open License (CPOL)
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using L10NSharp;
using L10NSharp.UI;
using SIL.Code;

namespace Bloom.ToPalaso
{

	/// <summary>
	/// BetterToolTip supports the ToolTipWhenDisabled and SizeOfToolTipWhenDisabled
	/// extender properties that can be used to show tooltip messages when the associated
	/// control is disabled.
	/// </summary>
	[ProvideProperty("ToolTipWhenDisabled", typeof(Control))]
	[ProvideProperty("SizeOfToolTipWhenDisabled", typeof(Control))]
	public class BetterToolTip : ToolTip, ILocalizableComponent
	{
		private string _toolTipTitle;
		private ToolTipIcon _toolTipIcon;
		private Dictionary<Control, string> _toolTipWhenDisabled = new Dictionary<Control, string>();
		private List<Control> _allControlsHavingToolTips = new List<Control>();
		private Dictionary<Control, BetterTooltipTransparentOverlay> _betterTooltipTransparentOverlay = new Dictionary<Control, BetterTooltipTransparentOverlay>();
		private Dictionary<Control, PaintEventHandler> _paintEventHandlers = new Dictionary<Control, PaintEventHandler>();
		private Dictionary<Control, EventHandler> _controlEnabledChangedHandlers = new Dictionary<Control, EventHandler>();
		private Dictionary<Control, Size> _sizeOfToolTipWhenDisabledDictionary = new Dictionary<Control, Size>();


		#region ========== Required constructor ==========

		// This constructor is required for the Windows Forms Designer to instantiate
		// an object of this class.
		public BetterToolTip(IContainer container)
		{
			// Required for Windows.Forms Class Composition Designer support
			if (container != null)
			{
				container.Add(this);
			}

			// Suscribes to Popup event listener
			Popup += EnhancedToolTip_Popup;
		}

		#endregion

		#region ========== ToolTipWhenDisabled extender property support ==========

		public new void SetToolTip(Control control, string value)
		{
			if (control == null)
			{
				throw new ArgumentNullException("control");
			}

			UpdateAllControlsList(control, value);
			Guard.AgainstNull(control, "control");
			//No: this can be null, it's OK: Guard.AgainstNull(value,"value");
			try
			{
				base.SetToolTip(control, value);
			}
			catch (NullReferenceException)
			{
#if DEBUG
				Debug.Fail("Debug Only: If you just changed the UI language, this is a BL-938 Reproduction");
				//So I ran into this in 3.9. I could not find a pattern visible from the input parameters;
				//when it threw or didn't through, the control and value had strings, the control's container and parentContainer were always null
				//Just can't tell what is the null thing that it is complaining about. Would need to debug into the winforms source to find out.
#endif
				//for the user, swallow
			}
		}

		private void UpdateAllControlsList(Control control, string value)
		{
			if (String.IsNullOrEmpty(value))
			{
				if (_allControlsHavingToolTips.Contains(control))
					_allControlsHavingToolTips.Remove(control);
			}
			else
			{
				if (!_allControlsHavingToolTips.Contains(control))
					_allControlsHavingToolTips.Add(control);
			}
		}

		public void SetToolTipWhenDisabled(Control control, string value)
		{
			if (control == null)
			{
				throw new ArgumentNullException("control");
			}

			UpdateAllControlsList(control, value);

			if (!String.IsNullOrEmpty(value))
			{
				_toolTipWhenDisabled[control] = value;
				if (!control.Enabled)
				{
					// When the control is disabled at design time, the EnabledChanged
					// event won't fire. So, on the first Paint event, we should call
					// PutOnBetterTooltipTransparentOverlay().
					_paintEventHandlers.Add(control, new PaintEventHandler(control_Paint));
					control.Paint += _paintEventHandlers[control];
				}
				_controlEnabledChangedHandlers.Add(control, new EventHandler(control_EnabledChanged));
				control.EnabledChanged += _controlEnabledChangedHandlers[control];
			}
			else
			{
				_toolTipWhenDisabled.Remove(control);
				control.EnabledChanged -= _controlEnabledChangedHandlers[control];
				_controlEnabledChangedHandlers.Remove(control);
				_paintEventHandlers.Remove(control);
			}
		}

		private void control_Paint(object sender, PaintEventArgs e)
		{
			Control control;

			control = (Control)sender;
			PutOnBetterTooltipTransparentOverlay(control);
			// Immediately remove the handler because we don't need it any longer.
			if (_paintEventHandlers != null)
			{
				control.Paint -= _paintEventHandlers[control];
			}
		}

		private void control_EnabledChanged(object sender, EventArgs e)
		{
			Control control;

			control = (Control)sender;
			if (control.Enabled)
			{
				TakeOffBetterTooltipTransparentOverlay(control);
			}
			else
			{
				PutOnBetterTooltipTransparentOverlay(control);
			}
		}

		[Category("Misc")]
		[Description("Determines the ToolTip shown when the mouse hovers over the disabled control.")]
		[Localizable(true)]
		[Editor("MultilineStringEditor", "UITypeEditor")]
		[DefaultValue("")]
		public string GetToolTipWhenDisabled(Control control)
		{
			if (control == null)
			{
				throw new ArgumentNullException("control");
			}

			if (_toolTipWhenDisabled.ContainsKey(control))
				return _toolTipWhenDisabled[control];

			return String.Empty;
		}

		private void PutOnBetterTooltipTransparentOverlay(Control control)
		{
			var transparentOverlay = new BetterTooltipTransparentOverlay(control);
			transparentOverlay.Location = control.Location;
			if (_sizeOfToolTipWhenDisabledDictionary.ContainsKey(control))
			{
				transparentOverlay.Size = _sizeOfToolTipWhenDisabledDictionary[control];
			}
			else
			{
				transparentOverlay.Size = control.Size;
			}
			control.Parent.Controls.Add(transparentOverlay);
			transparentOverlay.BringToFront();
			_betterTooltipTransparentOverlay[control] = transparentOverlay;
			SetToolTip(transparentOverlay, _toolTipWhenDisabled[control]);
		}

		private void TakeOffBetterTooltipTransparentOverlay(Control control)
		{
			if (_betterTooltipTransparentOverlay.ContainsKey(control))
			{
				var transparentOverlay = _betterTooltipTransparentOverlay[control];
				control.Parent.Controls.Remove(transparentOverlay);
				SetToolTip(transparentOverlay, String.Empty);
				transparentOverlay.Dispose();
				_betterTooltipTransparentOverlay.Remove(control);
			}
		}

		#endregion

		#region L10NSharp ILocalizableComponent support

		private static string NORMAL_TIP = ".ToolTip";
		private static string DISABLED_TIP = ".ToolTipWhenDisabled";

		/// <summary>
		/// Allows the BetterToolTip to give L10NSharp the information it needs to put strings
		/// into the localization UI to be localized.
		/// </summary>
		/// <returns>A list of LocalizingInfo objects</returns>
		public IEnumerable<LocalizingInfo> GetAllLocalizingInfoObjects(L10NSharpExtender extender)
		{
			var result = new List<LocalizingInfo>();
			foreach (var ctrl in _allControlsHavingToolTips)
			{
				var idPrefix = extender.GetLocalizingId(ctrl);
				var normalTip = GetToolTip(ctrl);
				if (!string.IsNullOrEmpty(normalTip))
				{
					var liNormal = new LocalizingInfo(ctrl, idPrefix + NORMAL_TIP)
						{ Text = normalTip, Category = LocalizationCategory.LocalizableComponent };
					result.Add(liNormal);
				}
				var disabledTip = GetToolTipWhenDisabled(ctrl);
				if (!string.IsNullOrEmpty(disabledTip))
				{
					var liDisabled = new LocalizingInfo(ctrl, idPrefix + DISABLED_TIP)
						{ Text = disabledTip, Category = LocalizationCategory.LocalizableComponent };
					result.Add(liDisabled);
				}
			}
			return result;
		}

		/// <summary>
		/// L10NSharp will call this for each localized string so that the component can set
		/// the correct value in the control.
		/// </summary>
		/// <param name="control">The control that was returned via the LocalizingInfo in
		/// GetAllLocalizingInfoObjects(). Will be null if that value was null.</param>
		/// <param name="id">a key into the ILocalizableComponent allowing it to know what
		/// string to localize</param>
		/// <param name="localization">the actual localized string</param>
		public void ApplyLocalizationToString(object control, string id, string localization)
		{
			if ((control as Control) == null || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(localization))
				return;

			var subControl = control as Control;
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

		public void SetSizeOfToolTipWhenDisabled(Control control, Size value)
		{
			if (control == null)
			{
				throw new ArgumentNullException("control");
			}

			if (!value.IsEmpty)
			{
				_sizeOfToolTipWhenDisabledDictionary[control] = value;
			}
			else
			{
				_sizeOfToolTipWhenDisabledDictionary.Remove(control);
			}
		}

		[Category("Misc")]
		[Description("Determines the size of the ToolTip when the control is disabled." +
			" Leave it to 0,0, unless you want the ToolTip to pop up over wider" +
			" rectangular area than this control.")]
		[DefaultValue(typeof(Size), "0,0")]
		public Size GetSizeOfToolTipWhenDisabled(Control control)
		{
			if (control == null)
			{
				throw new ArgumentNullException("control");
			}

			if (_sizeOfToolTipWhenDisabledDictionary.ContainsKey(control))
			{
				return _sizeOfToolTipWhenDisabledDictionary[control];
			}
			else
			{
				return Size.Empty;
			}
		}

		#endregion

		#region ========== Comment out this region if you are okay with the same Title/Icon for disabled controls. ==========

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
				_toolTipTitle = value;
			}
		}

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
				_toolTipIcon = value;
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
				base.ToolTipTitle = _toolTipTitle;
				base.ToolTipIcon = _toolTipIcon;
			}
		}

		protected override void Dispose(bool disposing)
		{
			// Removing Popup Handler.
			if (disposing)
			{
				Popup -= EnhancedToolTip_Popup;
			}
			base.Dispose(disposing);
		}

		#endregion
	}
}
