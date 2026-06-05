namespace copilot_auto_byok.Models;

public class ApiKeyConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
