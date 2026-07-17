using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace API.Extensions;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueViolation(this DbUpdateException e) => e.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation
    };
}
