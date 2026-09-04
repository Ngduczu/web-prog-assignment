using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class Users
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string Fullname { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? PasswordHash { get; private set;  }
    public Role Role { get; private set; }
    public AccountStatus Status  { get; private set; }
    public string? AvatarUrl { get; private set; }
    public DateTime CreatedOn { get; private set; }
    public DateTime ModifiedOn { get; private set; }
    
    // Audit
    public Guid? CreatedById { get; private set; }
    public Guid? ModifiedById { get; private set; }
    public Users? CreatedBy { get; private set; }
    public Users? ModifiedBy { get; private set; }
    
    private Users(){}

    private void Touch()
    {
        ModifiedOn = DateTime.UtcNow;
    }
    
    public Users(
        string email,
        string fullname,
        string? phone,
        string? passwordHash,
        Role role,
        AccountStatus status,
        Guid? createdById = null)
    {
        Id = Guid.NewGuid();
        
        Fullname = fullname;
        Email = email;
        Phone = phone;
        PasswordHash = passwordHash;
        Role = role;
        Status = status;
        
        CreatedOn = DateTime.UtcNow;
        ModifiedOn = DateTime.UtcNow;

        CreatedById = createdById;
    }

    public void SetFullname(string fullname)
    {
        Fullname = fullname;
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