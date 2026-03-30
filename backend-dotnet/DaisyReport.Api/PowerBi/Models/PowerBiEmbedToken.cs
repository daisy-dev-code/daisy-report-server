namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiEmbedToken
{
    public string Token { get; set; } = "";
    public string TokenId { get; set; } = "";
    public DateTime Expiration { get; set; }
}
