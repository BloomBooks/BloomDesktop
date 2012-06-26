using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Library;

namespace Bloom
{
	public interface IEvent {}//hack for our autofact registration because of the generic I don't know how to select by actual event<>

	public class Event<TPayload> : IEvent
	{
		private readonly List<Action<TPayload>> _subscribers = new List<Action<TPayload>>();

		public void Subscribe(Action<TPayload> action)
		{
			if (!_subscribers.Contains(action))
			{
				_subscribers.Add(action);
			}
		}
		public void Raise(TPayload descriptor)
		{
			foreach (Action<TPayload> subscriber in _subscribers)
			{
				((Action<TPayload>)subscriber)(descriptor);
			}
		}
		public bool HasSubscribers
		{
			get { return _subscribers.Count > 0; }
		}
	}

	public class TabChangedDetails
	{
		public Control From;
		public Control To;
	}

	/// <summary>
	/// called before the actual change
	/// </summary>
	public class SelectedTabAboutToChangeEvent : Event<TabChangedDetails>
	{ }

	/// <summary>
	/// Gives the first control in the tab
	/// </summary>
	public class SelectedTabChangedEvent : Event<TabChangedDetails>
	{ }

	public class CreateFromSourceBookCommand: Event<Book.Book>
	{}


	/// <summary>
	/// called when the user is quiting or changing to another library
	/// </summary>
	public class LibraryClosing : Event<object>
	{ }


	public class EditBookCommand : Event<Book.Book>
	{ }


//	public class BookCollectionChangedEvent : Event<BookCollection>
//	{ }

	public class PageListChangedEvent : Event<object>
	{ }

	/// <summary>
	/// ANything displaying the book should re-load it.
	/// </summary>
	public class BookRefreshEvent : Event<Book.Book>
	{ }

	public class RelocatePageInfo
	{
		public IPage Page;
		public int IndexOfPageAfterMove;
		public bool Cancel;

		public RelocatePageInfo(IPage page, int indexOfPageAfterMove)
		{
			Page = page;
			IndexOfPageAfterMove = indexOfPageAfterMove;
		}
	}

	public class RelocatePageEvent : Event<RelocatePageInfo>
	{ }
}