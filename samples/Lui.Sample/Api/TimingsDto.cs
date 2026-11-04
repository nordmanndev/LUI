namespace Nordmann.LUI.Sample.Api;

/// <summary><see cref="LuiTimings"/> in milliseconds.</summary>
internal sealed record TimingsDto(double CandidateReadMs, double CorrectionMs, double RankingMs, double PayloadMs, double TotalMs);
