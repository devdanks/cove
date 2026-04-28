using Cove.PerformanceTests.Infrastructure;

namespace Cove.PerformanceTests;

[CollectionDefinition("performance", DisableParallelization = true)]
public sealed class PerformanceCollection : ICollectionFixture<PostgresPerformanceFixture>
{
}