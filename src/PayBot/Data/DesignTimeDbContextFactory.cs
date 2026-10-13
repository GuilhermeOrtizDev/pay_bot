using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PayBot.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PayBotDbContext>
{
    public PayBotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PayBotDbContext>()
            .UseNpgsql("Host=localhost;Database=paybot;Username=paybot;Password=paybot")
            .Options;
        return new PayBotDbContext(options);
    }
}
