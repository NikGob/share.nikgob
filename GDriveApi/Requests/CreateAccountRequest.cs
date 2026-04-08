using System.ComponentModel.DataAnnotations;

namespace GDriveApi.Requests;

public class CreateAccountRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(admin|user)$", ErrorMessage = "Role must be 'admin' or 'user'.")]
    public string Role { get; set; } = "user";
}
