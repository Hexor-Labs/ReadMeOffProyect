using HubNegocios.IdentityService.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HubNegocios.IdentityService.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(30);

        // 60 caracteres es lo que ocupa un hash de BCrypt. Nunca la contraseña.
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordLastChanged).HasColumnName("password_last_changed").IsRequired();

        builder.Property(u => u.ExternalId).HasColumnName("external_id").HasMaxLength(200);
        builder.Property(u => u.ExternalProvider).HasColumnName("external_provider").HasMaxLength(50);
        builder.Property(u => u.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(500);

        builder.Property(u => u.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        builder.Property(u => u.LastLogin).HasColumnName("last_login");
        builder.Property(u => u.LoginAttempts).HasColumnName("login_attempts").HasDefaultValue(0).IsRequired();
        builder.Property(u => u.LockedUntil).HasColumnName("locked_until");

        // Habeas Data (Ley 1581): hay que poder demostrar cuándo aceptó cada
        // usuario. Sin fecha, el consentimiento no se puede acreditar.
        builder.Property(u => u.TermsAcceptedAt).HasColumnName("terms_accepted_at");
        builder.Property(u => u.PrivacyPolicyAcceptedAt).HasColumnName("privacy_policy_accepted_at");

        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.CreatedBy).HasColumnName("created_by");
        builder.Property(u => u.UpdatedBy).HasColumnName("updated_by");

        /*
          Único por (tenant, correo), no por correo a secas. El mismo correo
          puede ser cliente de dos negocios distintos del hub, y son dos
          usuarios sin relación: un índice único global impediría que alguien se
          registrara en el segundo restaurante.
        */
        builder.HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique()
            .HasDatabaseName("ux_users_tenant_email");

        builder.HasIndex(u => new { u.TenantId, u.ExternalProvider, u.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL")
            .HasDatabaseName("ux_users_tenant_proveedor_externo");
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();

        /*
          Solo el HASH del refresh token, nunca el token.

          Si alguien consigue leer esta tabla —una copia de seguridad mal
          guardada, una inyección SQL, un volcado para depurar— con los tokens
          en claro se podría suplantar a cualquier usuario sin saber su
          contraseña. Con el hash, lo que se filtra no sirve para entrar.

          Es el mismo motivo por el que no se guardan contraseñas en claro, y se
          olvida mucho más a menudo.
        */
        builder.Property(s => s.RefreshTokenHash).HasColumnName("refresh_token_hash").HasMaxLength(100).IsRequired();

        builder.Property(s => s.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(s => s.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
        builder.Property(s => s.UserAgent).HasColumnName("user_agent").HasMaxLength(500);

        // La búsqueda al refrescar es por hash: tiene que estar indexada y ser
        // única, porque dos sesiones con el mismo hash no pueden existir.
        builder.HasIndex(s => s.RefreshTokenHash).IsUnique().HasDatabaseName("ux_sessions_refresh_hash");
        builder.HasIndex(s => new { s.UserId, s.ExpiresAt }).HasDatabaseName("ix_sessions_usuario_caducidad");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(500);

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(p => new { p.TenantId, p.Code })
            .IsUnique()
            .HasDatabaseName("ux_permissions_tenant_codigo");
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        // Clave compuesta: un permiso se concede a un rol dentro de un tenant, y
        // esa terna no se puede repetir. Una clave artificial aquí solo añadiría
        // una columna y dejaría la puerta abierta a duplicados.
        builder.HasKey(rp => new { rp.TenantId, rp.Role, rp.PermissionId });

        builder.Property(rp => rp.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(rp => rp.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(rp => rp.PermissionId).HasColumnName("permission_id").IsRequired();

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
