using Microsoft.EntityFrameworkCore;
using TicketCore.Models.Access;
using TicketCore.Models.Organization;
using TicketCore.Models.Users;

namespace TicketCore.Data;

/// <summary>EF Core is used for writes and simple lookups; Dapper handles lists, trees and reports.</summary>
public class TicketDbContext(DbContextOptions<TicketDbContext> options) : DbContext(options)
{
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrgUnit>(e =>
        {
            e.ToTable("OrgUnit");
            e.HasKey(x => x.OrgUnitId);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("User");
            e.HasKey(x => x.UserId);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.DisplayName);
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("Permission");
            e.HasKey(x => x.PermissionKey);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Role");
            e.HasKey(x => x.RoleId);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermission");
            e.HasKey(x => new { x.RoleId, x.PermissionKey });
        });

        modelBuilder.Entity<RoleAssignment>(e =>
        {
            e.ToTable("RoleAssignment");
            e.HasKey(x => x.RoleAssignmentId);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshToken");
            e.HasKey(x => x.RefreshTokenId);
            e.Property(x => x.TokenHash).IsFixedLength();
            e.Property(x => x.ReplacedByTokenHash).IsFixedLength();
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(x => x.AuditLogId);
        });
    }
}
