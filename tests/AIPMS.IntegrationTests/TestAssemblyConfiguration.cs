using Xunit;

// Integration tests share a SQL database and a test-current-user accessor.
// Serial execution prevents one flow from deleting or replacing another flow's data.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
