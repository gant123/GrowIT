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
}

public class ClientService : BaseApiService, IClientService
{
    private const string Endpoint = "api/clients";

    public ClientService(HttpClient http) : base(http) { }

    public async Task<List<ClientDto>> GetAllClientsAsync()
    {
        // Your controller returns ActionResult<List<ClientDto>> on GET api/clients
        return await _http.GetFromJsonAsync<List<ClientDto>>(Endpoint) 
               ?? new List<ClientDto>();
    }

    public async Task<ClientDetailDto> GetClientDetailAsync(Guid id)
    {
        // GET api/clients/{id}
        return await _http.GetFromJsonAsync<ClientDetailDto>($"{Endpoint}/{id}")
               ?? throw new Exception("Client not found");
    }

    public async Task<Guid> CreateClientAsync(CreateClientRequest request)
    {
        // POST api/clients
        var response = await _http.PostAsJsonAsync(Endpoint, request);
        response.EnsureSuccessStatusCode();
        
        // Assuming your API returns { Message = "...", ClientId = "..." }
        var result = await response.Content.ReadFromJsonAsync<CreateResponse>();
        return result?.ClientId ?? Guid.Empty;
    }

    public async Task AddFamilyMemberAsync(Guid clientId, CreateFamilyMemberRequest request)
    {
        // POST api/clients/{id}/members
        var response = await _http.PostAsJsonAsync($"{Endpoint}/{clientId}/members", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FamilyMemberProfileDto> GetMemberProfileAsync(Guid memberId)
    {
        // GET api/clients/members/{memberId}
        return await _http.GetFromJsonAsync<FamilyMemberProfileDto>($"{Endpoint}/members/{memberId}")
               ?? throw new Exception("Member not found");
    }

    // Helper class to catch the anonymous object return { ClientId = ... }
    private class CreateResponse { public Guid ClientId { get; set; } }
}