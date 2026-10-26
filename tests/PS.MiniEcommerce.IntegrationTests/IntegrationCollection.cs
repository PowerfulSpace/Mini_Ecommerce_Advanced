using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<TestDatabaseFixture>
{
}
