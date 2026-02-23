# DTO Contracts (Current State)

## Active API Contracts (used by current controllers/client)
- `AuthDtos.cs`
- `AuthResponseDto.cs`
- `AdminManagementDtos.cs`
- `ClientDto.cs`
- `ClientDetailDto.cs`
- `CreateClientRequest.cs`
- `CreateFamilyMemberRequest.cs`
- `CreateFundRequest.cs`
- `CreateHouseholdRequest.cs`
- `CreateImprintRequest.cs`
- `CreateInvestmentRequest.cs`
- `CreateProgramRequest.cs`
- `DashboardStatsDto.cs`
- `FamilyMemberProfileDto.cs`
- `FundDto.cs`
- `HouseholdDtos.cs`
- `ImprintListDto.cs`
- `ImprintResponseDto.cs`
- `InvestmentApiDtos.cs`
- `InvestmentDetailDto.cs` (v1-minimal detail contract)
- `InvestmentListDto.cs`
- `PaginatedResult.cs`
- `ProgramDto.cs`
- `RecentReport.cs`
- `ReportManagementDtos.cs`
- `ScheduledReport.cs`
- `UpdateFundRequest.cs`

## Planned / Partial / Not Fully Implemented Yet
- `GrowthPlanListDto.cs` (API implemented and persisted, but still minimal v1 model)
- `GrowthPlanManagementDtos.cs` (API implemented and persisted, but still minimal v1 workflow)
- `InvestmentCreateDto.cs`
- `InvestmentUpdateDto.cs`
- `InvestmentSummaryByCategory.cs`
- `InvestmentSummaryByFundingSource.cs`

## Notes
- `ImprintListDto` and `InvestmentDetailDto` were trimmed to the current v1 API payloads to avoid contract drift.
- `ReportsController` and `GrowthPlansController` are now DB-backed (EF Core), not in-memory.
- Prefer adding new request/response contracts to `GrowIT.Shared/DTOs` instead of defining classes inside controllers or client services.
