Definitions of the terminilogy "Fake", "Mock", "Stub", etc. are based on the Martin Fowler definitions.
Refer to https://www.martinfowler.com/articles/mocksArentStubs.html

Summary:
• Dummy objects are passed around but never actually used. Usually they are just used to fill parameter lists.
• Fake objects actually have working implementations, but usually take some shortcut which makes them not suitable for production (an in memory database is a good example).
• Stubs provide canned answers to calls made during the test, usually not responding at all to anything outside what's programmed in for the test.
• Spies are stubs that also record some information based on how they were called. One form of this might be an email service that records how many messages it was sent.
• Mocks are [...] objects pre-programmed with expectations which form a specification of the calls they are expected to receive
