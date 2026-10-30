namespace Mognet.Bot.Core.Storage
{
    /// <summary>One schema change. <paramref name="Sql"/> may hold several statements and runs in a single transaction.</summary>
    public record Migration(int Version, string Name, string Sql);
}
