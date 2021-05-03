using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BloomTests.DataBuilders
{
	/// <summary>
	/// Uses a DataBuilder pattern to help initialize TeamCollectionApi objects for testing
	/// Example 1: new TeamCollectionApiBuilder().Set[...](...).Build();
	/// </summary>
	class TeamCollectionApiBuilder
	{
		/// <summary>
		/// Creates a new builder object to facilitate setting up TeamCollectionApi
		/// </summary>
		public TeamCollectionApiBuilder()
		{
		}

		#region Basic Fields
		private CollectionSettings _collectionSettings;
		private BookSelection _bookSelection;
		private ITeamCollectionManager _teamCollectionManager;
		private BookServer _bookServer;
		private BloomWebSocketServer _bloomWebSocketServer;
		#endregion

		/// <summary>
		/// Use this when finished initializing.
		/// </summary>
		/// <returns>Returns a TeamCollectionApi object that you can use</returns>
		public TeamCollectionApi Build()
		{
			return new TeamCollectionApi(_collectionSettings, _bookSelection, _teamCollectionManager, _bookServer, _bloomWebSocketServer);
		}

		#region Defaults
		public void WithDefaultValues()
		{
			this._collectionSettings = new CollectionSettings();
			this._bookSelection = new BookSelection();
			this._teamCollectionManager = null;	// ENHANCE: This would be better off calling the builder for TeamCollectionManager, when it's implemented.
			this._bookServer = null;
			this._bloomWebSocketServer = null;
		}

		public TeamCollectionApiBuilder WithDefaultMocks()
		{
			this.MockCollectionSettings = new Mock<CollectionSettings>();
			this.MockBookSelection = new Mock<BookSelection>();
			this.MockTeamCollectionManager = new Mock<ITeamCollectionManager>();
			this._bookServer = null;
			this._bloomWebSocketServer = null;
			return this;
		}
		#endregion


		#region Basic Setters
		public TeamCollectionApiBuilder WithCollectionSettings(CollectionSettings collectionSettings)
		{
			_collectionSettings = collectionSettings;
			return this;
		}

		public TeamCollectionApiBuilder WithBookSelection(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
			return this;
		}

		public TeamCollectionApiBuilder WithTeamCollectionManager(ITeamCollectionManager teamCollectionManager)
		{
			_teamCollectionManager = teamCollectionManager;
			return this;
		}

		public TeamCollectionApiBuilder WithBookServer(BookServer bookServer)
		{
			_bookServer = bookServer;
			return this;
		}

		public TeamCollectionApiBuilder WithBloomWebSocketServer(BloomWebSocketServer bloomWebSocketServer)
		{
			_bloomWebSocketServer = bloomWebSocketServer;
			return this;
		}
		#endregion

		#region Mocks
		private Mock<CollectionSettings> _mockCollectionSettings;
		public Mock<CollectionSettings> MockCollectionSettings
		{
			get => _mockCollectionSettings;
			set
			{
				_mockCollectionSettings = value;
				WithCollectionSettings(value.Object);
			}
		}

		private Mock<BookSelection> _mockBookSelection;
		public Mock<BookSelection> MockBookSelection
		{
			get => _mockBookSelection;
			set
			{
				_mockBookSelection = value;
				WithBookSelection(value.Object);
			}
		}

		private Mock<ITeamCollectionManager> _mockTeamCollectionManager;
		public Mock<ITeamCollectionManager> MockTeamCollectionManager
		{
			get => _mockTeamCollectionManager;
			set
			{
				_mockTeamCollectionManager = value;
				WithTeamCollectionManager(value.Object);
			}
		}

		private Mock<BookServer> _mockBookServer;
		public Mock<BookServer> MockBookServer
		{
			get => _mockBookServer;
			set
			{
				_mockBookServer = value;
				WithBookServer(value.Object);
			}
		}

		private Mock<BloomWebSocketServer> _mockBloomWebSocketServer;
		public Mock<BloomWebSocketServer> MockBloomWebSocketServer
		{
			get => _mockBloomWebSocketServer;
			set
			{
				_mockBloomWebSocketServer = value;
				WithBloomWebSocketServer(value.Object);
			}
		}

		

		// ENHANCE: If desired, add WithMock[...] to support chaining With() statements
		#endregion
	}
}
