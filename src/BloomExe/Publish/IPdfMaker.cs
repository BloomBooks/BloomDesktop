using System.ComponentModel;

namespace Bloom.Publish
{
	public interface IPdfMaker
	{
		/// <summary>
		///
		/// </summary>
		/// <param name="inputHtmlPath"></param>
		/// <param name="outputPdfPath"></param>
		/// <param name="paperSizeName">A0,A1,A2,A3,A4,A5,A6,A7,A8,A9,B0,B1,B10,B2,B3,B4,B5,B6,B7,B8,B9,C5E,Comm10E,DLE,Executive,Folio,Ledger,Legal,Letter,Tabloid</param>
		/// <param name="landscape"> </param>
		/// <param name="booketLayoutMethod"> </param>
		/// <param name="bookletPortion"></param>
		/// <param name="doWorkEventArgs"> </param>
		/// <param name="getIsLandscape"></param>
		void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, PublishModel.BookletLayoutMethod booketLayoutMethod, PublishModel.BookletPortions bookletPortion, DoWorkEventArgs doWorkEventArgs);
	}
}