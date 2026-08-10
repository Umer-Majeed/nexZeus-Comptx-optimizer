using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NexZeus
{
    public class CloudProfile
    {
        // WhenWritingNull: if we serialize "id": null explicitly, Postgres/PostgREST
        // treats that as an explicit NULL and will reject the insert if the id
        // column is NOT NULL with a default (e.g. uuid_generate_v4()) — the
        // explicit null overrides the default instead of falling back to it.
        // Omitting the field entirely lets the DB default kick in as intended.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        [JsonPropertyName("cpu")] public required string Cpu { get; set; }
        [JsonPropertyName("gpu")] public required string Gpu { get; set; }
        [JsonPropertyName("ram_gb")] public int RamGb { get; set; }
        [JsonPropertyName("dns_primary")] public string? DnsPrimary { get; set; }
        [JsonPropertyName("dns_secondary")] public string? DnsSecondary { get; set; }
        [JsonPropertyName("tweaks")] public List<string> Tweaks { get; set; } = [];
        [JsonPropertyName("fps_avg")] public double FpsAvg { get; set; }
        [JsonPropertyName("ping_avg")] public double PingAvg { get; set; }
        [JsonPropertyName("rating")] public double Rating { get; set; }
        [JsonPropertyName("votes")] public int Votes { get; set; }
        [JsonPropertyName("submitted_by")] public string? SubmittedBy { get; set; }
    }

    /// <summary>Talks to a Supabase table ("cloud_profiles") to share/browse community DNS+tweak configs.</summary>
    public class CloudProfileService
    {
        // TODO: fill these in from Supabase -> Project Settings -> API
        private const string BaseUrl = "https://zfqsyxlzpdodhlzzkqtb.supabase.co/rest/v1/cloud_profiles";
        private const string ApiKey = "sb_publishable_soczV162NttxhWQFPGcjMg_Pe4M345C";

        private readonly HttpClient _http;

        public CloudProfileService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("apikey", ApiKey);
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
        }

        public static string GetCpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                    return obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
            }
            catch { }
            return "Unknown CPU";
        }

        public static int GetRamGb()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                        return (int)Math.Round(bytes / 1024.0 / 1024.0 / 1024.0);
                }
            }
            catch { }
            return 0;
        }

        public async Task<List<CloudProfile>> GetMatchingProfilesAsync(string cpu, string gpu)
        {
            try
            {
                string url = $"{BaseUrl}?cpu=eq.{Uri.EscapeDataString(cpu)}&gpu=eq.{Uri.EscapeDataString(gpu)}&order=rating.desc&limit=20";
                var res = await _http.GetAsync(url);
                string body = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    Logger.LogError($"GetMatchingProfilesAsync failed: {(int)res.StatusCode} {res.StatusCode} — {body}");
                    return [];
                }

                return JsonSerializer.Deserialize<List<CloudProfile>>(body) ?? [];
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "GetMatchingProfilesAsync");
                return [];
            }
        }

        public async Task<List<CloudProfile>> GetTopProfilesAsync()
        {
            try
            {
                string url = $"{BaseUrl}?order=rating.desc&limit=20";
                var res = await _http.GetAsync(url);
                string body = await res.Content.ReadAsStringAsync();

                if (!res.IsSuccessStatusCode)
                {
                    Logger.LogError($"GetTopProfilesAsync failed: {(int)res.StatusCode} {res.StatusCode} — {body}");
                    return [];
                }

                return JsonSerializer.Deserialize<List<CloudProfile>>(body) ?? [];
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "GetTopProfilesAsync");
                return [];
            }
        }

        public async Task<bool> SubmitAsync(CloudProfile profile)
        {
            try
            {
                var content = new StringContent(JsonSerializer.Serialize(profile), Encoding.UTF8, "application/json");
                var res = await _http.PostAsync(BaseUrl, content);

                if (!res.IsSuccessStatusCode)
                {
                    string body = await res.Content.ReadAsStringAsync();
                    // This is the important line — it tells you WHY Supabase rejected
                    // the insert (RLS policy denial, NOT NULL violation, bad column
                    // name, etc). Check %UserProfile%\Documents\NexZeus\Logs\.
                    Logger.LogError($"SubmitAsync failed: {(int)res.StatusCode} {res.StatusCode} — {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "SubmitAsync");
                return false;
            }
        }

        public async Task<bool> RateAsync(string id, double newAverageRating, int newVoteCount)
        {
            try
            {
                var payload = new { rating = newAverageRating, votes = newVoteCount };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var req = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}?id=eq.{id}") { Content = content };
                var res = await _http.SendAsync(req);

                if (!res.IsSuccessStatusCode)
                {
                    string body = await res.Content.ReadAsStringAsync();
                    Logger.LogError($"RateAsync failed: {(int)res.StatusCode} {res.StatusCode} — {body}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "RateAsync");
                return false;
            }
        }
    }
}