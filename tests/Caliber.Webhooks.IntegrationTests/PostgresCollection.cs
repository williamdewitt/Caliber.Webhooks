using System.Diagnostics.CodeAnalysis;

namespace Caliber.Webhooks.IntegrationTests;

/// <summary>
/// Shares one Postgres container across every integration test class, so the whole suite spins a single
/// container per run. A marker type only — xUnit binds it by the <see cref="Name"/> on
/// <c>[Collection(...)]</c>.
/// </summary>
[CollectionDefinition(Name)]
[SuppressMessage(
    "Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection-definition marker; the 'Collection' suffix is the framework convention.")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
