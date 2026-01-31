using System.Net.Http.Json;
using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IClientService
{
    Task<List<ClientDto>> GetAllClientsAsync();
    Task<ClientDetailDto> GetClientDetailAsync(Guid id);
    Task<Guid> CreateClientAsync(CreateClientRequest request);
    Task AddFamilyMemberAsync(Guid clientId, CreateFamilyMemberRequest request);
    Task<FamilyMemberProfileDto> GetMemberProfileAsync(Guid memberId);
    Task UpdateFamilyMemberAsync(Guid memberId, CreateFamilyMemberRequest request);
    Task DeleteFamilyMemberAsync(Guid memberId);
}

public class ClientService : BaseApiService, IClientService
{
    private const string Endpoint = "api/clients";

    public ClientService(HttpClient http) : base(http) { }

    public async Task<List<ClientDto>> GetAllClientsAsync()
    {
        return await GetAsync<List<ClientDto>>(Endpoint) 
               ?? new List<ClientDto>();
    }

    public async Task<ClientDetailDto> GetClientDetailAsync(Guid id)
    {
        return await GetAsync<ClientDetailDto>($"{Endpoint}/{id}")
               ?? throw new Exception("Client not found");
    }

    public async Task<Guid> CreateClientAsync(CreateClientRequest request)
    {
        // POST api/clients
        var result = await PostAsync<CreateClientRequest, CreateResponse>(Endpoint, request);
        return result?.ClientId ?? Guid.Empty;
    }

    public async Task AddFamilyMemberAsync(Guid clientId, CreateFamilyMemberRequest request)
    {
        // POST api/clients/{id}/members
        await PostAsync($"{Endpoint}/{clientId}/members", request);
    }

    public async Task<FamilyMemberProfileDto> GetMemberProfileAsync(Guid memberId)
    {
        // GET api/clients/members/{memberId}
        return await GetAsync<FamilyMemberProfileDto>($"{Endpoint}/members/{memberId}")
               ?? throw new Exception("Member not found");
    }

    public async Task UpdateFamilyMemberAsync(Guid memberId, CreateFamilyMemberRequest request)
    {
        // PUT api/clients/members/{memberId}
        await PutAsync($"{Endpoint}/members/{memberId}", request);
    }

    public async Task DeleteFamilyMemberAsync(Guid memberId)
    {
        // DELETE api/clients/members/{memberId}
        await DeleteAsync($"{Endpoint}/members/{memberId}");
    }

    // Helper class to catch the anonymous object return { ClientId = ... }
    private class CreateResponse { public Guid ClientId { get; set; } }
}