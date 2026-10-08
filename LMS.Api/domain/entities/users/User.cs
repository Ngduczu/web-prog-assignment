using LMS.Api.domain.Enums.Users;

namespace LMS.Api.domain.Entities.Users;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? PasswordHash { get; private set;  }
    public long AuthVersion { get; private set; }
    public Role Role { get; private set; }
    public AccountStatus Status  { get; private set; }
    public string? AvatarUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    
    // Audit
    public Guid? CreatedById { get; private set; }
    public Guid? ModifiedById { get; private set; }
    public User? CreatedBy { get; private set; }
    public User? ModifiedBy { get; private set; }
    
    private User(){}

    private void Touch()
    {
        ModifiedAt = DateTime.UtcNow;
    }
    
    public User(
        string email,
        string fullName,
        string? phone,
        string? passwordHash,
        Role role,
        AccountStatus status,
        Guid? createdById = null)
    {
        Id = Guid.NewGuid();
        
        FullName = fullName;
        Email = email;
        Phone = phone;
        PasswordHash = passwordHash;
        AuthVersion = 1;
        Role = role;
        Status = status;
        
        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;

        CreatedById = createdById;
    }

    public void SetFullName(string fullName)
    {
        FullName = fullName;
        Touch();
    }

    public void SetPhone(string phone)
    {
        Phone = phone;
        Touch();
    }
    
    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        Touch();
    }

    public void RevokeSessions()
    {
        checked
        {
            AuthVersion++;
        }

        Touch();
    }

    public void RestorePasswordSecurityState(string passwordHash, long authVersion)
    {
        if (authVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(authVersion));

        PasswordHash = passwordHash;
        AuthVersion = authVersion;
        Touch();
    }

    public void SetRole(Role role)
    {
        Role = role;
        Touch();
    }

    public void SetStatus(AccountStatus status)
    {
        Status = status;
        Touch();
    }
    public void SetAvatarUrl(string avatarUrl)
    {
        AvatarUrl = avatarUrl;
        Touch();
    }

    public void SetModifiedBy(Guid? modifiedById)
    {
        ModifiedById = modifiedById;
    }
}
