namespace Nordmann.LUI.Indexes;

/// <summary>
/// What both kinds of index have beyond reading (ADR-0073): the checks and the upkeep. The typed handles of the facade
/// hold an index as this, so that <c>LuiIndex&lt;TKey&gt;</c> can offer them to both sources without naming either.
/// </summary>
internal interface IMaintainedIndex : ILuiIndex
{
    LuiIntegrityReport Check();

    void WarmUpTypoDictionary(CancellationToken cancellationToken = default);

    void RebuildTypoDictionary(CancellationToken cancellationToken = default);

    void Optimize();
}
