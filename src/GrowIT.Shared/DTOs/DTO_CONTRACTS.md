# DTO Contracts (Current State)

## Active API Contracts (used by current controllers/client)
- `AuthDtos.cs`
- `AuthResponseDto.cs`
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
- `ImprintListDto.cs`
- `ImprintResponseDto.cs`
- `InvestmentApiDtos.cs`
- `InvestmentDetailDto.cs` (partially populated by current API)
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
- `ImprintListDto` and `InvestmentDetailDto` contain fields not fully populated by current backend projections yet.
- `ReportsController` and `GrowthPlansController` are now DB-backed (EF Core), not in-memory.
- Prefer adding new request/response contracts to `GrowIT.Shared/DTOs` instead of defining classes inside controllers or client services.
