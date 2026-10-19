namespace PayBot.Domain;

public sealed class Tenant
{
    public static readonly Guid DefaultId = new("7f1c2d3e-0a4b-4c5d-9e6f-000000000001");

    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
