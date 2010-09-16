using System;
using System.Collections.Generic;

namespace Bloom
{
	public class Event<TPayload>
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



	public class DeletePageCommand: Event<IPage>
	{}
	public class PageListChangedEvent : Event<object>
	{ }

	public class RelocatePageInfo
	{
		public IPage Page;
		public int IndexOfPageAfterMove;

		public RelocatePageInfo(IPage page, int indexOfPageAfterMove)
		{
			Page = page;
			IndexOfPageAfterMove = indexOfPageAfterMove;
		}
	}

	public class RelocatePageEvent : Event<RelocatePageInfo>
	{ }
}