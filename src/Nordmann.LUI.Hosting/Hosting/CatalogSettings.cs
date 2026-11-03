namespace Nordmann.LUI.Hosting;

/// <summary>
/// The settings of the catalog that <c>AddLui</c> registered, under a type of LUI's own: a <see cref="LuiCatalogOptions"/>
/// the application registers for a catalog of its own does not change the one of the container (review of pull request
/// 23, N4).
/// </summary>
/// <param name="Options">The settings, as <c>AddLui</c> was given them.</param>
internal sealed record CatalogSettings(LuiCatalogOptions Options);
