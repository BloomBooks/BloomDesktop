using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Collection;

namespace Bloom.NewCollection
{
	public partial class LanguageIdControl : UserControl, IPageControl
	{
		public CollectionSettings _collectionInfo;
		private Action<UserControl, bool> _setNextButtonState;

		public LanguageIdControl()
		{
			InitializeComponent();
			_lookupISOControl.ISOCode = string.Empty;
			_selectedLanguage.Text = string.Empty;
		}

		private void OnLookupISOControlReadinessChanged(object sender, EventArgs e)
		{
			if (_collectionInfo == null)
				return;

			_collectionInfo.Language1Iso639Code = _lookupISOControl.ISOCode;
			_collectionInfo.Language1Name = _lookupISOControl.ISOCodeAndName==null? string.Empty :_lookupISOControl.ISOCodeAndName.Name;

			_setNextButtonState(this, _lookupISOControl.ISOCodeAndName != null);
			_selectedLanguage.Text = _collectionInfo.Language1Name;

		}

		public void Init(Action<UserControl, bool> setNextButtonState, CollectionSettings collectionInfo)
		{
			_setNextButtonState = setNextButtonState;
			_collectionInfo = collectionInfo;
			_lookupISOControl.ReadinessChanged += OnLookupISOControlReadinessChanged;
		}
		public void NowVisible()
		{
			_setNextButtonState(this, _lookupISOControl.ISOCodeAndName!=null);
		}

		private void _lookupISOControl_Leave(object sender, EventArgs e)
		{
			_setNextButtonState(this, _lookupISOControl.ISOCodeAndName != null);
		}
	}
}
